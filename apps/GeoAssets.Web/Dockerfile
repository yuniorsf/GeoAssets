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
# XD01-384: the REST base URLs the app reads in its (Production) environment, from the container's environment at start —
# nginx's entrypoint renders /etc/nginx/templates/*.template with envsubst into NGINX_ENVSUBST_OUTPUT_DIR, here the site
# root, replacing the published appsettings.Production.json. GEOASSETS_GATEWAY_URL is the gateway's public URL (every
# GeoAssets.Server and vertical API goes through it); the defaults are the local deploy/server stack's.
COPY apps/GeoAssets.Web/appsettings.Production.json.template /etc/nginx/templates/appsettings.Production.json.template
ENV NGINX_ENVSUBST_OUTPUT_DIR=/usr/share/nginx/html \
    GEOASSETS_GATEWAY_URL=http://localhost:8090 \
    GEOASSETS_IPBILLING_SERVER_URL=http://localhost:8090
EXPOSE 80
