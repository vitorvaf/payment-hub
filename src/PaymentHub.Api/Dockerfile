FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PaymentHub.slnx ./
COPY src/PaymentHub.Domain/PaymentHub.Domain.csproj src/PaymentHub.Domain/
COPY src/PaymentHub.Application/PaymentHub.Application.csproj src/PaymentHub.Application/
COPY src/PaymentHub.Infrastructure.Postgres/PaymentHub.Infrastructure.Postgres.csproj src/PaymentHub.Infrastructure.Postgres/
COPY src/PaymentHub.Infrastructure.Providers/PaymentHub.Infrastructure.Providers.csproj src/PaymentHub.Infrastructure.Providers/
COPY src/PaymentHub.Api/PaymentHub.Api.csproj src/PaymentHub.Api/
COPY src/PaymentHub.Worker/PaymentHub.Worker.csproj src/PaymentHub.Worker/
COPY tests/PaymentHub.UnitTests/PaymentHub.UnitTests.csproj tests/PaymentHub.UnitTests/
COPY tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj tests/PaymentHub.IntegrationTests/

RUN dotnet restore src/PaymentHub.Api/PaymentHub.Api.csproj

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/PaymentHub.Api/PaymentHub.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentHub.Api.dll"]
