# Local Development Runbook

Este runbook prepara os comandos esperados para o Payment Gateway MVP. Alguns comandos só funcionarão depois que a solução .NET, Docker Compose e serviços forem criados.

## Comandos esperados

```bash
dotnet restore
dotnet build
dotnet test
docker compose up -d
docker compose logs -f
```

## Variáveis de ambiente esperadas

Use valores locais e não sensíveis. Nunca commite `.env` real.

```env
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__Postgres=
PaymentGateway__DefaultProvider=Fake
PaymentGateway__ApiKeyHashSecret=
PaymentGateway__CredentialEncryptionKey=
```

## Fluxo local futuro

1. Restaurar dependências com `dotnet restore`.
2. Subir dependências com `docker compose up -d`.
3. Aplicar migrations quando existirem.
4. Executar API e Worker.
5. Validar `/health` e Swagger/OpenAPI.
6. Rodar `dotnet test` antes de abrir PR.
