# Build context must be the repo root (GeoAssets.Server references sibling
# projects under core/, providers/, workflow/) — see deploy/server/docker-compose.yaml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
# Explicit RID is required even for a framework-dependent publish — without it,
# SkiaSharp.NativeAssets.Linux.NoDependencies' libSkiaSharp.so never gets copied
# into the output, and WmsEndpointExtensions' GetMap throws DllNotFoundException
# at runtime. TARGETARCH is set automatically by BuildKit to match the build
# platform (amd64/arm64); .NET RIDs use "x64" instead of "amd64", so translate it.
RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore apps/GeoAssets.Server/GeoAssets.Server.csproj -r $RID && \
    dotnet publish apps/GeoAssets.Server/GeoAssets.Server.csproj \
      -c Release -o /app/publish --no-restore -r $RID --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoAssets.Server.dll"]
