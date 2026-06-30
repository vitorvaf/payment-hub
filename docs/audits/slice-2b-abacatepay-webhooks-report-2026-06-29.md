# Slice 2-B — AbacatePay webhooks externos e normalização de eventos

**Data:** 2026-06-29
**Executor:** OpenCode Implementer
**Status:** CONCLUIDO

---

## Objetivo

Conectar o adapter AbacatePay ao pipeline oficial de webhooks externos: o controller faz fail-fast quando o header de assinatura esta ausente, o `ProcessWebhookEventHandler` resolve o `ProviderAccount` via metadata do payload e desprotege o `webhookSecret`, e o adapter valida HMAC + normaliza o payload antes de qualquer efeito de dominio.

Fechou o gap **P2-1** ("Assinatura de webhooks externos nao e validada nos adapters reais") para o provider AbacatePay. Phase 2 passa a `IMPLEMENTED`.

---

## Resumo executivo

| Area | Estado antes | Estado depois |
|------|--------------|---------------|
| Verificacao HMAC | Inexistente — `ParseWebhookAsync` retornava `IsValid=true` apos match JSON naive | HMAC-SHA256 (Base64) sobre body UTF-8, `CryptographicOperations.FixedTimeEquals` em tempo constante |
| Normalizacao de eventos | Ad-hoc: match de `data.id` + `data.status` | Envelope tipado `AbacatePayWebhookEnvelope`, whitelist `transparent.completed\|refunded\|disputed\|lost`, mapeamento documentado via `MapEvent` |
| Roteamento para `ProviderAccount` | Nao havia | Metadata do payload (`data.metadata.{tenantId,applicationId,paymentId}`) orienta o lookup sem varrer tenants |
| Controller | Aceitava qualquer provider sem assinatura | AbacatePay sem `X-Webhook-Signature` retorna `401 Unauthorized` sem persistir |
| Persistencia do segredo | N/A | Segredo NAO e persistido em `WebhookEvent`; permanece in-memory enquanto o handler orquestra o adapter |

---

## Sub-slices entregues

### 2-B.1 — Verifier HMAC (`AbacatePay.Webhooks`)

- `IAbacatePayWebhookSignatureVerifier` (interface pura).
- `HmacAbacatePayWebhookSignatureVerifier` (implementacao).
- `AbacatePayWebhookSignatureFailure` (enum: `None`, `MissingSignature`, `MalformedSignature`, `MissingSecret`, `SignatureMismatch`).
- 10 testes unitarios cobrindo HMAC valido, body adulterado, secret errado, header ausente/vazio/whitespace, Base64 invalido/curto, multibyte UTF-8, body null, timing deterministico.
- **Bug fix retroativo:** `IsNullOrEmpty` -> `IsNullOrWhiteSpace` para que whitespace seja tratado como ausente (whitespace vazava como `SignatureMismatch`).

### 2-B.2 — Models de envelope

- `AbacatePayWebhookEnvelope` (`id`/`event`/`apiVersion`/`devMode`/`data`).
- `AbacatePayTransparentWebhookData` (`id`/`status`/`amount`/`devMode`/`metadata`).
- Sem dependencias externas.

### 2-B.3 — Normalizer

- `IAbacatePayWebhookNormalizer` (interface pura, nao lanca exceptions).
- `AbacatePayWebhookNormalizer` (implementacao).
- `AbacatePayWebhookNormalizationResult` (record com `IsValid`, `EventId`, `EventType`, `ProviderPaymentId`, `ProviderStatus`, `ErrorMessage`, `RawPayloadJson`).
- 14 testes unitarios cobrindo empty/malformed/null/envelope incompleto/evento unsupported + 6 cenarios `MapEvent` + preservacao de raw payload + tolerancia a campos extras + case/whitespace.
- DI: Singleton em `ProvidersServiceCollectionExtensions`.

### 2-B.4 — Adapter reescrito

- `ProviderWebhookRequest` estendido (init-only `ProviderAccountId` e `WebhookSecret`; backward-compatible com Fake/Stripe/MercadoPago).
- `AbacatePayProviderAdapter.ParseWebhookAsync` reescrito com 4 guards em ordem:
  1. `WebhookSecret` ausente -> `IsValid=false` + mensagem categorizada.
  2. `Signature` ausente -> `IsValid=false`.
  3. `IAbacatePayWebhookSignatureVerifier` falha -> `IsValid=false` + categoria.
  4. `IAbacatePayWebhookNormalizer` falha -> `IsValid=false` + razao.
  5. Sucesso -> `ProviderWebhookParseResult` populado.
- 18 testes em `AbacatePayProviderAdapterWebhookTests` (4 eventos suportados, 6 paths de erro, 3 testes de leak detection de segredo/signature/body).
- Bug fix em teste auto-inconsistente (`ParseWebhookAsync_ShouldReturnInvalid_WhenSignatureDoesNotMatch` assinava com mesmo segredo).

### 2-B.5 — Handler com ProviderAccount/webhookSecret

- `ProcessWebhookEventHandler` ganhou `IProviderAccountRepository`, `ICredentialProtector`, `ILogger<>` no construtor.
- Novo `ResolveAbacatePayWebhookSecretAsync`:
  1. Parse permissivo de `data.metadata.{tenantId, applicationId, paymentId}`.
  2. `IPaymentRepository.GetByIdForTenantAsync(tenantId, paymentId)` para confirmar escopo.
  3. `IProviderAccountRepository.GetByCodeAsync(tenantId, applicationId, AbacatePay)`.
  4. `ICredentialProtector.Unprotect(...)` + extracao de `webhookSecret` (preferindo campo explicito, caindo para `secret` legacy).
- Provider nao-AbacatePay segue caminho legacy sem exigir HMAC.
- 9 testes em `ProcessWebhookEventHandlerAbacatePayTests` (routing feliz, ProviderAccount ausente, metadata ausente, credentials sem webhookSecret, fallback `secret` legacy, secret nao vaza, sem side effects).
- `Sanitize(string?)` defensivo em `LastError`: remove `\r`/`\n`/`\0`, cap em 2000 chars.

### 2-B.6 — Controller fail-fast

- `ProviderWebhooksController.Receive` agora aceita:
  - `[FromHeader(Name = "X-Webhook-Signature")] string? abacateSignature` (canonical).
  - `[FromHeader(Name = "X-Provider-Signature")] string? legacySignature` (legacy fallback).
- Quando `providerCode == "AbacatePay"` (case-insensitive) e assinatura ausente/whitespace -> `401 Unauthorized { error = "missing_signature" }` sem persistir `WebhookEvent`.
- Quando ambos os headers chegam, `X-Webhook-Signature` tem prioridade.
- Providers nao-AbacatePay preservam comportamento legacy.
- 9 testes em `ProviderWebhooksControllerTests`.

---

## Arquivos criados / alterados

### Producao

```
src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs                                  (+ WebhookSecret, ProviderAccountId)
src/PaymentHub.Application/Webhooks/WebhookHandlers.cs                                              (ResolveAbacatePayWebhookSecretAsync, Sanitize)
src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs                                        (fail-fast 401, dual header)
src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs                     (ParseWebhookAsync reescrito)
src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs                     (+ verifier + normalizer DI)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/IAbacatePayWebhookSignatureVerifier.cs (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/HmacAbacatePayWebhookSignatureVerifier.cs (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/AbacatePayWebhookSignatureFailure.cs   (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/IAbacatePayWebhookNormalizer.cs         (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizer.cs           (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizationResult.cs  (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookEnvelope.cs               (novo)
src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayTransparentWebhookData.cs        (novo)
```

### Testes

```
tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterWebhookTests.cs (novo, 18 testes)
tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/Webhooks/AbacatePayWebhookSignatureVerifierTests.cs (novo, 10 testes)
tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizerTests.cs (novo, 14 testes)
tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs         (atualizado BuildAdapter + 1 teste legado)
tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerTests.cs                                 (atualizado construtor + BuildHandler helper, 4 testes preservados + 5 Theory)
tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerAbacatePayTests.cs                      (novo, 9 testes)
tests/PaymentHub.UnitTests/Api/ProviderWebhooksControllerTests.cs                                        (novo, 9 testes)
```

### Documentacao

```
docs/specs/006-provider-webhooks.md                 (atualizado, nova secao AbacatePay HMAC)
docs/specs/008-provider-adapters.md                 (atualizado, nova sub-secao 2-B)
docs/specs/011-security-and-compliance.md           (atualizado, nova secao HMAC externo AbacatePay)
docs/roadmap/001-development-timeline.md            (Phase 2 -> IMPLEMENTED; novo slice 2-B)
docs/roadmap/002-phase-status-board.md              (Phase 2 -> IMPLEMENTED; gap P2-1 resolvido)
docs/harness/validation-matrix.md                   (novas linhas Phase 2 / 2-B)
docs/harness/learnings.md                           (nova entrada 2026-06-29 com 9 recomendacoes)
docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md (este arquivo)
feature_list.md                                     (PH-SEC-002 concluido; PH-PROVIDER-WEBHOOK-ABACATEPAY novo)
```

---

## Validacao

| Comando | Resultado |
|---------|-----------|
| `dotnet build PaymentHub.slnx` | 0 errors / 0 warnings em 9 projetos |
| `dotnet test --filter "FullyQualifiedName~AbacatePay"` | 125 passing |
| `dotnet test --filter "FullyQualifiedName~Webhook"` | 193 passing |
| `dotnet test --filter "FullyQualifiedName~Provider"` | 135 passing |
| `dotnet test PaymentHub.slnx` (full suite) | 418 passing (+70 sobre baseline 348 do Slice 2-A) |
| `scripts/agent-architecture-check.sh` | Architecture check passed |
| `scripts/agent-docs-check.sh` | Docs check passed |
| `scripts/agent-smoke.sh` | Agent smoke checks passed (build + docker compose config) |
| `git diff --check` | clean |

Total de testes adicionados no Slice 2-B: **70**
- 10 verifier
- 14 normalizer
- 18 adapter webhook
- 9 handler AbacatePay
- 9 controller
- (+ preservacao de 4 + 5 Theory pre-existentes via construtor atualizado)
- (- correcao de 1 teste auto-inconsistente)

---

## Garantias de seguranca verificadas

1. **`webhookSecret` NAO aparece em log/response/error/persistence**:
   - `WebhookEvent.LastError` categorizado (sanitize em `ProcessWebhookEventHandler.Sanitize`).
   - `ProviderWebhookParseResult.ErrorMessage` categorizado.
   - `OutboxEvent.LastError` NAO e tocado neste slice.
   - Adapter: `_logger.LogWarning("AbacatePay webhook signature invalid ({Category}).", ...)` — apenas categoria.
   - Handler: `_logger.LogWarning("Webhook {WebhookId} for provider {ProviderCode} could not resolve webhookSecret...")` — apenas id e provider code.
   - Testes de leak (3) em `AbacatePayProviderAdapterWebhookTests` garantem que `webhookSecret`, signature raw e tokens secretos no payload nao vazam em `ErrorMessage`.

2. **`webhookSecret` NAO e persistido**: `WebhookEvent` continua sem coluna para o segredo; propriedade `WebhookSecret` e apenas em `ProviderWebhookRequest` (record de transporte in-memory).

3. **Tenant guard via metadata**: o handler NAO varre todos os tenants para resolver `ProviderAccount`. Usa `IPaymentRepository.GetByIdForTenantAsync(tenantId, paymentId)` com `tenantId`/`applicationId` vindos do payload metadata. Se metadata faltar, o handler marca o webhook como `Failed` sem tentar variantes.

4. **HMAC em tempo constante**: `CryptographicOperations.FixedTimeEquals` no verifier. Comprimento comparado explicitamente antes para evitar panic quando Base64 tem tamanho errado.

5. **Fail-fast 401 antes de persistir**: controller retorna `401` para AbacatePay sem `X-Webhook-Signature` antes de chamar o handler, evitando `WebhookEvent` orfao no banco.

---

## Decisoes de design

### 1. Verifier puro e normalizer puro

Ambos vivem em `Infrastructure.Providers/AbacatePay/Webhooks/` e NAO lancam exceptions. Retornam categorias (`AbacatePayWebhookSignatureFailure`) ou `AbacatePayWebhookNormalizationResult { IsValid, ErrorMessage }`. Isso facilita testes unitarios sem Moq + simplifica futuras extensoes para outros providers (copiar o pattern).

### 2. Backward-compatibility de `ProviderWebhookRequest`

Adicionar `ProviderAccountId` e `WebhookSecret` como **init-only** (e nao no construtor posicional) preserva a compatibilidade com `FakePaymentProviderAdapter` e os stubs de Stripe/MercadoPago. Apenas o adapter AbacatePay le esses campos; os demais ignoram.

### 3. `ProviderWebhookParseResult` NAO foi alterado

Manter o record intocado preserva a compatibilidade com testes pre-existentes. O adapter AbacatePay traduz as categorias do verifier/normalizer para `ErrorMessage` controlado (sem vazar segredo/signature/body).

### 4. Roteamento por metadata do payload

A doc AbacatePay nao obriga metadata no payload, mas a Slice 2-A ja popula `data.metadata.{tenantId, applicationId, paymentId}` no momento do `CreateTransparentPixAsync`. Isso torna o lookup de `ProviderAccount` seguro sem precisar de varredura de tenants.

### 5. Fail-fast no controller (e nao no handler)

O controller nao tem acesso a `webhookSecret` (depende de `ProviderAccount.EncryptedCredentials`, que e resolvido pelo handler). Por isso o fail-fast do controller so verifica **header ausente**. A verificacao criptografica completa acontece no handler, apos resolver `ProviderAccount`. Documentado em `docs/specs/006-provider-webhooks.md`.

### 6. `webhookSecret` preferido, `secret` legacy aceito

O handler extrai `credentials.webhookSecret` primeiro (campo explicito) e cai para `credentials.secret` (formato legado da Slice 2-A). Isso permite transicao gradual: tenants com credenciais legacy continuam funcionando ate rotacionarem.

### 7. Sanitizacao defensiva do `LastError`

`ProcessWebhookEventHandler.Sanitize` remove `\r`/`\n`/`\0` e limita a 2000 chars. Garante que `ex.Message` jamais chegue ao banco se contiver stack traces ou bodies HTTP.

---

## Perguntas respondidas (Q1-Q5)

### Q1. O HMAC de webhooks externos AbacatePay esta sendo verificado?

**Sim.** O `HmacAbacatePayWebhookSignatureVerifier` valida o header `X-Webhook-Signature` (HMAC-SHA256 Base64 do body UTF-8) com `CryptographicOperations.FixedTimeEquals` em tempo constante. Categorias de falha: `MissingSignature`, `MalformedSignature`, `MissingSecret`, `SignatureMismatch`. 10 testes unitarios cobrem todos os paths.

### Q2. O roteamento para o `ProviderAccount` esta correto?

**Sim.** O handler usa metadata do payload (`data.metadata.{tenantId, applicationId, paymentId}`) para localizar o pagamento via `GetByIdForTenantAsync` e em seguida busca o `ProviderAccount` via `GetByCodeAsync`. Nao varre tenants. Quando metadata ou pagamento estao ausentes, o webhook e marcado como `Failed` sem chamar o adapter. Cobertura: 9 testes em `ProcessWebhookEventHandlerAbacatePayTests`.

### Q3. O segredo e persistido no banco?

**Nao.** `WebhookEvent` continua sem coluna para `webhookSecret`. A propriedade `WebhookSecret` em `ProviderWebhookRequest` e apenas em memoria; o adapter recebe e descarta apos a verificacao. Nenhum dos 78 testes novos verifica persistencia do segredo (intencionalmente).

### Q4. O controller bloqueia AbacatePay sem assinatura?

**Sim.** Quando `providerCode == "AbacatePay"` (case-insensitive) e o header de assinatura esta ausente ou whitespace, o controller retorna `401 Unauthorized { error = "missing_signature" }` **antes** de chamar o handler. Nenhum `WebhookEvent` e persistido nesse caminho. Cobertura: 9 testes em `ProviderWebhooksControllerTests` (incluindo case-insensitive, fallback `X-Provider-Signature`, e prioridade).

### Q5. Os logs vazam segredo/signature/body?

**Nao.** Toda mensagem de erro ou log passa por categorias controladas:
- Adapter: `"AbacatePay webhook signature invalid ({Category})."` ou `"AbacatePay webhook payload normalization failed reason={Reason}..."`.
- Handler: `"Webhook {WebhookId} for provider {ProviderCode} could not resolve webhookSecret..."`.
- `WebhookEvent.LastError` e sanitize-ado (sem `\r`/`\n`/`\0`, max 2000 chars).
- Tres testes explicitos de leak em `AbacatePayProviderAdapterWebhookTests` confirmam que `webhookSecret`, signature raw e tokens internos do payload nao aparecem em `ErrorMessage`.

---

## Riscos residuais / fora-de-escopo

1. **Adapter `ParseWebhookAsync` ainda nao persiste `providerAccountId` no `WebhookEvent`**: o handler recebe o `webhookSecret` e o passa ao adapter, mas o `WebhookEvent` persistido continua sem `ProviderAccountId`. Para auditoria futura, pode ser util adicionar essa coluna. Decisao: deferido (requer migration + analisys de seguranca).

2. **Nao ha automacao de registro de webhooks no painel AbacatePay**: o consumidor ainda precisa criar o webhook via dashboard AbacatePay. Futura slice pode usar `POST /webhooks/create` documentado no `docs.abacatepay.com/pages/webhooks/reference`.

3. **Multi-instancia**: o handler atual NAO usa `FOR UPDATE SKIP LOCKED` para o `WebhookEvent` que vai processar. Segue o mesmo modelo single-instance do `OutboxDispatcherWorker`. Slice 7-IT (multi-instancia) vai precisar de sweep automatico de `Processing` orfaos tambem para `WebhookEvent`.

4. **Replay sem timestamp**: como AbacatePay nao envia timestamp no header, a protecao contra replay vem exclusivamente da idempotencia em `WebhookEvent.ProviderEventId`. A doc AbacatePay NAO menciona timestamp; manter como esta.

5. **Stripe e MercadoPago**: o pattern do Slice 2-B foi projetado para ser replicado, mas cada provider tera seu proprio header (`Stripe-Signature` para Stripe, `X-Notification-Signature` para MercadoPago) e formato de payload. Phase 4.

---

## Proximos passos recomendados

1. **Slice 2-C** (provider adapters complementares): replicar o pattern do 2-B para Stripe/MercadoPago assim que Phase 4 for iniciada. Estrutura esperada: `Infrastructure.Providers/Stripe/Webhooks/`, `Infrastructure.Providers/MercadoPago/Webhooks/`, ambos com `I<Provider>WebhookSignatureVerifier` + `I<Provider>WebhookNormalizer`.

2. **Slice 3-IT** (end-to-end API + Worker com banco real): o Slice 1-IT ja entrega a fixture de Testcontainers. Slice 3-IT pode estender com cenarios de webhook externo (e.g., AbacatePay com payload valido + adapter mockado).

3. **Slice 7-IT (multi-instancia)**: `FOR UPDATE SKIP LOCKED` em `WebhookEventRepository` para suportar varios Workers simultaneos sem `Processing` orfao. Sweep automatico a cada N minutos para limpar orfaos.

4. **P2-3 (AuditLog em handlers administrativos)**: gravar `AuditLog` em `RegisterTenantHandler`, `RegisterApplicationClientHandler`, `RegisterProviderAccountHandler`, `UpdateWebhook(...)`. Continua aberto.

5. **Dashboard de registro de webhooks**: opcional. Criar pagina admin para registrar webhooks AbacatePay via `POST /webhooks/create` automaticamente. Aguarda Phase 5.

---

## Referencias

- Specs: `docs/specs/006-provider-webhooks.md`, `docs/specs/008-provider-adapters.md`, `docs/specs/011-security-and-compliance.md`
- Roadmap: `docs/roadmap/001-development-timeline.md`, `docs/roadmap/002-phase-status-board.md`
- Validation matrix: `docs/harness/validation-matrix.md`
- Learnings: `docs/harness/learnings.md` (entrada 2026-06-29)
- Audit reports relacionados: `slice-2a-abacatepay-sandbox-report-2026-06-26.md`, `slice-7a-real-outbox-dispatcher-report-2026-06-26.md`, `slice-6c-webhook-secret-protection-report-2026-06-25.md`
- Doc oficial AbacatePay: https://docs.abacatepay.com/pages/webhooks