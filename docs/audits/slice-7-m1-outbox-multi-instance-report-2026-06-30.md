# Slice 7-M1 — Outbox Multi-Instancia (FOR UPDATE SKIP LOCKED + sweep de Processing orfao) Report

Data: 2026-06-30
Phase: 7 — Workers, Outbox e processamento assincrono
Specs relacionadas: `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/010-database-contract.md`, `docs/specs/011-security-and-compliance.md`
ADRs referenciadas: `ADR-0010-real-outbox-dispatcher-location.md`
Gaps enderecados: **M1-security** (sweep automatico de eventos `Processing` orfaos) + **C.3-qa** (`FOR UPDATE SKIP LOCKED` em `OutboxRepository` para multi-instancia).

## Resumo

Ate a Slice 7-M1 (2026-06-30), o `OutboxDispatcherWorker` rodava em single-instance com duas restricoes que bloqueavam o caminho para multi-instancia:

1. **Race window no claim.** `OutboxRepository.GetPendingForDispatchAsync` retornava rows `Pending` sem lock transacional, e o Worker chamava `outbox.MarkProcessing()` + `eventStore.SaveAsync(outbox, ct)` em iteracao separada. Dois workers concorrentes poderiam pegar a mesma row → double-dispatch → entrega duplicada do mesmo `payment.approved` no consumidor.
2. **Recovery orfao ineficiente.** Se o Worker crashasse entre `MarkProcessing` e `MarkSent`/`MarkRetry*`, a row ficava presa em `Processing` para sempre. Sem sweep automatico, a unica recuperacao era SQL manual.

A Slice 7-M1 fecha ambos os gaps com um design em 4 camadas:

- **Claim transacional com `FOR UPDATE SKIP LOCKED`** em uma unica transacao `ReadCommitted` no `NpgsqlConnection` raw (EF Core 10 nao traduz `SKIP LOCKED` em LINQ). O `SELECT` e o `UPDATE` rodam atomicamente; outra instancia recebe `SKIP LOCKED` para rows ja bloqueadas.
- **`OutboxEvent.ProcessingStartedAt` (`timestamptz NULL`)** registra o instante UTC do claim, permitindo o sweep distinguir rows vivas de rows orfas.
- **`SweepOrphanedProcessingAsync(DateTime cutoff, ct)`** como um unico `ExecuteSqlRawAsync` UPDATE atomic + idempotente. `last_error` recebe apenas o literal `'ProcessingOrphaned'` (nunca `ex.Message`, URL, blob ou stack).
- **Sanity-check no Worker:** se o claim devolver uma row em estado invalido (`Status != Processing` ou `ProcessingStartedAt == null`), o Worker pula o dispatch com `LogError` — anti-regressao contra remocao futura acidental do UPDATE do claim.

Phase 7 alcancou `IMPLEMENTED` apos esta slice. **Suite E2E = 498 testes** (467 unit + 31 integration), com +7 testes novos distribuidos entre `OutboxDispatcherConcurrencyTests` (2: P1.1 + P1.2) e `OutboxProcessingSweepTests` (5: P1.3-P1.5 + P2.1-P2.2).

## Decisoes (Q1-Q9)

### Q1. Concorrrencia multi-instancia: claim transacional ou mechanism externo?

**Decisao:** Claim transacional no proprio `OutboxRepository.ClaimPendingForDispatchAsync(int batchSize, DateTime now, CancellationToken)` usando `FOR UPDATE SKIP LOCKED` + atomic UPDATE.

**Justificativa:**
- Reduz superficie de complexidade (zero mensagens externas, zero broker).
- Compatibilidade com a decisao ADR-0010 de manter broker externo fora do MVP.
- Postgres 16 (target atual) ja' provê `FOR UPDATE SKIP LOCKED` estavel desde 9.5.
- Ja' roda contra o Testcontainer Postgres compartilhado da Slice 1-IT (validado nos testes P1.1/P1.2).

**Trade-off:** Worker nao pode exceder `OutboxWorkerBatchSize` por iteracao. Resolvido por multiplas iteracoes (default 1s tick via `OutboxWorkerIntervalSeconds`).

### Q2. Onde colocar o `SELECT ... FOR UPDATE SKIP LOCKED`?

**Decisao:** Em uma transacao `BeginTransactionAsync(IsolationLevel.ReadCommitted)` no `NpgsqlConnection` raw, obtido via `_db.Database.GetDbConnection()`.

**Justificativa:**
- EF Core 10 nao traduz `SKIP LOCKED` em LINQ. Workaround: raw ADO.NET e' o unico caminho.
- Manter o DbContext compartilhado entre claim, sweep e reload (`AsNoTracking`) preserva `IUnitOfWork` sem duplicar conexao.

**Anti-pattern:** NAO usar `BeginTransaction` no DbContext direto — ele esconde o `lock` da query que voce montou via `FromSqlRaw`/`Database.ExecuteSqlRawAsync` no mesmo escopo. A transacao precisa ficar explicita no escopo do `NpgsqlConnection` que emite o `SELECT FOR UPDATE`.

**Implementacao:**
```csharp
var dbConnection = _db.Database.GetDbConnection();
var connectionWasClosed = dbConnection.State != System.Data.ConnectionState.Open;
if (connectionWasClosed) await dbConnection.OpenAsync(ct);

await using (var transaction = await dbConnection.BeginTransactionAsync(
    System.Data.IsolationLevel.ReadCommitted, cancellationToken))
{
    // SELECT id FROM outbox_events
    //   WHERE status='Pending' AND (next_retry_at IS NULL OR next_retry_at <= @now)
    //   ORDER BY created_at
    //   FOR UPDATE SKIP LOCKED
    //   LIMIT @batchSize;
    //
    // UPDATE outbox_events
    //   SET status='Processing', processing_started_at=@now, updated_at=@now
    //   WHERE id = ANY(@claimedIds);
    // (commit)
}
```

### Q3. O Worker NAO chama `MarkProcessing` separado?

**Decisao:** Sim. O claim ja entrega rows em estado `Processing`. O Worker NAO chama `outbox.MarkProcessing()` + `eventStore.SaveAsync(outbox, ct)` no loop. Esta foi a reversao do design pre-Slice-7-M1 que estava documentado em `agent-progress.md` linha 151 como "**race window**".

**Justificativa:**
- Reintroduzir o pattern `MarkProcessing + SaveAsync` separado re-abre a janela onde duas instancias podem fazer `SELECT` da mesma row antes do `UPDATE`.
- O `UPDATE` do claim path ja' flips a row para `Processing` em uma unica transacao. Worker so precisa de `MarkSent`/`MarkRetry*`/`MarkFailed*` ao fim do dispatch.

**Anti-regressao:** Worker tem sanity-check explicito:
```csharp
if (outbox.Status != OutboxEventStatus.Processing || outbox.ProcessingStartedAt is null)
{
    _logger.LogError("Outbox event {OutboxId} was returned by ClaimPendingForDispatchAsync in an invalid state...");
    continue;
}
```

### Q4. Quando o sweep roda?

**Decisao:** Antes do claim, em toda iteracao de `DispatchOnceAsync`.

**Justificativa:**
- Roda o sweep primeiro garante que rows orfas sao re-disparadas na mesma iteracao quando o cutoff permite (P1.3 cobre isso: sweep + claim na mesma iteracao entrega o evento).
- Nao roda em loop proprio (background service separado) — fica dentro da mesma transacao logica do Worker.
- Cutoff = `_clock.UtcNow.AddSeconds(-_options.OutboxProcessingTimeoutSeconds)` (default 900s).

**Trade-off:** Custo de uma query extra por iteracao. Aceitavel dado que o Worker ja' faz `ClaimPending` em loop.

### Q5. `last_error` do sweep persiste o que?

**Decisao:** Apenas o literal hardcoded `'ProcessingOrphaned'` (valor enum da nova categoria `WebhookDispatcherCategory.ProcessingOrphaned = 8`).

**Justificativa (anti-regression rule):**
- Mesma politica aplicada a `LastError` em outros caminhos de erro (ver `docs/specs/011-security-and-compliance.md` secao "LastError seguro"): nunca `ex.Message`, URL, blob, stack, ou qualquer dado sensivel.
- O sweep nunca tem acesso a exception original do crash — apenas ao row stale. Persistir o literal do enum torna a recovery deterministica (operador sabe "foi orfao" sem ambiguidade).

**Cobertura do teste P1.3:**
```csharp
reloaded.LastError.Should().NotBeNull();
factory.WebhookHandler.CallCount.Should().Be(1);
```

### Q6. `next_retry_at` do sweep recebe NULL ou @now?

**Decisao:** **`NULL`** (NAO `@now`).

**Justificativa:**
- A claim filtra `next_retry_at IS NULL OR next_retry_at <= @now`.
- Se gravarmos `@now`, o claim path vai computar um novo `now` (microsegundos depois), e `next_retry_at <= @now` falharia. A row ficaria em `Pending` ate o proximo tick do Worker.
- `NULL` e' imediatamente dispatchable; `P1.3` valida isso (sweep + claim na mesma iteracao entrega o evento).

**Licao:** para qualquer sweep/scheduler que preenche `next_run_at` ou equivalente, sempre usar `NULL` (ou valor no passado) para disparo imediato na mesma transacao.

### Q7. Migration nova e' obrigatoria?

**Decisao:** Sim. `20260630184619_AddOutboxProcessingStartedAtAndIndexes`:
- `AddColumn processing_started_at timestamptz NULL`
- `DropIndex IX_outbox_events_status_next_retry_at`
- `CreateIndex IX_outbox_events_status_next_retry_at_created_at` (claim cobre ORDER BY created_at)
- `CreateIndex IX_outbox_events_status_processing_started_at` partial `WHERE status='Processing'` (sweep)

**Justificativa:**
- Indice composto `(status, next_retry_at, created_at)` serve `claim` sem sort step.
- Indice parcial serve `sweep` e permanece pequeno em steady state (so' rows em `Processing` aparecem).
- `processing_started_at` e' nullable SEM default — rows pre-existentes voltam com NULL e o sweep ignora ate que o claim novo os processe.

### Q8. Anti-regression rules da migration

- `outbox_events.payload` permanece `jsonb` (decisao Slice 7-IT, NAO regressao). Conteudo controlado pelo PaymentHub, indexado em queries internas.
- `webhook_events.raw_payload` permanece `text` (decisao Slice 3-IT BLOCKER, NAO regressao).
- `provider_accounts.webhook_events` permanece `text` (decisao Slice 2-C BLOCKER, NAO regressao).
- `processing_started_at` e' `timestamptz NULL`, NAO `jsonb`. Nao confundir: timestamp e' numero, JSON e' documento.

### Q9. Suite E2E cresceu para 498 testes

**Incremento:**
- 2 testes em `OutboxDispatcherConcurrencyTests`: P1.1 (2 workers concorrentes, 1 evento, `CallCount = 1`); P1.2 (10 eventos, 3 workers, 5 iteracoes cada, `CallCount = 10`, todos `Sent`).
- 5 testes em `OutboxProcessingSweepTests`: P1.3 (requeue de Processing de 2h atras + claim entrega na mesma iteracao); P1.4 (Processing de 1s atras preservado); P1.5 (Sent/Failed nao reabertos); P2.1 (NextRetryAt futuro respeitado, due-now entregue); P2.2 (batch=3 respeitado com 10 eventos em 4 iteracoes).

**Suite previa:** 491 testes (apos Slice 7-IT). **Suite nova:** 498 (+7).

**Garantias:** Worker NAO continua hospedado em `WebApplicationFactory` (re-asserting Slice 7-IT). Cada worker usa seu proprio `IServiceScope` + `PaymentHubDbContext` (EF Core DbContext nao e' thread-safe; compartilhar mascara bugs reais).

## Riscos residuais / fora-de-escopo

- **Backpressure:** Worker continua processando ate `OutboxWorkerBatchSize` (default 50) por tick. Rate limit aplicado pelo consumer via HTTP 429 (coberto P1.2 do Slice 7-IT).
- **Outbox de Outbox:** se um dispatch falhar 5 vezes (`RetryPolicy.MaxAttempts`), a row vai para `Failed` (sem nova tentativa). Operacao manual futura.
- **Migracoes futuras:** se Phase 7 precisar de multi-tenancy no sweep (cutoff por tenant), uma migration nova seria necessaria. Fora do MVP.
- **Outros outbox-like (retry queue, scheduled job queue):** se algum dia o projeto introduzir fila similar, copiar o pattern documentado na secao "Impacto para proximos agentes".

## Anti-patterns proibidos (re-asserting)

1. **NAO trocar `FOR UPDATE SKIP LOCKED` por `FOR UPDATE` puro.** Sem `SKIP LOCKED`, workers concorrentes serializam em vez de pularem rows bloqueadas.
2. **NAO mover o sweep para o mesmo `BeginTransactionAsync` do claim.** Sao concerns separados (recovery vs claim) e devem rodar em transacoes independentes.
3. **NAO usar `ExecuteSqlInterpolated` no sweep.** Use `ExecuteSqlRawAsync` com parametros nomeados (`@now`, `@cutoff`) para evitar SQL injection e manter a query estavel para `EXPLAIN`.
4. **NAO reintroduzir `MarkProcessing` separado no Worker.** O claim ja entrega `Processing`; voltar atras re-introduz a race window que esta slice fecha.
5. **NAO persistir o motivo do crash (exception, URL, body, stack) em `last_error` no caminho do sweep.** Use exclusivamente o literal `'ProcessingOrphaned'` (enum value).
6. **NAO `Dispose` a `NpgsqlConnection` no `claim`/`sweep` path.** EF Core owns the connection. Fechar apenas via `connectionWasClosed` flag se ja' estava fechada antes.
7. **NAO chamar `_db.Database.GetDbConnection()` e' descartado via `await using`.** Use `using` sincrono ou `await using` apenas na transacao.

## Proximo slice recomendado

- **Slice 6-* - AuditLog em handlers administrativos (P2-3):** Phase 6 continua `IMPLEMENTING` ate P2-3 ser fechado. Tarefas tipicas: `RegisterProviderAccountHandler`, `RegisterApplicationClientHandler`, manipulacao de `Active`/`Suspend` de `Tenant` e `ApplicationClient`. Detalhes em `docs/roadmap/002-phase-status-board.md`.
- Alternativamente, **Slice 2-C.1 — Cliente HTTP real para `IProviderWebhookManagementClient`:** Substitui `NoOpProviderWebhookManagementClient` por client real com `HttpClient` nomeado, Bearer Token via `ICredentialProtector.Unprotect`, e `AbacatePayErrorCategory`-based envelope error handling. Sem dependencias de Phase 7.

## Impacto para proximos agentes

Esta slice introduz um pattern reutilizavel: **claim transacional + sweep automatico para qualquer outbox-like em Postgres**. Detalhes para reaproveitamento:

1. **Pattern de claim para outbox-like:** Em qualquer sistema que precise evitar double-processing entre multiplos workers (retry queue, scheduled job queue, fanout queue), copie o trio:
   - Interface `I<Domain>Repository` com `<Verb>` que retorna `IReadOnlyList<T>` (claim) + `Sweep<State>` (recovery).
   - Implementacao com raw `NpgsqlConnection` + `BeginTransactionAsync(ReadCommitted)` + `SELECT ... FOR UPDATE SKIP LOCKED LIMIT @batchSize` + atomic UPDATE.
   - Entidade domain com `ProcessingStartedAt` (timestamp nullable) + `MarkProcessing(DateTime now)` que preserva o invariant da coluna.

2. **Pattern de sweep automatico:** Em qualquer sistema que precise recuperar de crash/restart de worker, use:
   - `ExecuteSqlRawAsync` com parametros nomeados (NAO `ExecuteSqlInterpolated` — risco SQL injection).
   - Template SQL estavel para `EXPLAIN`.
   - `last_error`/`<recovery_marker>` recebe apenas literal hardcoded da categoria (`'ProcessingOrphaned'`, `'RetryOrphaned'`, etc.) — nunca `ex.Message`.

3. **Pattern de teste de concorrencia:** Em qualquer teste E2E que envolva `BackgroundService`:
   - `Task.WhenAll` com workers instanciados via `factory.Services.GetRequiredService<>` (NAO hospedagem paralela no host).
   - Cada worker usa seu proprio `IServiceScope` + `PaymentHubDbContext`. EF Core DbContext nao e' thread-safe; compartilhar mascara bugs reais.
   - Anti-flaky: `await Task.Delay(10)` entre os `*OnceAsync` se o teste for flaky em CI. Ainda exercita SKIP LOCKED (segundo worker ve a lock) sem a corrida rara de microsegundo.

4. **Ao adicionar nova coluna de timestamp:** usar `timestamptz NULL`, NAO `jsonb`. Nao confundir: timestamp e' numero, JSON e' documento. Auditoria de migrations novas deve rejeitar `HasColumnType("jsonb")` em colunas de timestamp.

5. **Sanity-check post-claim no Worker:** `if (outbox.Status != Processing || outbox.ProcessingStartedAt is null) continue;` com `LogError`. Protege contra remocao futura acidental do UPDATE do claim path.

6. **Anti-regression rules** ja' documentadas em outras slices continuam validas: `payload` jsonb, `raw_payload` text, `webhook_events` text, `OutboxDispatcherWorker` nao hospedado em WAF, `ApplicationWebhookCaptureHandler` default 204.

## Arquivos relacionados

- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (ProcessingStartedAt + RequeueOrphaned + MarkProcessing(DateTime))
- `src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs` (ProcessingOrphaned = 8)
- `src/PaymentHub.Application/Abstractions/Outbox/IOutboxPublisher.cs` (ClaimPendingForDispatchAsync + SweepOrphanedProcessingAsync)
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs` (claim + sweep)
- `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs` (mappings + indices)
- `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs` (OutboxProcessingTimeoutSeconds)
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260630184619_AddOutboxProcessingStartedAtAndIndexes.cs` (novo)
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` (DispatchOnceAsync usa Claim + Sweep + sanity-check)
- `tests/PaymentHub.UnitTests/Worker/OutboxDispatcherWorkerTests.cs` (migrado)
- `tests/PaymentHub.IntegrationTests/Persistence/OutboxPendingQueryTests.cs` (migrado)
- `tests/PaymentHub.IntegrationTests/Support/E2ESeedHelpers.cs` (SeedOutboxEventAsync + SeedProcessingOutboxEventAsync)
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherConcurrencyTests.cs` (novo, 2 testes)
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxProcessingSweepTests.cs` (novo, 5 testes)
- `docs/specs/007-inbox-outbox-workers.md` (secao Multi-instancia + categoria 8)
- `docs/specs/010-database-contract.md`
- `docs/specs/011-security-and-compliance.md` (secao Slice 7-M1)
- `docs/harness/validation.md` (Slice-specific Phase 7 / Slice 7-M1 block)
- `docs/harness/learnings.md` (entrada 2026-06-30)
- `feature_list.md` (PH-OUTBOX-MULTI-INSTANCE)
- `docs/roadmap/001-development-timeline.md` (Phase 7 → IMPLEMENTED)
- `docs/roadmap/002-phase-status-board.md` (Phase 7 → IMPLEMENTED + M1-security/C.3-qa → RESOLVIDO)
- `agent-progress.md` (Slice 7-M1 → Historico)
