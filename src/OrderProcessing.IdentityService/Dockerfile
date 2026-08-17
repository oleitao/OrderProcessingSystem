# Build context is the repository root (see docker-compose.yml), so multi-project references resolve.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# curl is used by the docker-compose healthcheck to probe GET /health.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["OrderProcessingSystem.slnx", "."]
COPY ["src/OrderProcessing.IdentityService/OrderProcessing.IdentityService.csproj", "src/OrderProcessing.IdentityService/"]
RUN dotnet restore "src/OrderProcessing.IdentityService/OrderProcessing.IdentityService.csproj"

COPY src/OrderProcessing.IdentityService/ src/OrderProcessing.IdentityService/

WORKDIR /src/src/OrderProcessing.IdentityService
RUN dotnet publish "OrderProcessing.IdentityService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderProcessing.IdentityService.dll"]
