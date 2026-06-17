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

## Contratos

| Area | Contrato |
|------|----------|
| API Key | `Authorization: Bearer`, hash HMAC no banco, claro exibido uma vez |
| Provider credentials | JSON protegido por criptografia |
| Webhook interno | `X-PaymentHub-Signature` HMAC-SHA256 sobre `timestamp.rawBody` |
| Webhook externo | Assinatura validada quando provider oferecer |
| Logs | correlation id e contexto sem secrets |

### HMAC de webhook interno

- Algoritmo: `HMAC-SHA256`.
- Encoding do payload: UTF-8.
- Formato da assinatura: hexadecimal lowercase.
- Header de timestamp: `X-PaymentHub-Timestamp`, em Unix time seconds.
- Header de assinatura: `X-PaymentHub-Signature`.
- String assinada: `{timestamp}.{rawBody}`.
- Tolerancia recomendada para consumidores: 5 minutos.
- Prevencao de replay: consumidor deve rejeitar timestamp fora da janela e aplicar idempotencia por `eventId`.

Exemplo:

```text
signed_payload = "{timestamp}.{rawBody}"
signature = HMACSHA256(webhookSecret, signed_payload)
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
