FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/IntegrationConnector.Core/*.csproj src/IntegrationConnector.Core/
COPY src/IntegrationConnector.Connectors/*.csproj src/IntegrationConnector.Connectors/
COPY src/IntegrationConnector.Transformation/*.csproj src/IntegrationConnector.Transformation/
COPY src/IntegrationConnector.Engine/*.csproj src/IntegrationConnector.Engine/
COPY src/IntegrationConnector.Infrastructure/*.csproj src/IntegrationConnector.Infrastructure/
COPY src/IntegrationConnector.Api/*.csproj src/IntegrationConnector.Api/
RUN dotnet restore src/IntegrationConnector.Api/IntegrationConnector.Api.csproj

COPY src/ src/
RUN dotnet publish src/IntegrationConnector.Api/IntegrationConnector.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "IntegrationConnector.Api.dll"]
