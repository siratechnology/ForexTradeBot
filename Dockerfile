# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ForexBot.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime (linux/amd64)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Environment variables — pass at runtime via docker run -e or --env-file
ENV ANTHROPIC_API_KEY=""
ENV META_API_TOKEN=""
ENV META_ACCOUNT_ID=""

ENTRYPOINT ["dotnet", "ForexBot.dll"]
