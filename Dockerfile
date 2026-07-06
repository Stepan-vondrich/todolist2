# syntax=docker/dockerfile:1

# ---- Stage 1: build the React/Vite frontend ----
FROM node:20-alpine AS frontend
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---- Stage 2: build & publish the .NET backend ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /src
COPY backend/TodoApi/TodoApi.csproj ./TodoApi/
# Restore for the Alpine (musl) target so the ReadyToRun crossgen has the right packs.
RUN dotnet restore ./TodoApi/TodoApi.csproj -r linux-musl-x64
COPY backend/TodoApi/ ./TodoApi/
# Override OutputType=WinExe (set for Windows Release builds) so this produces a normal
# assembly; skip the apphost since we launch via `dotnet`. PublishReadyToRun precompiles
# IL to native for linux-musl-x64 so cold start does far less JIT (faster boot).
RUN dotnet publish ./TodoApi/TodoApi.csproj \
    -c Release \
    -o /app/publish \
    -r linux-musl-x64 --self-contained false \
    -p:OutputType=Exe \
    -p:UseAppHost=false \
    -p:PublishReadyToRun=true

# ---- Stage 3: runtime (Alpine = ~2x smaller image → faster pull on cold start) ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# Alpine .NET images ship no ICU; install it so culture-specific formatting (cs-CZ
# dates/numbers) keeps working instead of falling back to invariant globalization.
RUN apk add --no-cache icu-libs icu-data-full
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=backend /app/publish ./
# Frontend is served as static files from <ContentRoot>/wwwroot
COPY --from=frontend /app/dist ./wwwroot

# Bind to all interfaces on 8080 (Azure Container Apps ingress target port).
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "TodoApi.dll"]
