# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props UrlShortener.slnx ./
COPY src/UrlShortener.Core/UrlShortener.Core.csproj src/UrlShortener.Core/
COPY src/UrlShortener.Infrastructure/UrlShortener.Infrastructure.csproj src/UrlShortener.Infrastructure/
COPY src/UrlShortener.Api/UrlShortener.Api.csproj src/UrlShortener.Api/
RUN dotnet restore src/UrlShortener.Api/UrlShortener.Api.csproj
COPY src/ src/
RUN dotnet publish src/UrlShortener.Api/UrlShortener.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /data && chown app:app /data
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__Provider=Sqlite \
    Storage__ConnectionString="Data Source=/data/urlshortener.db"
VOLUME ["/data"]
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]
