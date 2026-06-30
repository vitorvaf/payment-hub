# Validation Matrix

Use esta matriz para selecionar validações proporcionais ao escopo da tarefa.

## Validação local

```bash
dotnet restore
dotnet build
dotnet test
```

## Specs e ADRs

- Conferir se a alteração respeita a spec relacionada em `docs/specs`.
- Atualizar spec quando houver mudança de contrato.
- Atualizar ADR quando houver nova decisão arquitetural.

## Build

- `dotnet restore`: restaura dependências.
- `dotnet build`: compila a solução.
- `dotnet test`: executa testes automatizados.

## Docker

```bash
docker compose config
docker compose up -d
```

- Validar que o compose é sintaticamente correto.
- Subir dependências locais quando existirem.

## Banco

Validações futuras, quando EF Core e migrations existirem:

```bash
dotnet ef migrations list
dotnet ef database update
```

- Conferir migrations pendentes.
- Aplicar migrations em ambiente local controlado.

## API

Validações futuras:

```bash
curl http://localhost:5000/health
```

- Conferir health check.
- Conferir Swagger/OpenAPI.
- Validar autenticação server-to-server.
- Validar idempotência em endpoints de criação de pagamento.

## Worker

- Validar consumo de Inbox.
- Validar publicação por Outbox.
- Validar retry e tratamento de falhas.
- Validar logs estruturados.

## Segurança

- Verificar ausência de secrets reais no repositório.
- Verificar que `.env` real não foi commitado.
- Verificar que API Keys são armazenadas como hash.
- Verificar que dados sensíveis não aparecem em logs.
- Verificar validação de assinatura de webhooks quando suportado.

## Slice-specific (Phase 2 / Slice 2-C — AbacatePay webhook management)

Quando a slice altera rotas de webhook de provider ou endpoints de gerenciamento:

- Migration nova nao cria coluna para `webhookSecret`. Confirmar com `dotnet ef migrations list` + diff visual.
- Migration nova mantem `webhook_events` como `text` (NAO `jsonb`) em qualquer tabela. Buscar `HasColumnType("jsonb")` no commit; rejeitar se presente em coluna que armazena JSON do cliente.
- `IntegrationTestFactory.ResetDatabaseAsync` continua truncando `provider_accounts` em ordem topologica reversa; se nova coluna ganhar FK ou indice, atualizar a ordem.
- Filtros de teste cobrem os caminhos novos:
  ```bash
  dotnet test --filter "FullyQualifiedName~ProviderAccountWebhookPersistenceTests"
  dotnet test --filter "FullyQualifiedName~ConfigureProviderAccountWebhookHandlerTests"
  dotnet test --filter "FullyQualifiedName~GetProviderAccountWebhookHandlerTests"
  dotnet test --filter "FullyQualifiedName~ConfigureAbacatePayWebhookRequestValidatorTests"
  dotnet test --filter "FullyQualifiedName~ProviderAccountsWebhookControllerTests"
  ```
- Contratos novos vao em `docs/specs/009-api-contracts.md` antes do PR. Fica proibido inserir `tenantId`/`applicationId` em DTOs de request quando o endpoint for autenticado (re-asserting Slice 6-B).
