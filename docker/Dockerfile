# Use the official .NET 8 runtime image for ARM64
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine-arm64v8 AS base
WORKDIR /app
EXPOSE 8080

# Use the official .NET 8 SDK image for ARM64 to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine-arm64v8 AS build
WORKDIR /src

# Copy solution file and project files
COPY AutoBot.sln ./
COPY src/Backend/AutoBot.csproj src/Backend/
COPY tests/AutoBot.Tests/AutoBot.Tests.csproj tests/AutoBot.Tests/

# Restore dependencies
RUN dotnet restore AutoBot.sln

# Copy the rest of the source code
COPY . .

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