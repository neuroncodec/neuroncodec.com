# syntax=docker/dockerfile:1

# ── Build ────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release

# Stamped by CI from the GitVersion AssemblySemVer.
ARG VERSION=0.1.0.0
ARG INFORMATIONAL_VERSION=0.1.0

WORKDIR /src

# Restore first, on the project file alone, so the layer caches across source edits.
COPY ["src/NeuronCodec.Web/NeuronCodec.Web.csproj", "src/NeuronCodec.Web/"]
RUN dotnet restore "src/NeuronCodec.Web/NeuronCodec.Web.csproj"

COPY . .

RUN dotnet publish "src/NeuronCodec.Web/NeuronCodec.Web.csproj" \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:Version="$VERSION" \
    /p:InformationalVersion="$INFORMATIONAL_VERSION"

# ── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

# curl is only here for the healthcheck below.
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# The two mount points. Created up front and owned by the non-root app user so a fresh
# named volume inherits the right ownership.
RUN mkdir -p /app/data /app/uploads && chown -R app:app /app/data /app/uploads

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    NeuronCodec__DataPath=/app/data \
    NeuronCodec__Uploads__Path=/app/uploads \
    ConnectionStrings__Default="Data Source=/app/data/neuroncodec.db"

VOLUME ["/app/data", "/app/uploads"]
EXPOSE 8080

USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "NeuronCodec.Web.dll"]
