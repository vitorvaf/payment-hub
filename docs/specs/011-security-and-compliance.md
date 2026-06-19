# Seguranca e Compliance

## Objetivo

Consolidar regras de seguranca obrigatorias para o MVP.

## Escopo

- Dados de pagamento.
- Secrets e credenciais.
- API Key, webhooks, HTTPS, logs e erros.
- Auditoria de acoes sensiveis.

## Fora de escopo

- Certificacao PCI completa.
- Antifraude complexo.
- Wallet, split e custodia de saldo.

## Regras obrigatorias

- Nao armazenar cartao.
- Nunca armazenar CVV.
- Nao logar secrets, API Keys, tokens ou credenciais.
- Nao commitar `.env` real.
- API Key apenas como hash.
- Credenciais de providers criptografadas ou preparadas para criptografia.
- HMAC para webhooks internos.
- Validacao de assinatura em webhooks externos quando suportado.
- HTTPS obrigatorio em producao.
- Logs sem dados sensiveis.
- Erros sem stack trace em producao.
- `AuditLog` para acoes administrativas.
- Hosted checkout como regra do MVP.

## Politica de bootstrap e admin seed

- Toda criacao automatica de dados iniciais (tenant, application, API Key, provider account) deve passar por `IBootstrapPolicy`.
- `Production` nao cria dados sensiveis automaticamente. `Bootstrap:AllowProductionBootstrap` precisa estar `true` para que qualquer seed rode em `Production`. Padrao seguro: `false`.
- `Development` e `Test` podem rodar seed automatico apenas com `Bootstrap:Enabled=true` e `Bootstrap:SeedDevelopmentData=true`. Padrao seguro: `false`.
- O seedor nunca loga API Key raw, secrets, webhook secrets, tokens, senhas ou connection strings. Logs podem registrar ids, slugs, ambiente e decisao politica.
- O seedor e idempotente: usa `ITenantRepository.GetBySlugAsync` e `IApplicationClientRepository.GetByTenantAndNameAsync` antes de criar; rodar N vezes nao duplica dados.
- Configuracao ausente ou invalida deve produzir comportamento seguro: `Production` nao cria nada; `Development`/`Test` apenas loga "skipped" se o opt-in nao estiver presente.
- Endpoints publicos de bootstrap (`POST /api/v1/tenants`, `POST /api/v1/applications`, `POST /api/v1/provider-accounts`) permanecem sob `ApiKeyAuthenticationMiddleware`. A politica de bootstrap nao introduz bypass de autenticacao no MVP.
- Detalhes tecnicos em `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`.

## Contratos

| Area | Contrato |
|------|----------|
| API Key | `Authorization: Bearer`, hash HMAC no banco, claro exibido uma vez |
| Provider credentials | JSON protegido por criptografia |
| Webhook interno | `X-PaymentHub-Signature` HMAC-SHA256 sobre `timestamp.rawBody` |
| Webhook externo | Assinatura validada quando provider oferecer |
| Logs | correlation id e contexto sem secrets |
| Tenant/application em endpoints autenticados | Derivado exclusivamente de `ITenantContext` (populado pelo middleware). Body/headers do request nunca podem sobrescrever tenant/application. |

### HMAC de webhook interno

- Algoritmo: `HMAC-SHA256`.
- Encoding do payload: UTF-8.
- Formato da assinatura: hexadecimal lowercase.
- Header de timestamp: `X-PaymentHub-Timestamp`, em Unix time seconds.
- Header de assinatura: `X-PaymentHub-Signature`.
- String assinada: `{timestamp}.{rawBody}`.
- Tolerancia recomendada para consumidores: 5 minutos.
- Prevencao de replay: consumidor deve rejeitar timestamp fora da janela e aplicar idempotencia por `eventId`.
- `rawBody` deve ser o corpo HTTP exatamente como recebido, sem reserializar o JSON.
- Secrets de webhook nunca devem aparecer em logs, erros ou traces.
- Assinaturas devem ser comparadas em tempo constante quando a plataforma permitir.

Exemplo:

```text
rawBody = corpo HTTP exatamente como enviado
timestamp = valor do header X-PaymentHub-Timestamp
signedPayload = timestamp + "." + rawBody
signature = HMACSHA256(webhookSecret, UTF8(signedPayload))
signatureFormat = hexadecimal lowercase
```

Exemplo C# para gerar hex:

```csharp
var signatureBytes = HMACSHA256.HashData(secretBytes, signedPayloadBytes);
var signature = Convert.ToHexString(signatureBytes).ToLowerInvariant();
```

## Criterios de aceite

- Revisao de seguranca nao encontra secrets reais em repo.
- Nenhum fluxo exige dados de cartao no Payment Hub.
- Webhooks internos podem ser verificados pela aplicacao cliente.

## Testes esperados

- API Key invalida e incompatibil.
- Idempotency key ausente.
- Payload duplicado.
- HMAC de webhook interno.
- Logs sem secrets quando possivel.

## Arquivos relacionados

- `docs/harness/security.md`
- `.github/instructions/security.instructions.md`
- `src/PaymentHub.Infrastructure.Postgres/Security/`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
