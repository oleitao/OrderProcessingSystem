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
COPY ["src/OrderProcessing.Api/OrderProcessing.Api.csproj", "src/OrderProcessing.Api/"]
COPY ["src/OrderProcessing.Application/OrderProcessing.Application.csproj", "src/OrderProcessing.Application/"]
COPY ["src/OrderProcessing.Domain/OrderProcessing.Domain.csproj", "src/OrderProcessing.Domain/"]
COPY ["src/OrderProcessing.Infrastructure/OrderProcessing.Infrastructure.csproj", "src/OrderProcessing.Infrastructure/"]
COPY ["src/OrderProcessing.Contracts/OrderProcessing.Contracts.csproj", "src/OrderProcessing.Contracts/"]
RUN dotnet restore "src/OrderProcessing.Api/OrderProcessing.Api.csproj"

COPY src/OrderProcessing.Api/ src/OrderProcessing.Api/
COPY src/OrderProcessing.Application/ src/OrderProcessing.Application/
COPY src/OrderProcessing.Domain/ src/OrderProcessing.Domain/
COPY src/OrderProcessing.Infrastructure/ src/OrderProcessing.Infrastructure/
COPY src/OrderProcessing.Contracts/ src/OrderProcessing.Contracts/

WORKDIR /src/src/OrderProcessing.Api
RUN dotnet publish "OrderProcessing.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderProcessing.Api.dll"]
