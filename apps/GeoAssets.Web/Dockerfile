# syntax=docker/dockerfile:1.29.0@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
# Build context must be the repo root (GeoAssets.Web references sibling
# projects under core/, providers/, apps/GeoAssets.Shared) — see deploy/web/docker-compose.yaml.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src
# Restore from the solution-level build files and every project file only (their directory layout kept), so the restore
# layer is reused until a .csproj or Directory.Build.props changes; packages live in the BuildKit cache mount.
COPY --parents GeoAssets.sln Directory.Build.props BannedSymbols.txt .editorconfig global.json **/*.csproj ./
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore apps/GeoAssets.Web/GeoAssets.Web.csproj
COPY . .
# The static site ends up as /app/site/html, so that copying /app/site/ below re-owns the html directory itself as well.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish apps/GeoAssets.Web/GeoAssets.Web.csproj \
    -c Release -o /app/publish && \
    mkdir -p /app/site && \
    mv /app/publish/wwwroot /app/site/html

# nginx as its unprivileged "nginx" user (uid/gid 101), listening on 8080 (nginx.conf).
FROM nginxinc/nginx-unprivileged:1.31.7-alpine@sha256:b5047b9b76845eac124358373c6852377c52e4b05834a47647e955dd91cb8f1e AS runtime
# XD01-384: the REST base URLs the app reads in its (Production) environment, from the container's environment at start —
# nginx's entrypoint renders /etc/nginx/templates/*.template with envsubst into NGINX_ENVSUBST_OUTPUT_DIR, here the site
# root, replacing the published appsettings.Production.json; the site root is therefore owned by uid 101, which the
# entrypoint runs as. GEOASSETS_GATEWAY_URL is the gateway's public URL (every GeoAssets.Server and vertical API goes
# through it); the defaults are the local deploy/server stack's.
COPY --from=build --chown=101:101 /app/site/ /usr/share/nginx/
COPY apps/GeoAssets.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY apps/GeoAssets.Web/appsettings.Production.json.template /etc/nginx/templates/appsettings.Production.json.template
ENV NGINX_ENVSUBST_OUTPUT_DIR=/usr/share/nginx/html \
    GEOASSETS_GATEWAY_URL=http://localhost:8090 \
    GEOASSETS_IPBILLING_SERVER_URL=http://localhost:8090
EXPOSE 8080
USER 101
# nginx's graceful shutdown signal (SIGTERM is its fast shutdown), the same as the base image's.
STOPSIGNAL SIGQUIT
