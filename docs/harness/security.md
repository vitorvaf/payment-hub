# Security Rules

Estas regras são obrigatórias para o Payment Gateway MVP.

## Dados de pagamento

- Nunca armazenar número de cartão.
- Nunca armazenar CVV.
- Usar checkout hospedado no MVP.
- Não implementar cartão salvo no MVP.

## Secrets e credenciais

- Nunca logar secrets.
- Nunca commitar `.env` real.
- Nunca commitar API Keys reais.
- API Key deve ser armazenada como hash.
- Credenciais de provedores devem ser criptografadas ou preparadas para criptografia.
- Dados sensíveis não devem ir para logs, traces, mensagens de erro ou respostas HTTP.

## Comunicação e autenticação

- Chamadas entre sistemas devem usar API Key server-to-server.
- Webhooks devem usar assinatura ou validação equivalente quando suportado pelo provedor.
- Todo endpoint de criação de pagamento deve exigir idempotência.

## Processamento confiável

- Todo webhook deve ser persistido em Inbox antes de processar.
- Todo evento de saída deve passar por Outbox.
- Retries devem ser seguros para reexecução.
- Processamento duplicado deve ser tratado por idempotência.

## Auditoria

- Ações administrativas devem gerar AuditLog.
- Alterações de credenciais, tenants, provedores e configurações sensíveis devem ser auditáveis.

## Fora de escopo seguro para o MVP

- Antifraude complexo.
- Conciliação completa.
- Split financeiro.
- Wallet.
- Broker externo obrigatório.
