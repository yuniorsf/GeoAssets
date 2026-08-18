# Build context must be the repo root (GeoAssets.Web references sibling
# projects under core/, providers/, apps/GeoAssets.Shared) — see deploy/server/docker-compose.yaml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore apps/GeoAssets.Web/GeoAssets.Web.csproj
RUN dotnet publish apps/GeoAssets.Web/GeoAssets.Web.csproj \
    -c Release -o /app/publish --no-restore

FROM nginx:alpine AS runtime
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY apps/GeoAssets.Web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
