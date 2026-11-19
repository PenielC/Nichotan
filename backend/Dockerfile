# Nichotan API. Build context is the backend/ folder:  docker build -t nichotan-api backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first (cached until a project file changes)
COPY Nichotan.slnx ./
COPY src/Nichotan.Domain/*.csproj src/Nichotan.Domain/
COPY src/Nichotan.Application/*.csproj src/Nichotan.Application/
COPY src/Nichotan.Infrastructure/*.csproj src/Nichotan.Infrastructure/
COPY src/Nichotan.Api/*.csproj src/Nichotan.Api/
RUN dotnet restore src/Nichotan.Api/Nichotan.Api.csproj
COPY src/ src/
RUN dotnet publish src/Nichotan.Api/Nichotan.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# QuestPDF (the application-form PDF) needs fontconfig on Linux.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
# Runs as the image's non-root "app" user.
USER $APP_UID
ENTRYPOINT ["dotnet", "Nichotan.Api.dll"]
