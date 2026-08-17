# Build context is the repository root (see docker-compose.yml), so multi-project references resolve.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
# Npgsql probes for Kerberos/GSSAPI support at connect time; the slim runtime image doesn't
# include it by default, which otherwise logs a scary (but non-fatal) "cannot open shared
# object file" error on every connection attempt.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["OrderProcessingSystem.slnx", "."]
COPY ["src/OrderProcessing.OrderWorker/OrderProcessing.OrderWorker.csproj", "src/OrderProcessing.OrderWorker/"]
COPY ["src/OrderProcessing.Application/OrderProcessing.Application.csproj", "src/OrderProcessing.Application/"]
COPY ["src/OrderProcessing.Domain/OrderProcessing.Domain.csproj", "src/OrderProcessing.Domain/"]
COPY ["src/OrderProcessing.Infrastructure/OrderProcessing.Infrastructure.csproj", "src/OrderProcessing.Infrastructure/"]
COPY ["src/OrderProcessing.Contracts/OrderProcessing.Contracts.csproj", "src/OrderProcessing.Contracts/"]
RUN dotnet restore "src/OrderProcessing.OrderWorker/OrderProcessing.OrderWorker.csproj"

COPY src/OrderProcessing.OrderWorker/ src/OrderProcessing.OrderWorker/
COPY src/OrderProcessing.Application/ src/OrderProcessing.Application/
COPY src/OrderProcessing.Domain/ src/OrderProcessing.Domain/
COPY src/OrderProcessing.Infrastructure/ src/OrderProcessing.Infrastructure/
COPY src/OrderProcessing.Contracts/ src/OrderProcessing.Contracts/

WORKDIR /src/src/OrderProcessing.OrderWorker
RUN dotnet publish "OrderProcessing.OrderWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderProcessing.OrderWorker.dll"]
