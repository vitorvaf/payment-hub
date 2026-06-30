# Slice 7-IT — OutboxDispatcherWorker End-to-End Report

Data: 2026-06-30
Phase: 7 — Workers, Outbox e processamento assincrono
Specs relacionadas: `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`, `docs/specs/013-testing-strategy.md`
ADRs referenciadas: `ADR-0010-real-outbox-dispatcher-location.md` (dispatcher em `Infrastructure.Postgres`)
Gap enderecado: **P2-2** (parcial) + **M1-integration** (suite E2E do dispatcher).

## Resumo

Ate o Slice 7-IT (2026-06-30), o dispatcher real `HttpApplicationWebhookDispatcher`
existia desde o Slice 7-A mas era coberto apenas por testes unitarios
(`OutboxDispatcherWorkerWithRealDispatcherTests`). Nenhum teste de integracao
exercitava o ciclo de Outbox completo contra o Postgres real + a `PaymentHub.Api`
real, deixando uma lacuna P2 (auditada em 2026-06-17 como `P2-2`). Esta slice
fecha a lacuna introduzindo uma suite E2E em
`tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` que:

- Roda a `PaymentHub.Api` real via `PaymentHubApiFactory` apontando para
  Testcontainers Postgres (compartilhado pelo `PostgresFixture` ja existente da
  Slice 1-IT/3-IT).
- Semeia `Tenant`, `ApplicationClient` (com `WebhookUrl` HTTPS publico e
  `WebhookSecret` protegido por `IWebhookSecretProtector`) e `OutboxEvent`
  (`Pending`) via os mesmos handlers/repository que o codigo de producao usa.
- Invoca `OutboxDispatcherWorker.DispatchOnceAsync(CancellationToken)` uma unica
  vez (o worker hospedado NAO roda dentro do `WebApplicationFactory` por
  testabilidade; decisao herdada da Slice 3-IT).
- Recarrega o `OutboxEvent` do Postgres real e asserta `Status`, `RetryCount`,
  `LastError`, `SentAt`, `NextRetryAt`.
- Inspeciona o `ApplicationWebhookCaptureHandler.Captured` para validar
  headers `X-PaymentHub-*`, body raw e assinatura HMAC.

Suite previa: **484 testes** (apos Slice 2-C). Suite nova: **491 testes** (+7).
Build limpo (0 errors / 0 warnings em 9 projetos). `scripts/agent-architecture-check.sh`,
`scripts/agent-docs-check.sh` e `git diff --check` verdes.

## Decisoes (Q1-Q7)

| # | Questao | Decisao | Justificativa |
|---|---------|---------|---------------|
| **Q1** | Onde rodar o `OutboxDispatcherWorker` em E2E? | **Fora do `WebApplicationFactory`** — instanciar manualmente via `factory.Services` e chamar `DispatchOnceAsync` | `TestServer` nao hospeda `BackgroundService`. A Slice 3-IT fixou essa decisao; muda-la agora exigiria um harness de hosting que NAO traria ganho de fidelidade (a tick e sincrona). Adicionar `InternalsVisibleTo("PaymentHub.IntegrationTests")` em `PaymentHub.Worker.csproj` foi a unica alteracao no codigo de producao. |
| **Q2** | `HttpClient` outbound do dispatcher continua o mesmo do Slice 3-IT? | **SIM** — re-registrar `services.AddHttpClient("application-webhook").ConfigurePrimaryHttpMessageHandler(() => _webhookHandler)` em `ConfigureTestServices` | Ja documentado na Slice 3-IT: o ultimo `PrimaryHandler` ganha, preservando `BaseAddress`/`Timeout` originais (registrados pela API). Sem mudanca. |
| **Q3** | Como exercitar `HttpFailure` sem chamada externa real? | **`ApplicationWebhookCaptureHandler.EnqueueResponse(HttpStatusCode, reasonPhrase)`** — fila FIFO de respostas; default 204 quando vazia | Mantem o default `204 NoContent` (preserva `Captured.Should().BeEmpty()` em `AbacatePayCheckoutE2ETests`) e adiciona capacidade programavel sem custo de manutencao. `Reset()` disponivel para casos futuros. |
| **Q4** | Como capturar e validar o HMAC interno? | **Helper puro `InternalWebhookHmac.Compute/Matches`** em `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs` | Evita duplicar `sha256_hex_lowercase(secret, "{ts}.{body}")` em cada teste; centraliza a regra de negocio "tamper em qualquer lado invalida" e a constante `64 chars / hex lowercase`. |
| **Q5** | Quais headers `X-PaymentHub-*` capturar? | **TODOS os 4** (`event-id`, `event-type`, `timestamp`, `signature`) alem do body raw | O `HttpApplicationWebhookDispatcher` envia 4 headers (Slice 7-A). Capturar todos permite asserir o contrato completo; o teste P1.2 valida-os explicitamente. |
| **Q6** | Como reproduzir `UnprotectFailure` sem chave divergente entre API e Worker? | **Inserir blob base64 lixo** em `protectedWebhookSecret` quando seedar o `ApplicationClient` | `AesWebhookSecretProtector.Unprotect("not-a-valid-base64-aes-blob-xyzzy")` lanca `InvalidOperationException`; o dispatcher converte para `WebhookDispatcherException(UnprotectFailure)`. Cobre a regra de seguranca "abortar cedo" sem depender de mutar `PaymentHub:WebhookSecretEncryptionKey` (o que afetaria outros testes em paralelo). |
| **Q7** | Cobrir todo o fluxo AbacatePay ate o delivery interno? | **SIM — P2.1 (`AbacatePayWebhookFlow_ShouldCreateOutbox_AndDispatchInternalWebhook`)** reusa `CreateCheckoutAsync` da Slice 3-IT, dispara `IProcessWebhookEventHandler.ProcessAsync(webhookId, ct)` e so depois roda o dispatcher | Prova que nao ha gap entre Inbox e Outbox em producao: checkout cria `payment.checkout.created`, webhook externo processado cria `payment.approved`, dispatcher entrega os 2 ao consumer com HMAC valido. |

## Cobertura E2E

Total: **7 testes** (5 P1 + 2 P2).

| ID | Cenario | Path testado | Resultado esperado |
|----|---------|--------------|---------------------|
| **P1.1** | Happy path | `payment.checkout.created` | `Status = Sent`, `LastError = null`, `SentAt` populado, `RetryCount = 0`, 1 captura no fake receiver com method `POST`, URL `https://webhook.fake.test/hook` |
| **P1.2** | HMAC do webhook interno | qualquer evento com secret | `X-PaymentHub-Signature = sha256_hex_lowercase(secret, "{ts}.{body}")`; tamper em body OU timestamp invalida assinatura; length 64 chars hex lowercase |
| **P1.3** | 5xx do consumer | `payment.failed` | `Status = Pending`, `RetryCount = 1`, `NextRetryAt` futuro, `LastError = "HttpFailure: status=500"`, ZERO leak de URL/secret/body/reason |
| **P1.4** | 429 do consumer | `payment.checkout.created` | `Status = Pending`, `RetryCount = 1`, `LastError = "HttpFailure: status=429"` (formato canonico) |
| **P1.5** | `UnprotectFailure` | secret blob invalido | `Status = Pending`, `RetryCount = 1`, `LastError = "UnprotectFailure"` (length <= 64), `CallCount == 0` no fake receiver |
| **P2.1** | Fluxo completo AbacatePay ate delivery interno | checkout + webhook externo + processor + dispatcher | ambos Outbox `Sent`, `CallCount = 2`, HMAC valido contra `ApplicationClient.WebhookSecret`, payload outbound NAO contem provider secret |
| **P2.2** | Sent nao e redespachado | iteracao 1 + iteracao 2 | `CallCount = 1` na segunda iteracao (prova que `GetPendingForDispatchAsync` filtra Sent) |

## Arquivos criados

| Arquivo | Linhas | Proposito |
|---------|--------|-----------|
| `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` | 713 | 7 testes P1+P2 + helpers `CreateFreshFactoryAsync`, `SeedApplicationWithWebhookAsync`, `SeedWebhookUrlAndSecretForApplicationAsync`, `CreateCheckoutAsync`, `EnqueueOutboxAsync`, `RunDispatcherOnceAsync`, `BuildAbacatePayCompletedEnvelope`, `ComputeAbacatePayHmac`, `EnvelopeId`. |
| `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md` | (este) | Audit report. |

## Arquivos modificados

| Arquivo | Mudanca | Justificativa |
|---------|---------|---------------|
| `src/PaymentHub.Worker/PaymentHub.Worker.csproj` | +3 (linha de `InternalsVisibleTo` + comentario) | Permitir que `PaymentHub.IntegrationTests` chame `OutboxDispatcherWorker.DispatchOnceAsync` (metodo `internal`). Ja era `InternalsVisibleTo("PaymentHub.UnitTests")`; estender para o projeto de E2E e mudanca minima de uma linha. |
| `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | +6 (ProjectReference a Worker) + version bump 10.0.0 → 10.0.9 em 3 packages | `Microsoft.Extensions.Hosting 10.0.9` (transitivo via Worker SDK) exigiu bump de `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging` e `Microsoft.Extensions.Options` para evitar `NU1605` package downgrade. |
| `tests/PaymentHub.IntegrationTests/Infrastructure/PaymentHubApiFactory.cs` | +20 (metodo `ProtectWebhookSecret(string)`) | E2E precisa proteger `WebhookSecret` com o `IWebhookSecretProtector` real da API para semear a `ApplicationClient`; re-uso da mesma chave deterministica que `IntegrationTestFactory` ja usava. |
| `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs` | refactor (CapturedRequest +4 campos, EnqueueResponse, Reset, InternalWebhookHmac static class) | Evolucao backward-compatible: `CapturedRequest` agora expoe `EventIdHeader` e `EventTypeHeader` alem dos 3 campos pre-existentes; default 204 preservado; helper puro para HMAC evita duplicar logica em cada teste. |
| `docs/harness/validation.md` | +40 (bloco `Slice-specific (Phase 7 / Slice 7-IT)`) | 11 regras MUST-NOT-REGRESS (InternalsVisibleTo, header capture, helper HMAC puro, `LastError` canonical, `UnprotectFailure` no-HTTP, no-redispatch, `payload` continua `jsonb`, filtros de teste obrigatorios, sem migration). |
| `docs/specs/007-inbox-outbox-workers.md` | +50 (secao `End-to-end integration tests (Slice 7-IT)` + gaps + arquivos relacionados) | Cobertura E2E documentada explicitamente; tabela de 7 cenarios; arquivo `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` adicionado. |
| `docs/specs/011-security-and-compliance.md` | +30 (sub-secao `Slice 7-IT — End-to-end dispatcher (2026-06-30)`) | 6 invariants E2E de seguranca (HMAC, headers, `Sent`, retry safety, `UnprotectFailure`, no-redispatch, full AbacatePay path) adicionados a lista de testes esperados. |
| `docs/harness/learnings.md` | +30 (entrada `2026-06-30 - Slice 7-IT ...`) | 5 recomendacoes reaproveitaveis (default 204 + EnqueueResponse, `InternalWebhookHmac` puro, `OutboxEvent.payload` permanece `jsonb`, nao hospedar workers no WAF, `InternalsVisibleTo` para integration tests). |
| `feature_list.md` | +1 (PH-OUTBOX-E2E → Concluido) | Encerramento formal de P2-2 (parcial) + M1-integration. |
| `docs/roadmap/001-development-timeline.md` | +1 (Slice 7-IT `[CONCLUIDO 2026-06-30]` na lista recomendada + Phase 7 note) | Timeline atualizada. |
| `docs/roadmap/002-phase-status-board.md` | +5 (Phase 7 status note, P2-2 RESOLVIDO, Bloco B Slice 7-IT CONCLUIDO, indicadores de saude 491/24) | Phase 7 continua `IMPLEMENTING` (multi-instancia + sweep `Processing` orfao continuam fora de escopo). |
| `agent-progress.md` | +30 (entrada Slice 7-IT `CONCLUIDO`) | Status IMPLEMENTING → CONCLUIDO 2026-06-30; proximo slice recomendado: 2-C.1 (cliente HTTP real para `IProviderWebhookManagementClient`). |

Total: **2 arquivos novos + 11 arquivos modificados**.

## Validacao

```text
dotnet build PaymentHub.slnx              → 0 errors / 0 warnings em 9 projetos
dotnet test PaymentHub.slnx               → 491 passed (467 unit + 24 integration), 0 warnings
dotnet test --filter ~Outbox              → passa (sem regressao; cobre Slice 1-IT e 7-IT)
dotnet test --filter ~OutboxDispatcher    → 7 passed (novo)
dotnet test --filter ~EndToEnd            → 11 passed (4 Slice 3-IT + 7 Slice 7-IT)
dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj
  → 24 passed (17 baseline + 7 Slice 7-IT), ~14s
scripts/agent-architecture-check.sh        → Architecture check passed
scripts/agent-docs-check.sh               → Docs check passed
git diff --check                           → limpo
```

## Anti-Regression Rules

Nenhum bug de producao novo foi descoberto (diferente da Slice 3-IT, que
encontrou 2 anti-regression rules: `webhook_events.raw_payload` `jsonb → text`
e `_payments.AddAttemptAsync` explicito). As 11 regras MUST-NOT-REGRESS
documentadas em `docs/harness/validation.md` sao **preventivas**, baseadas nas
decisoes Q1-Q7 acima:

1. NAO hospedar `OutboxDispatcherWorker`/`WebhookProcessorWorker` dentro do
   `WebApplicationFactory` (Q1).
2. NAO remover `InternalsVisibleTo("PaymentHub.IntegrationTests")` de
   `PaymentHub.Worker.csproj` (Q1).
3. NAO trocar `CreateHost(IHostBuilder)` override em `PaymentHubApiFactory`
   por apenas `ConfigureWebHost(IWebHostBuilder)` (herdado da Slice 3-IT).
4. NAO alterar o default `204 NoContent` do `ApplicationWebhookCaptureHandler`
   (quebra Slice 3-IT `Captured.Should().BeEmpty()`).
5. NAO copiar a logica `sha256_hex_lowercase(secret, "{ts}.{body}")` em cada
   teste — sempre usar `InternalWebhookHmac.Compute/Matches` (Q4).
6. NAO persistir URL, segredo, blob protegido, signature ou body da response
   em `LastError` (Q3).
7. NAO trocar `OutboxEvent.payload` para `text` (esta coluna continua `jsonb`
   propositalmente — o conteudo e controlado pelo PaymentHub e indexado em
   queries internas; a regra `jsonb → text` da Slice 2-C vale apenas para
   colunas que armazenam corpo bruto de webhook de provider).
8. NAO chamar `DispatchAsync` do dispatcher sem o `Unprotect` passar
   (Invariant: `UnprotectFailure` aborta ANTES de qualquer HTTP POST).
9. NAO reenviar eventos `Sent`/`Processing`/`Failed` no worker — a query
   `GetPendingForDispatchAsync` filtra `Pending` com `NextRetryAt` vencido
   ou nulo (P2.2 cobre isso).
10. NAO exigir `tenantId`/`applicationId` em DTOs de request quando o endpoint
    for autenticado (herdado da Slice 6-B).
11. NAO criar migration para esta slice (storage ja existe; mudancas de
    storage ficam para o Phase 7 multi-instancia: `FOR UPDATE SKIP LOCKED`,
    sweep de `Processing` orfao).

## Riscos residuais / fora-de-escopo

- **Multi-instancia** (`FOR UPDATE SKIP LOCKED`, sweep de `Processing`
  orfao, dispatch idempotente em multiplos Workers): **NAO** enderecado
  nesta slice. A Slice 7-IT continua single-instance (mesmo comportamento
  que a Slice 3-IT). Deferido para `Slice 7-M1` (Phase 7 multi-instancia),
  ja documentado em `docs/roadmap/002-phase-status-board.md` (gaps M1-security
  e C.3-qa).
- **Backpressure**: o worker continua processando ate `OutboxWorkerBatchSize`
  (default 50) por tick; rate limit aplicado pelo consumer via 429 (P1.4).
- **Migracoes**: zero novas. Storage ja cobre tudo.
- **Outros providers**: dispatcher e tenant-agnostico; quando novos providers
  externos chegarem (Stripe/MercadoPago em Phase 4), replicar pattern
  Slice 2-B no lado Inbox; dispatcher ja cobre o outbound.
- **Outbox de Outbox**: se um dispatch falhar 5 vezes seguidas, vai para
  `Failed` (sem nova tentativa). Operacao manual futura; fora do escopo do MVP.

## Proximo slice recomendado

- **Slice 2-C.1** — Cliente HTTP real para `IProviderWebhookManagementClient`
  (call `POST /v2/webhooks/create` na AbacatePay). Ja planejado em
  `agent-progress.md` como `PLANEJADO PARA PROXIMA SESSAO` desde 2026-06-30.
  Substitui o `NoOpProviderWebhookManagementClient` (registra
  `RemoteRegistrationDeferred`) por um client real com `HttpClient` nomeado,
  Bearer Token via `ICredentialProtector.Unprotect`, e
  `AbacatePayErrorCategory`-based envelope error handling.
- Alternativamente, **Slice 7-M1** (Phase 7 multi-instancia) fecha os gaps
  M1-security + C.3-qa e promove Phase 7 de `IMPLEMENTING` para `IMPLEMENTED`.
- O Slice 6 (seguranca) ainda tem o gap P2-3 (AuditLog em handlers
  administrativos), que tambem pode ser enderecado agora.

## Arquivos relacionados

- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `src/PaymentHub.Worker/PaymentHub.Worker.csproj` (InternalsVisibleTo)
- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` (novo)
- `tests/PaymentHub.IntegrationTests/Infrastructure/PaymentHubApiFactory.cs`
- `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs`
- `tests/PaymentHub.IntegrationTests/Support/E2ESeedHelpers.cs`
- `docs/specs/007-inbox-outbox-workers.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/harness/validation.md`
- `docs/harness/learnings.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `feature_list.md`
- `agent-progress.md`