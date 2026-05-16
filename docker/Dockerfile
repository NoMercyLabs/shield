# syntax=docker/dockerfile:1.7

# ---------- Stage 1: SPA build ----------
# Vite writes to ../Shield.Api/wwwroot/, so both projects must be present.
FROM node:22-alpine AS spa-build
WORKDIR /work
COPY src/Shield.Web/package.json src/Shield.Web/package-lock.json ./src/Shield.Web/
WORKDIR /work/src/Shield.Web
RUN npm ci
WORKDIR /work
COPY src/Shield.Web/ ./src/Shield.Web/
COPY src/Shield.Api/ ./src/Shield.Api/
WORKDIR /work/src/Shield.Web
RUN npm run build

# ---------- Stage 2: .NET build + publish ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS dotnet-build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Shield.sln ./
COPY src/ ./src/
COPY agent/ ./agent/
# Bring in the SPA build output before publish so it lands in wwwroot.
COPY --from=spa-build /work/src/Shield.Api/wwwroot/ ./src/Shield.Api/wwwroot/
RUN dotnet restore src/Shield.Api/Shield.Api.csproj
RUN dotnet publish src/Shield.Api/Shield.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    /p:UseAppHost=false

# ---------- Stage 3: Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache wget icu-libs \
 && addgroup -S shield \
 && adduser -S -G shield -h /app -s /sbin/nologin shield \
 && mkdir -p /data /data/keys \
 && chown -R shield:shield /data /app
WORKDIR /app
COPY --from=dotnet-build --chown=shield:shield /publish/ ./
USER shield
VOLUME ["/data", "/data/keys"]
ENV ASPNETCORE_URLS=http://+:8842 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Shield__Db__Shield="Data Source=/data/shield.db" \
    Shield__Db__Feeds="Data Source=/data/feeds.db" \
    Shield__SingleUser=true \
    Shield__Auth__JwtSigningKey=CHANGE_ME_must_be_set_to_a_real_secret_in_production \
    Shield__Auth__DataProtectionKeysPath=/data/keys \
    Shield__Auth__DataProtectionMasterKey=CHANGE_ME_set_a_real_secret_or_secrets_die_on_recreate
EXPOSE 8842
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://localhost:8842/healthz || exit 1
ENTRYPOINT ["dotnet", "Shield.Api.dll"]
