# Copilot Instructions

Siga `AGENTS.md` antes de sugerir ou alterar codigo. Este arquivo deve ficar curto; use as instrucoes por path e `docs/` para contexto progressivo.

## Projeto

- Payment Gateway MVP: orquestrador multitenant de pagamentos.
- Stack: .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 16, Docker Compose, Serilog, Swagger/OpenAPI, xUnit.
- Arquitetura: Domain, Application, Infrastructure.Postgres, Infrastructure.Providers, Api e Worker.
- Fonte de verdade: specs em `docs/specs/` e ADRs em `docs/adr/`.

## Antes de trabalhar

- Leia `docs/harness/project-context.md`, `docs/harness/workflow.md`, `docs/harness/validation.md`, `docs/harness/security.md` e `docs/harness/learnings.md`.
- Leia a spec relacionada em `docs/specs/README.md`.
- Para mudancas arquiteturais, leia os ADRs relevantes.
- Inspecione comandos reais no repositorio; nao invente build, test, lint ou scripts.

## Convencoes obrigatorias

- Preserve Clean Architecture: controllers chamam Application; Domain nao depende de infraestrutura.
- Trabalhe em slices pequenos e revisaveis.
- Exija idempotencia em criacao de pagamento.
- Derive tenant/application de `ITenantContext` em endpoints autenticados; nunca do body.
- Persista webhooks em Inbox antes do processamento e eventos de saida via Outbox.
- Use `FakePaymentProvider` para desenvolvimento/testes quando provider real nao for explicitamente pedido.

## Seguranca

- Nunca armazenar cartao, CVV, secrets ou API Keys reais.
- API Key deve ser persistida apenas como hash.
- Nao logar credenciais, tokens, payloads sensiveis ou chaves de provider.
- Validar assinatura de webhooks quando o provider suportar.
- Alteracoes em autenticacao, autorizacao, banco ou pipelines exigem cuidado extra e evidencia.

## Comandos conhecidos

```bash
dotnet restore
dotnet build
dotnet test
docker compose config
docker compose up -d
```

## Done

- Codigo/docs respeitam specs, ADRs e instrucoes aplicaveis.
- Testes ou validacoes proporcionais foram executados ou justificados.
- Arquivos alterados, evidencias e riscos residuais foram registrados.
- `docs/harness/learnings.md` foi atualizado quando houve aprendizado reutilizavel.

## Nao fazer

- Nao implementar feature de produto fora do escopo do pedido.
- Nao criar documentacao generica que duplique specs existentes.
- Nao alterar migrations, auth, secrets, Docker ou CI sem validar impacto.
- Nao fazer merge automatico nem remover testes para mascarar falhas.
