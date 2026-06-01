FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.sln .
COPY Application/ Application/
COPY BusinessLogic/ BusinessLogic/
COPY DataLayer/ DataLayer/
COPY ["Online Store Application/Online Store Application(API).csproj", "Online Store Application/"]
RUN dotnet restore "Online Store Application/Online Store Application(API).csproj"
COPY . .
RUN dotnet publish "Online Store Application/Online Store Application(API).csproj" -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 80
ENTRYPOINT ["dotnet", "Online Store Application(API).dll"]