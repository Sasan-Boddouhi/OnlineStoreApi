# ============================================
# Stage 1: Build & Publish
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. کپی فایل‌های csproj برای restore (استفاده از کش Docker)
COPY ["Online Store Application/Online Store Application(API).csproj", "Online Store Application/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["BusinessLogic/BusinessLogic.csproj", "BusinessLogic/"]
COPY ["DataLayer/DataLayer.csproj", "DataLayer/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]

# 2. Restore وابستگی‌ها
RUN dotnet restore "Online Store Application/Online Store Application(API).csproj"

# 3. کپی کامل سورس
COPY . .

# 4. Build و Publish
WORKDIR "/src/Online Store Application"
RUN dotnet publish "Online Store Application(API).csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

# ============================================
# Stage 1b: EF Core migration bundle (Batch 52, OPS-02)
# ============================================
# مایگریشن دیگر هنگام بالا آمدن API اجرا نمی‌شود (Database__AutoMigrate=false در Production)؛
# سرویس یک‌بارهٔ «migrator» در docker-compose.prod.yml همین bundle را با login مخصوص مایگریشن اجرا می‌کند.
# ساخت: docker build --target migrator -t <image>:<tag>-migrator .
# نسخهٔ dotnet-ef باید با Microsoft.EntityFrameworkCore پروژه یکی باشد.
FROM build AS bundle
ARG DOTNET_EF_VERSION=8.0.34
ARG BUNDLE_RUNTIME=linux-x64
RUN dotnet tool install --global dotnet-ef --version ${DOTNET_EF_VERSION}
ENV PATH="${PATH}:/root/.dotnet/tools"
WORKDIR /src
RUN dotnet ef migrations bundle \
    --project DataLayer/DataLayer.csproj \
    --startup-project "Online Store Application/Online Store Application(API).csproj" \
    --context AppDbContext \
    --configuration Release \
    --target-runtime ${BUNDLE_RUNTIME} \
    --self-contained \
    --output /app/bundle/efbundle

# ============================================
# Stage 1c: Migrator image (one-shot)
# ============================================
# DesignTimeDbContextFactory فایل appsettings.json را از پوشهٔ جاری می‌خواند (اجباری)؛
# رشتهٔ اتصال فقط از متغیر محیطی ConnectionStrings__DefaultConnection می‌آید.
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS migrator
WORKDIR /app
COPY --from=bundle /app/bundle/efbundle /app/efbundle
COPY --from=build ["/src/Online Store Application/appsettings.json", "/app/appsettings.json"]
USER app
ENTRYPOINT ["/app/efbundle"]

# ============================================
# Stage 2: Runtime (آخرین stage = هدف پیش‌فرض docker build)
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# 5. کپی فقط فایل‌های publish شده
COPY --from=build /app/publish .

# 6. تنظیمات محیطی
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80

# 7. پورت
EXPOSE 80

# 8. نصب curl برای HEALTHCHECK (نیاز به root)
USER root
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# 8.1 پوشهٔ آپلود باید قبل از سوییچ به کاربر غیر root ساخته و به او داده شود؛ وگرنه
#     LocalStorageService در سازنده خطای دسترسی می‌دهد (F-32). در compose روی volume پایدار mount می‌شود.
RUN mkdir -p /app/wwwroot/uploads && chown -R app:app /app/wwwroot

# 9. برگرداندن کاربر غیر root (امنیت بیشتر)
USER app

# 10. HEALTHCHECK واقعی
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:80/health/live || exit 1

# 11. دستور اجرا
ENTRYPOINT ["dotnet", "Online Store Application(API).dll"]