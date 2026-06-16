FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PaymentHub.slnx ./
COPY src/PaymentHub.Domain/PaymentHub.Domain.csproj src/PaymentHub.Domain/
COPY src/PaymentHub.Application/PaymentHub.Application.csproj src/PaymentHub.Application/
COPY src/PaymentHub.Infrastructure.Postgres/PaymentHub.Infrastructure.Postgres.csproj src/PaymentHub.Infrastructure.Postgres/
COPY src/PaymentHub.Infrastructure.Providers/PaymentHub.Infrastructure.Providers.csproj src/PaymentHub.Infrastructure.Providers/
COPY src/PaymentHub.Worker/PaymentHub.Worker.csproj src/PaymentHub.Worker/
COPY tests/PaymentHub.UnitTests/PaymentHub.UnitTests.csproj tests/PaymentHub.UnitTests/
COPY tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj tests/PaymentHub.IntegrationTests/

RUN dotnet restore src/PaymentHub.Worker/PaymentHub.Worker.csproj

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/PaymentHub.Worker/PaymentHub.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "PaymentHub.Worker.dll"]
