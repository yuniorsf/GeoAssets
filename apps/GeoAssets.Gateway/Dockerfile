# syntax=docker/dockerfile:1.29.0@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
# Build context must be the repo root (GeoAssets.Gateway references sibling projects under core/) — see
# deploy/server/docker-compose.yaml (service "gateway", XD01-384).
FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src
# Restore from the solution-level build files and every project file only (their directory layout kept), so the restore
# layer is reused until a .csproj or Directory.Build.props changes; packages live in the BuildKit cache mount.
COPY --parents GeoAssets.sln Directory.Build.props BannedSymbols.txt .editorconfig global.json **/*.csproj ./
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore apps/GeoAssets.Gateway/GeoAssets.Gateway.csproj
COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish apps/GeoAssets.Gateway/GeoAssets.Gateway.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586 AS runtime
WORKDIR /app
# No curl: the compose healthcheck (GET /healthz) uses bash's /dev/tcp.
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The image's unprivileged "app" user (APP_UID=1654).
USER $APP_UID
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "GeoAssets.Gateway.dll"]
