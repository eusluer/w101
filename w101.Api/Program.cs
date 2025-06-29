using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using w101.Api.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Railway PORT environment variable'ı için
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Console.WriteLine($"Using PORT: {port}");
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
builder.Services.AddControllers();

// Configure Data Protection for production
try
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/tmp/dataprotection-keys"));
    Console.WriteLine("Data protection configured successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"Data protection configuration failed: {ex.Message}");
}

// JWT Authentication with error handling
try
{
    var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? builder.Configuration["Jwt:Secret"];
    var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"];
    var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"];

    if (string.IsNullOrEmpty(jwtSecret))
    {
        throw new InvalidOperationException("JWT Secret not configured");
    }
    if (string.IsNullOrEmpty(jwtIssuer))
    {
        throw new InvalidOperationException("JWT Issuer not configured");
    }
    if (string.IsNullOrEmpty(jwtAudience))
    {
        throw new InvalidOperationException("JWT Audience not configured");
    }

    Console.WriteLine($"JWT configured - Issuer: {jwtIssuer}, Audience: {jwtAudience}");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
            };
        });
}
catch (Exception ex)
{
    Console.WriteLine($"JWT configuration failed: {ex.Message}");
    throw;
}

builder.Services.AddAuthorization();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Swagger Configuration with JWT
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "w101 API", 
        Version = "v1",
        Description = "Wizard101 Game API with JWT Authentication"
    });

    // JWT Authentication için Swagger konfigürasyonu
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Database connection string - PostgreSQL URL formatını parse et
try
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? 
                      builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrEmpty(databaseUrl))
    {
        throw new InvalidOperationException("Database connection string not configured");
    }

    Console.WriteLine($"Database URL found: {databaseUrl.Substring(0, Math.Min(50, databaseUrl.Length))}...");

    string connectionString;
    if (databaseUrl.StartsWith("postgresql://"))
    {
        // PostgreSQL URL formatını Npgsql connection string formatına çevir
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var database = uri.AbsolutePath.TrimStart('/');
        
        connectionString = $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Server Compatibility Mode=NoTypeLoading;Include Error Detail=true";
        Console.WriteLine("PostgreSQL URL converted to connection string");
    }
    else
    {
        connectionString = databaseUrl;
        Console.WriteLine("Using direct connection string");
    }

    // Register services
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    builder.Services.AddSingleton(connectionString);
    builder.Services.AddScoped<JwtService>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<ProfileService>();

    Console.WriteLine("Services registered successfully");

    // Test database connection
    try
    {
        using var testConnection = new NpgsqlConnection(connectionString);
        testConnection.Open();
        Console.WriteLine("Database connection test successful");
        testConnection.Close();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: Database connection test failed: {ex.Message}");
        Console.WriteLine($"Connection string (masked): {connectionString.Substring(0, Math.Min(30, connectionString.Length))}...");
        Console.WriteLine("Application will continue without database connection - please fix database issue");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Database configuration failed: {ex.Message}");
    throw;
}

var app = builder.Build();

Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");

// Configure the HTTP request pipeline.
// Enable Swagger in all environments for API testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "w101 API v1");
    c.DisplayRequestDuration();
});
Console.WriteLine($"Swagger configured for {app.Environment.EnvironmentName}");

// Railway'de HTTPS redirect genellikle gerekli değil
// app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapGet("/", () => Results.Ok(new { message = "w101 API is running!", status = "healthy" }));
app.MapGet("/health", () => Results.Ok(new { status = "OK", timestamp = DateTime.UtcNow }));
app.MapGet("/test", () => Results.Ok(new { message = "Test successful", timestamp = DateTime.UtcNow }));

app.MapControllers();

Console.WriteLine("Application starting...");

// Railway için heartbeat
Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(30000); // 30 saniye
        Console.WriteLine($"Heartbeat: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC - Application is running");
    }
});

try
{
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    throw;
}