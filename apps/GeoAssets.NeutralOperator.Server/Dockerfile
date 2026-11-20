# Build context must be the repo root (GeoAssets.NeutralOperator.Server references sibling projects under core/,
# providers/, workflow/ and verticals/) — see deploy/neutral-operator/README.md. Same shape as
# apps/GeoAssets.FieldService.Server/Dockerfile.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# The protoc that Grpc.Tools 2.71.0 bundles for linux-arm64 (libprotoc 29.0) segfaults (exit 139) whenever it reads its
# arguments from a response file — which is how Grpc.Tools' ProtoCompile task always invokes it — on current Docker
# Desktop VM kernels, failing GeoAssets.Workflow.Messaging.Protobuf's build. Compile the .proto files with the distro's
# protoc instead; the well-known types still come from Grpc.Tools' include path.
RUN apt-get update && \
    apt-get install -y --no-install-recommends protobuf-compiler && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore apps/GeoAssets.NeutralOperator.Server/GeoAssets.NeutralOperator.Server.csproj -r $RID && \
    dotnet publish apps/GeoAssets.NeutralOperator.Server/GeoAssets.NeutralOperator.Server.csproj \
      -c Release -o /app/publish --no-restore -r $RID --self-contained false \
      -p:Protobuf_ProtocFullPath=/usr/bin/protoc

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl for the compose healthcheck (GET /healthz).
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoAssets.NeutralOperator.Server.dll"]
