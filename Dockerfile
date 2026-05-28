# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ForexBot.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime — aspnet image required for ASP.NET Core Web API
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Env vars are injected at runtime via .env file (see docker-compose.yml)

ENTRYPOINT ["dotnet", "ForexBot.dll"]
