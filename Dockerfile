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
# Stage 2: Runtime
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

# 9. برگرداندن کاربر غیر root (امنیت بیشتر)
USER app

# 10. HEALTHCHECK واقعی
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl --fail http://localhost:80/health || exit 1

# 11. دستور اجرا
ENTRYPOINT ["dotnet", "Online Store Application(API).dll"]