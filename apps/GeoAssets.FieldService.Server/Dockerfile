# Build context must be the repo root (GeoAssets.FieldService.Server references sibling
# projects under core/, providers/, workflow/) — see deploy/helm/geoassets-fieldservice/README.md.
#
# Deliberately the same shape as apps/GeoAssets.Server/Dockerfile (XD01-215's "Zero Code
# Branching Invariant": a Tier-2 dedicated silo and the shared multi-tenant tier run the exact
# same image — only the ConnectionStrings__FieldService/AzureAdCiam__* environment variables at
# deploy time differ).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
COPY . .
RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore apps/GeoAssets.FieldService.Server/GeoAssets.FieldService.Server.csproj -r $RID && \
    dotnet publish apps/GeoAssets.FieldService.Server/GeoAssets.FieldService.Server.csproj \
      -c Release -o /app/publish --no-restore -r $RID --self-contained false \
      -p:Protobuf_ProtocFullPath=/usr/bin/protoc

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoAssets.FieldService.Server.dll"]
