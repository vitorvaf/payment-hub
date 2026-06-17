# Visao de Produto e Fronteiras do Payment Hub

## Objetivo

Formalizar a visao de produto do Payment Hub, definir o que ele e e o que ele nao e, identificar personas tecnicas e consumidores esperados, e delimitar as fronteiras de responsabilidade.

Este documento complementa `001-glossary-and-boundaries.md` (glossario e limites operacionais) com o contexto de produto e decisoes de escopo de longo prazo.

## Fora de escopo deste documento

- Contratos HTTP detalhados: ver `009-api-contracts.md`.
- Schema de banco: ver `010-database-contract.md`.
- Glossario tecnico: ver `001-glossary-and-boundaries.md`.

---

## Declaracao de visao

O Payment Hub e um orquestrador de pagamentos multitenant que centraliza integracoes com provedores de pagamento externos e oferece uma API consistente para sistemas internos da organizacao.

O objetivo e que qualquer produto da organizacao possa aceitar pagamentos sem reimplementar integracao de provider, sem lidar com webhooks de provider diretamente e sem manter credenciais de gateway individualmente.

---

## O que o Payment Hub E

- **Gateway de pagamento interno**: ponto unico de integracao para provedores externos como AbacatePay, Stripe e MercadoPago.
- **Orquestrador de estado canonico**: traduz vocabularios de providers diferentes para um conjunto unico de status de pagamento.
- **Plataforma multitenant**: isola configuracao, credenciais e dados entre organizacoes e suas aplicacoes consumidoras.
- **Emissor de eventos internos**: notifica sistemas consumidores sobre mudancas de estado de pagamento via webhook interno assinado.
- **Inbox de webhooks externos**: persiste eventos de providers antes de qualquer processamento, garantindo que nenhum evento seja perdido por falha temporaria.
- **Gerenciador de tentativas e retries**: reagenda processamento e dispatch em caso de falha com politica de backoff.
- **Preparado para escala**: arquitetura Inbox/Outbox permite migracao para broker externo sem reescrita do dominio.

---

## O que o Payment Hub NAO E

- **Nao e instituicao de pagamento**: nao processa pagamentos diretamente, nao detém saldo financeiro e nao emite instrumentos de pagamento.
- **Nao armazena dados de cartao**: numero de cartao, validade, CVV e PAN mascarado nao existem no Payment Hub. Checkout e sempre hospedado pelo provider.
- **Nao executa regra de negocio pos-pagamento**: liberar plano, entregar produto ou ativar recurso e responsabilidade da aplicacao consumidora.
- **Nao e processador de split financeiro no MVP**: divisao de valores, marketplace e repasse financeiro estao fora do MVP.
- **Nao gerencia recorrencia completa no MVP**: assinaturas com cobrana automatica periodica estao fora do MVP.
- **Nao e broker de mensagens**: o Outbox em Postgres e um padrao de persistencia; o Payment Hub nao e um substituto para RabbitMQ, Kafka ou Azure Service Bus.
- **Nao e sistema antifraude**: analise de risco e decisao de fraude sao responsabilidades externas.

---

## Personas tecnicas

### Operador do Payment Hub

Responsavel pela instalacao, configuracao e operacao do Payment Hub. Tem acesso privilegiado para criar tenants, gerenciar providers e monitorar saude do sistema.

Necessidades:
- Criar e gerenciar tenants.
- Configurar provider accounts por tenant e application.
- Monitorar health checks, workers e filas.
- Auditar acoes administrativas sensiveis.

### Tenant Admin

Representante de uma organizacao que usa o Payment Hub. Nao acessa o codigo; configura sua organizacao via API ou painel admin (Phase 5).

Necessidades:
- Criar applications dentro do tenant.
- Gerenciar API Keys de suas applications.
- Configurar providers para cada application.
- Visualizar pagamentos do seu tenant.

### Desenvolvedor de Aplicacao (Application Developer)

Engenheiro que integra o sistema consumidor (ex.: Job Search) com o Payment Hub via API server-to-server.

Necessidades:
- Criar checkouts com idempotencia.
- Receber webhooks internos assinados.
- Consultar status de pagamentos.
- Depurar erros com mensagens claras.

---

## Sistemas consumidores

### Job Search / Quero Vagas Tech (consumidor primario)

Plataforma de emprego que usa o Payment Hub para aceitar pagamentos de planos premium. Responsavel por:
- Criar checkout passando `externalReference` da ordem interna.
- Receber webhook interno, validar assinatura HMAC e liberar o recurso.
- Garantir idempotencia propria usando `eventId` do webhook.

Ver spec `014-job-search-integration.md` para o fluxo detalhado.

### Futuros produtos internos

O Payment Hub e desenhado para servir multiplos produtos da organizacao como tenants distintos.

---

## Providers externos suportados

| Provider | Status no MVP | Ambiente |
|----------|--------------|---------|
| Fake | Funcional (dev/testes) | Local |
| AbacatePay | Skeleton (funcional na Phase 2) | Sandbox / Production |
| Stripe | Skeleton (funcional na Phase 4) | Sandbox / Production |
| MercadoPago | Skeleton (funcional na Phase 4) | Sandbox / Production |

### Separacao de ambiente por provider

Cada `ProviderAccount` carrega o campo `ProviderEnvironment` com valor `Sandbox` ou `Production`. O Payment Hub nao mistura credenciais de ambientes. Em Development, o provider Fake pode ser usado automaticamente como fallback.

---

## Principios de arquitetura

1. **Dominio independente de infraestrutura**: `PaymentHub.Domain` nao depende de banco, HTTP ou provider. Invariantes sao testadas sem banco.
2. **Status canonico sempre interno**: nenhum vocabulario de provider externo vaza para a API ou para o dominio.
3. **Inbox antes de processar**: webhooks externos sao persistidos antes de qualquer logica pesada. Falha de processamento nao perde o evento.
4. **Outbox antes de despachar**: eventos internos sao persistidos antes do dispatch HTTP. Falha de entrega nao perde o evento.
5. **Secrets nunca em claro no banco**: API Keys como hash, credenciais de provider criptografadas, webhook secrets com protecao em repouso.
6. **Hosted checkout obrigatorio**: sem formulario proprio de cartao no MVP.
7. **Idempotencia como contrato**: criacao de checkout exige `Idempotency-Key`; reprocessamento de eventos preserva `eventId`.
8. **Isolamento multitenant em todas as tabelas**: toda entidade carrega `tenant_id` e/ou `application_id`.
9. **Preparado para broker sem reescrita**: `IApplicationWebhookDispatcher` abstrai o mecanismo de entrega; substituir Outbox por broker externo nao altera o dominio.

---

## Fronteira de escopo do MVP

O MVP e definido em `000-mvp-scope.md`. Resumidamente:

**Incluso no MVP:**
- Tenants, applications e provider accounts.
- API Key server-to-server.
- Checkout hospedado com provider Fake e estrutura para provedores reais.
- Inbox de webhooks externos.
- Status canonico e transicoes.
- Outbox de eventos internos.
- Workers de processamento e dispatch.
- Documentacao, specs, ADRs e testes proporcionais.

**Excluido do MVP (qualquer funcionalidade abaixo exige decisao explicita):**
- Split financeiro, wallet, cartao salvo, CVV.
- Recorrencia completa.
- Antifraude complexo.
- Conciliacao financeira completa.
- Painel admin completo.
- Broker externo.
- O Payment Hub atuar como instituicao de pagamento.

---

## Decisoes pendentes

| # | Decisao | Contexto | Urgencia |
|---|---------|---------|---------|
| D-01 | Politica de autenticacao para endpoints de bootstrap/admin (`POST /tenants`, `POST /applications`) | Hoje o middleware exige API Key para todos os endpoints nao anonimos, mas nao ha API Key antes de criar o primeiro tenant. Precisa de politica explicita (ex.: token de admin, seed inicial, claims especiais). | Alta — gap P1 na auditoria |
| D-02 | Protecao em repouso de `ApplicationClient.WebhookSecret` | O `webhook_secret` esta persistido sem criptografia equivalente a credenciais de provider. Decidir entre criptografia AES, KMS, hash com rotacao ou aceitar risco com mitigacoes documentadas. | Alta — gap P1 na auditoria |
| D-03 | Autenticacao do painel admin (Phase 5) | API Key S2S nao e adequada para acesso humano interativo. Necessita decisao: OAuth, OIDC, magic link, ou outro mecanismo. | Media — necessaria antes da Phase 5 |
| D-04 | FKs obrigatorias no banco | Spec de banco nao define explicitamente quais referencias sao FK no banco e quais sao apenas logicas. Decidir por integridade referencial completa ou parcial com justificativa de MVP. | Media — gap P2 na auditoria |
| D-05 | Selecao de provider default sem `ProviderAccount` em Development | Spec permite Fake automaticamente em Development. Definir se isso deve ser configuravel ou hardcoded. | Baixa |

---

## Arquivos relacionados

- `docs/specs/000-mvp-scope.md`
- `docs/specs/001-glossary-and-boundaries.md`
- `docs/adr/ADR-0003-hosted-checkout-only.md`
- `docs/adr/ADR-0004-api-key-server-to-server.md`
- `docs/architecture/overview.md`
- `docs/harness/project-context.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
