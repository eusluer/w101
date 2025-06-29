# Use the official .NET 8 SDK image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory
WORKDIR /app

# Copy the solution and project files
COPY w101.Api.sln ./
COPY w101.Api/w101.Api.csproj ./w101.Api/

# Restore dependencies
RUN dotnet restore w101.Api.sln

# Copy the entire source code
COPY . ./

# Build the application
RUN dotnet publish w101.Api/w101.Api.csproj -c Release -o out

# Use the official .NET 8 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Set the working directory
WORKDIR /app

# Copy the published application
COPY --from=build /app/out .

# Create directory for data protection keys
RUN mkdir -p /tmp/dataprotection-keys

# Set environment variable for ASP.NET Core
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# Expose the port
EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "w101.Api.dll"] 