# syntax=docker/dockerfile:1

# ---- Stage 1: build the frontend (Vite/React -> static files) ----
FROM node:20-alpine AS frontend
WORKDIR /src/frontend
# Install deps first for better layer caching.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build
# Output is /src/frontend/dist

# ---- Stage 2: build & publish the backend, bundling the SPA into wwwroot ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
# Restore first for better layer caching.
COPY backend/Api/Api.csproj backend/Api/
RUN dotnet restore backend/Api/Api.csproj
COPY backend/Api/ backend/Api/
# Drop the built SPA into wwwroot so ASP.NET serves it (UseStaticFiles + fallback).
COPY --from=frontend /src/frontend/dist backend/Api/wwwroot
RUN dotnet publish backend/Api/Api.csproj -c Release -o /app/publish

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Npgsql probes for Kerberos/GSSAPI at startup; without this lib it logs a noisy
# (harmless) error before falling back to password auth. Install it to keep
# startup logs clean.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=backend /app/publish ./
# The aspnet image listens on 8080 by default (ASPNETCORE_HTTP_PORTS=8080).
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
