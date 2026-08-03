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

# 4. Build و Publish (یکجا انجام می‌شود برای کاهش لایه‌ها)
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

# 8. کاربر غیر root (امنیت بیشتر)
USER app

# 9. دستور اجرا
ENTRYPOINT ["dotnet", "Online Store Application(API).dll"]