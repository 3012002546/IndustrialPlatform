# syntax=docker/dockerfile:1
# 平台统一运行镜像:前端生产静态文件 + UnifiedHost 单进程(云端无需第二个前端容器)。
# CI 构建后以不可变 tag/digest 发布;服务器只拉取镜像,不 clone/build 源码。
#
# 构建参数:
#   VITE_API_BASE_URL  部署后前端可达的同一源(如 https://<tailnet-ip>:8080);
#                      必须显式传入,否则回退到本地默认 localhost:5080 导致云端 API 404。
#   VITE_AUTH_MODE     生产构建禁止 mock,默认 http(真实登录)。
# 示例(CI):
#   docker build -f deploy/application/Dockerfile \
#     --build-arg VITE_API_BASE_URL=https://100.x.y.z:8080 \
#     -t registry.example/industrial-platform:<version> .

# ---------- 前端构建 ----------
FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY src/frontend/package.json src/frontend/pnpm-lock.yaml ./
RUN corepack enable && corepack prepare pnpm@11.16.0 --activate
RUN pnpm install --frozen-lockfile
COPY src/frontend/ ./
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
RUN pnpm build

# ---------- 后端发布 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-publish
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/backend/src ./src/backend/src
RUN dotnet publish src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj \
    --configuration Release \
    --output /app/publish \
    --nologo

# ---------- 运行时 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=backend-publish /app/publish ./
COPY --from=frontend-build /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "IndustrialPlatform.UnifiedHost.dll"]
