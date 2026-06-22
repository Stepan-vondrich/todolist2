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
RUN dotnet restore ./TodoApi/TodoApi.csproj
COPY backend/TodoApi/ ./TodoApi/
# Override OutputType=WinExe (set for Windows Release builds) so this produces a
# normal cross-platform assembly; skip the apphost since we launch via `dotnet`.
RUN dotnet publish ./TodoApi/TodoApi.csproj \
    -c Release \
    -o /app/publish \
    -p:OutputType=Exe \
    -p:UseAppHost=false

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./
# Frontend is served as static files from <ContentRoot>/wwwroot
COPY --from=frontend /app/dist ./wwwroot

# Bind to all interfaces on 8080 (Azure Container Apps ingress target port).
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "TodoApi.dll"]
