# Build context must be the repo root (GeoAssets.Server references sibling
# projects under core/, providers/, workflow/) — see deploy/server/docker-compose.yaml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/GeoAssets.Server/GeoAssets.Server.csproj
RUN dotnet publish apps/GeoAssets.Server/GeoAssets.Server.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoAssets.Server.dll"]
