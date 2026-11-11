FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/LlmQuality.Api/LlmQuality.Api.csproj src/LlmQuality.Api/
RUN dotnet restore src/LlmQuality.Api/LlmQuality.Api.csproj
COPY src/ src/
RUN dotnet publish src/LlmQuality.Api/LlmQuality.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LlmQuality.Api.dll"]
