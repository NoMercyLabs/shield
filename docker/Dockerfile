# syntax=docker/dockerfile:1.7

# ---------- Stage 1: SPA build ----------
# Vite writes to ../Shield.Api/wwwroot/, so both projects must be present.
# Output is baked into the runtime image — SPA changes require a full image rebuild.
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
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS dotnet-build
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
# Fixed UID/GID (10001) so bind-mounts have a predictable owner on the host and the user
# sits well outside the system range. wget is for HEALTHCHECK; icu-libs lets
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false work for the few code paths that need ICU.
RUN apk add --no-cache wget icu-libs \
 && addgroup -S -g 10001 shield \
 && adduser -S -u 10001 -G shield -h /app -s /sbin/nologin shield \
 && mkdir -p /app/data /app/data/keys \
 && chown -R shield:shield /app
WORKDIR /app
COPY --from=dotnet-build --chown=shield:shield /publish/ ./
USER shield
VOLUME ["/app/data"]
# Bind to ALL interfaces inside the container — the host port mapping decides whether
# traffic is reachable from outside the container. localhost-only inside the container
# would make the published port unreachable.
ENV ASPNETCORE_URLS=http://+:8842 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    Shield__Db__Shield="Data Source=/app/data/shield.db" \
    Shield__Db__Feeds="Data Source=/app/data/feeds.db" \
    Shield__Auth__DataProtectionKeysPath=/app/data/keys
# Secrets live in the operator's .env file — see docker/.env.example. The image ships NO
# JwtSigningKey or DataProtectionMasterKey default so a misconfigured deploy crashes loudly
# at the ProductionSafetyGate instead of silently running with a shared dev secret.
#
# Posture knobs (docs/internet-exposure.md):
#   Shield__Public=true                            # marks host as internet-exposed
#   Shield__Auth__RequireHttps=true                # required when Public=true (HSTS + Secure cookies)
#   Shield__SingleUser=false                       # multi-user prod (default unset = false)
#   Shield__Auth__AllowSingleUserInProduction=true # escape hatch to keep SingleUser auto-Admin in prod
EXPOSE 8842
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://localhost:8842/healthz || exit 1
ENTRYPOINT ["dotnet", "Shield.Api.dll"]
