# Security Instructions

Leia e siga `docs/harness/security.md` antes de alterar código.

Regras essenciais:

- Nunca armazenar número de cartão ou CVV.
- Nunca logar secrets, API Keys, tokens ou credenciais de provedores.
- Nunca commitar `.env` real ou chaves reais.
- Armazenar API Key apenas como hash.
- Preparar credenciais de provedores para criptografia.
- Validar assinatura de webhooks quando o provedor suportar.
- Exigir API Key server-to-server entre sistemas.
- Exigir idempotência na criação de pagamentos.
- Persistir webhook antes de processar.
- Enviar eventos de saída via Outbox.
- Gerar AuditLog para ações administrativas.
