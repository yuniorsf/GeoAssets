# Build context must be the repo root (GeoAssets.Gateway references sibling projects under core/) — see
# deploy/server/docker-compose.yaml (service "gateway", XD01-384).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/GeoAssets.Gateway/GeoAssets.Gateway.csproj
RUN dotnet publish apps/GeoAssets.Gateway/GeoAssets.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl for the compose healthcheck (GET /healthz).
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoAssets.Gateway.dll"]
