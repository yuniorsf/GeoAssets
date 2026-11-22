# syntax=docker/dockerfile:1.29.0@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
# Build context must be the repo root (GeoAssets.Server references sibling
# projects under core/, providers/, workflow/) — see deploy/server/docker-compose.yaml.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
ARG TARGETARCH
# The protoc that Grpc.Tools 2.71.0 bundles for linux-arm64 (libprotoc 29.0) segfaults (exit 139) whenever it reads its
# arguments from a response file — which is how Grpc.Tools' ProtoCompile task always invokes it — on current Docker
# Desktop VM kernels, failing GeoAssets.Workflow.Messaging.Protobuf's build. Compile the .proto files with the distro's
# protoc instead (the same override that project's csproj already makes with Homebrew's protoc on macOS arm64); the
# well-known types still come from Grpc.Tools' include path, and the generated code targets the same Google.Protobuf runtime.
RUN apt-get update && \
    apt-get install -y --no-install-recommends protobuf-compiler && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /src
# Restore from the solution-level build files and every project file only (their directory layout kept), so the restore
# layer is reused until a .csproj or Directory.Build.props changes; packages live in the BuildKit cache mount.
COPY --parents GeoAssets.sln Directory.Build.props BannedSymbols.txt .editorconfig global.json **/*.csproj ./
# Explicit RID is required even for a framework-dependent publish — without it,
# SkiaSharp.NativeAssets.Linux.NoDependencies' libSkiaSharp.so never gets copied
# into the output, and WmsEndpointExtensions' GetMap throws DllNotFoundException
# at runtime. TARGETARCH is set automatically by BuildKit to match the build
# platform (amd64/arm64); .NET RIDs use "x64" instead of "amd64", so translate it.
# Publish restores again (a no-op while the cache mount is warm) instead of --no-restore, so an evicted cache mount
# re-downloads the packages rather than failing with NETSDK1064; restore and publish use the same RID.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore apps/GeoAssets.Server/GeoAssets.Server.csproj -r $RID
COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish apps/GeoAssets.Server/GeoAssets.Server.csproj \
      -c Release -o /app/publish -r $RID --self-contained false \
      -p:Protobuf_ProtocFullPath=/usr/bin/protoc

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The image's unprivileged "app" user (APP_UID=1654); the app only writes to /tmp.
USER $APP_UID
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "GeoAssets.Server.dll"]
