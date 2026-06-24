---
applyTo: "src/**/*.{cs,json};tests/**/*.cs;docker-compose.yml;docs/**/*.md"
---

# Security Instructions

Leia `docs/harness/security.md` antes de alterar fluxos sensiveis.

- Nunca armazenar numero de cartao ou CVV.
- Nunca logar secrets, API Keys, tokens ou credenciais de providers.
- Nunca commitar `.env` real ou chaves reais.
- Armazenar API Key apenas como hash.
- Preparar credenciais de providers para criptografia.
- Validar assinatura de webhooks quando o provider suportar.
- Exigir API Key server-to-server entre sistemas.
- Exigir idempotencia na criacao de pagamentos.
- Persistir webhook antes de processar.
- Enviar eventos de saida via Outbox.
- Gerar AuditLog para acoes administrativas.
- Em endpoints autenticados, derive tenant/application de `ITenantContext`; nao aceite esses IDs no body.
