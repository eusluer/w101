using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using w101.Api.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Logging configuration - Production'da da bilgi görebilmek için
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsProduction())
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

// Railway PORT environment variable'ı için
var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
Console.WriteLine($"Using PORT: {port}");
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// HTTP timeout ayarları
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// Kestrel server options - timeout ayarları
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
});

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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };
            
            // JWT authentication timeout ayarları
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.WriteLine($"JWT Authentication failed: {context.Exception.Message}");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Console.WriteLine($"JWT Token validated successfully for user");
                    return Task.CompletedTask;
                }
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
        
        // Timeout ve connection pool ayarları ekle
        connectionString = $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;Server Compatibility Mode=NoTypeLoading;Include Error Detail=true;Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=20;Minimum Pool Size=5;Connection Pruning Interval=10;Pooling=true";
        Console.WriteLine("PostgreSQL URL converted to connection string with optimization");
    }
    else
    {
        // Mevcut connection string'e timeout ayarları ekle
        if (!databaseUrl.Contains("Timeout="))
        {
            connectionString = databaseUrl + ";Timeout=30;Command Timeout=30;Connection Idle Lifetime=300;Maximum Pool Size=20;Minimum Pool Size=5;Connection Pruning Interval=10;Pooling=true";
        }
        else
        {
            connectionString = databaseUrl;
        }
        Console.WriteLine("Using direct connection string with optimization");
    }

    // Register services
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    builder.Services.AddSingleton(connectionString);
    builder.Services.AddScoped<JwtService>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<ProfileService>();

    Console.WriteLine("Services registered successfully");

    // Test database connection with retry logic
    var maxRetries = 3;
    var retryDelay = TimeSpan.FromSeconds(2);
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            using var testConnection = new NpgsqlConnection(connectionString);
            await testConnection.OpenAsync();
            
            // Test simple query
            using var command = new NpgsqlCommand("SELECT 1", testConnection);
            command.CommandTimeout = 10; // 10 saniye timeout
            await command.ExecuteScalarAsync();
            
            Console.WriteLine($"Database connection test successful (attempt {i + 1})");
            await testConnection.CloseAsync();
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database connection test failed (attempt {i + 1}): {ex.Message}");
            if (i == maxRetries - 1)
            {
                Console.WriteLine($"Connection string (masked): {connectionString.Substring(0, Math.Min(30, connectionString.Length))}...");
                Console.WriteLine("WARNING: All database connection attempts failed - application will continue but may have issues");
            }
            else
            {
                await Task.Delay(retryDelay);
            }
        }
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
    c.EnableTryItOutByDefault();
});
Console.WriteLine($"Swagger configured for {app.Environment.EnvironmentName}");

// Railway'de HTTPS redirect genellikle gerekli değil
// app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapGet("/", () => Results.Ok(new { message = "w101 API is running!", status = "healthy", timestamp = DateTime.UtcNow }));
app.MapGet("/health", () => Results.Ok(new { status = "OK", timestamp = DateTime.UtcNow, environment = app.Environment.EnvironmentName }));
app.MapGet("/test", () => Results.Ok(new { message = "Test successful", timestamp = DateTime.UtcNow }));

app.MapControllers();

Console.WriteLine("Application starting...");

// Railway için heartbeat - async olarak çalıştır
_ = Task.Run(async () =>
{
    try
    {
        while (true)
        {
            await Task.Delay(30000); // 30 saniye
            Console.WriteLine($"Heartbeat: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC - Application is running");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Heartbeat task failed: {ex.Message}");
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