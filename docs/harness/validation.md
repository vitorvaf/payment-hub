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
