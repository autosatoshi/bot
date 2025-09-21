# Use the official .NET 9 ASP.NET Core runtime image (multi-platform)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Use the official .NET 9 SDK image (multi-platform) to build the app
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy only the main project file
COPY src/Backend/AutoBot.csproj src/Backend/

# Restore dependencies for main project only
RUN dotnet restore src/Backend/AutoBot.csproj

# Copy only the source code (excluding tests)
COPY src/ src/

# Build the application
WORKDIR /src/src/Backend
RUN dotnet build AutoBot.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish AutoBot.csproj -c Release -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN addgroup -g 1001 -S appgroup && \
    adduser -S appuser -u 1001 -G appgroup

# Change ownership of the app directory
RUN chown -R appuser:appgroup /app
USER appuser

ENTRYPOINT ["dotnet", "AutoBot.dll"]