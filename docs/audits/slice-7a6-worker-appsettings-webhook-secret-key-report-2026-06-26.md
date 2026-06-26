# 7-A.6 — Relatório

Data: 2026-06-26
Phase: 7 — Workers, Outbox e processamento assíncrono (sub-slice de hardening de configuração)
Specs relacionadas: `docs/specs/011-security-and-compliance.md`
Slice predecessor: 7-A.1, 7-A.2, 7-A.3, 7-A.4, 7-A.5, 7-A.7, 7-A.8
Slice sucessor (recomendado, não implementado): 7-A.9

## Resumo

O `src/PaymentHub.Worker/appsettings.json` (production) não continha a seção `PaymentHub` nem a chave `WebhookSecretEncryptionKey`. Sem o placeholder, o operador não tinha o nome canônico da chave a ser fornecido por canal externo (variável de ambiente, secret manager, Docker secret), e a ausência da chave em produção só seria detectada no primeiro dispatch — quando o `OutboxDispatcherWorker` tentasse decifrar o segredo via `IWebhookSecretProtector.Unprotect` e falhasse com `InvalidOperationException`.

Este sub-slice adiciona o placeholder explícito vazio em `appsettings.json` e complementa a `docs/specs/011-security-and-compliance.md` com uma subseção dedicada à configuração da chave por ambiente (production/dev/variável de ambiente). O `appsettings.Development.json` já estava corretamente configurado com valor fake de 39 caracteres. O fail-fast no `Worker/Program.cs` (slice 7-A.3) permanece intacto.

Suite: 281 testes passando (sem regressão).

## Arquivos alterados

| Arquivo | Tipo de mudança | Linhas |
|---|---|---|
| `src/PaymentHub.Worker/appsettings.json` | Adicionada seção `PaymentHub` com placeholder vazio | +4 |
| `docs/specs/011-security-and-compliance.md` | Adicionada subseção `#### Configuracao da chave por ambiente (Worker e API)` | +27 |
| `agent-progress.md` | Entrada em `## Historico` + status do Slice 7-A pai | +30 |

**Não alterados (constraint de escopo):**
- `src/PaymentHub.Worker/appsettings.Development.json` — já tinha valor dev compatível (39 chars).
- `src/PaymentHub.Api/appsettings.json` — mesmo gap existe, mas o briefing deste sub-slice limita escopo ao Worker.
- `src/PaymentHub.Worker/Program.cs` — fail-fast intacto.
- `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs` — protector intocado.
- `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs` — opções intocadas.
- Qualquer teste novo — configuração não altera código de produção nem de teste.
- ADR / roadmap / feature_list / learnings — slice 7-A.9, não implementado.

## Configuração adicionada

### `src/PaymentHub.Worker/appsettings.json` (production)

Adicionada seção entre `Serilog` e `Bootstrap`:

```json
{
  "PaymentHub": {
    "WebhookSecretEncryptionKey": ""
  }
}
```

O placeholder vazio documenta o nome canônico da chave (`PaymentHub:WebhookSecretEncryptionKey`) sem expor valor real. JSON não suporta comentários; a obrigatoriedade em produção é documentada na spec 011.

### `src/PaymentHub.Worker/appsettings.Development.json` (dev/test)

Sem alteração. Conteúdo já presente:

```json
{
  "PaymentHub": {
    "WebhookSecretEncryptionKey": "dev-webhook-secret-key-change-me-32bytes"
  }
}
```

O valor `dev-webhook-secret-key-change-me-32bytes` (39 caracteres) é compatível com o protector:
- `AesWebhookSecretProtector` aceita qualquer string não-whitespace.
- Se o valor tiver < 32 caracteres, o protector preenche com `0` (PadRight).
- Não é valor real; é marcador fake explicitamente nomeado como "dev" / "change-me".

## Produção vs Development

### Production (appsettings.json)

- Valor em `appsettings.json`: **vazio** (`""`).
- Valor real: fornecido por **variável de ambiente**, secret manager, Docker secret ou mecanismo equivalente.
- Convenção .NET para override por env var: `PaymentHub__WebhookSecretEncryptionKey=<valor-real>`.
- O mesmo valor precisa estar disponível na **API** (que cifra em `RegisterApplicationClientHandler.HandleAsync`) e no **Worker** (que decifra em `HttpApplicationWebhookDispatcher.DispatchAsync`). Divergência provoca `InvalidOperationException("Protected webhook secret purpose mismatch.")` no primeiro dispatch e o Worker entra em loop de retry.
- **Nenhum valor real pode ser commitado**.

### Development (appsettings.Development.json)

- Valor fixo fake: `dev-webhook-secret-key-change-me-32bytes`.
- Permite que a API e o Worker subam localmente sem variável de ambiente.
- Permite que os testes passem sem mock da configuração.
- **Nunca usar em produção**.

### Fail-fast

- **Worker**: `src/PaymentHub.Worker/Program.cs:53-56` resolve `IWebhookSecretProtector` em um scope anônimo antes de `host.Run()`. Se a chave estiver ausente, a exceção é capturada pelo `try/catch` externo (linhas 60-63) e logada como fatal.
- **API**: o protector é registrado como Singleton em `PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions`. A primeira resolução (na construção do request pipeline ou no seedor de dev) falha com `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")` se a chave estiver ausente.

## Validações executadas

| Comando | Resultado |
|---|---|
| `git status --short` | 3 arquivos modificados (appsettings.json, spec 011, agent-progress.md) |
| `dotnet restore PaymentHub.slnx` | 9 projetos, 0 errors, 0 warnings |
| `dotnet build PaymentHub.slnx` | 9 projetos, 0 errors, 0 warnings |
| `dotnet test PaymentHub.slnx` | **281 passed**, 0 failed, 0 skipped |
| `--filter "FullyQualifiedName~WebhookSecret"` | **26 passed** |
| `--filter "FullyQualifiedName~ApplicationWebhook"` | **13 passed** (sem regressão) |
| `--filter "FullyQualifiedName~OutboxDispatcherWorker"` | **17 passed** (sem regressão) |
| `scripts/agent-architecture-check.sh` | **passed** |
| `scripts/agent-verify.sh` | **passed** (docs + architecture + state + secrets + docker) |
| `git diff --check` | passed (sem warnings) |

## Resultado das validações

Todos os critérios de aceite atendidos:

1. ✅ `src/PaymentHub.Worker/appsettings.json` contém placeholder explícito para a chave real.
2. ✅ `src/PaymentHub.Worker/appsettings.Development.json` contém valor fake/dev compatível (39 chars, ≥ 32).
3. ✅ Nenhum segredo real foi commitado — apenas placeholder `""` em production.
4. ✅ Documentação explica como configurar produção (variável de ambiente `PaymentHub__WebhookSecretEncryptionKey`, mesmo valor na API e Worker).
5. ✅ Fail-fast do Worker permanece intacto em `Program.cs:53-56`.
6. ✅ Build passou (0 errors / 0 warnings em 9 projetos).
7. ✅ Testes passaram (281 total, sem regressão).
8. ✅ `agent-architecture-check.sh` passou (Worker continua sem depender de Api).
9. ✅ `agent-verify.sh` passou (validação completa de harness).
10. ✅ `git diff --check` passou.
11. ✅ Não houve avanço para 7-A.9 ou outro sub-slice.

## Riscos ou gaps remanescentes

### R1 — API `appsettings.json` ainda sem placeholder (deferido)

Mesmo gap existe em `src/PaymentHub.Api/appsettings.json` (linhas 1-28). Não tratado neste sub-slice por constraint de escopo do briefing. **Recomendação**: aplicar o mesmo placeholder em slice próprio ou como parte de 7-A.9.

### R2 — Chave compartilhada entre API e Worker (deferido)

A mesma chave precisa estar disponível na API (que cifra) e no Worker (que decifra). Divergência provoca falha de `Unprotect`. Esta é uma constraint operacional documentada na spec 011 (`#### Configuracao da chave por ambiente (Worker e API)`). A gestão de rotação e sincronização está fora do escopo do MVP.

### R3 — Tamanho mínimo não enforçado em produção

O protector faz `PadRight(32, '0')` quando o valor é menor que 32 bytes. Isso evita crash em dev com valor curto, mas não obriga alta entropia em produção. **Recomendação**: adicionar validação explícita de tamanho mínimo (ex.: 32 bytes) no protector quando o ambiente não for Development, em slice futuro (não crítico para o MVP).

### R4 — Cobertura de testes de configuração continua zero (P2-2)

Não há testes que validem o comportamento de `AesWebhookSecretProtector` quando a chave está ausente em produção (apenas o fail-fast manual em `Worker/Program.cs` garante). **Recomendação**: adicionar teste de integração que simule ausência da chave e valide o `InvalidOperationException`. Fora do escopo deste sub-slice.

## Próximo sub-slice recomendado

**7-A.9** — Documentação final do Slice 7-A: criar `docs/adr/ADR-0007-webhook-secret-protection.md` (status `ACCEPTED`, conteúdo do report 6-C) e `docs/adr/ADR-0010-real-outbox-dispatcher-location.md` (status `ACCEPTED`, decisions deste slice: localização do dispatcher em `Infrastructure.Postgres.Webhooks/`, lifetime Scoped, `AddHttpClient` co-localizado, `IOutboxEventStore` para testabilidade, `IClock` no Worker, tenant guard, `LastError` sem body truncado, chave `WebhookSecretEncryptionKey` compartilhada API/Worker com blast radius documentado, headers deferred, sweep `Processing` órfão como gap conhecido). Atualizar `docs/adr/000-adr-index.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md` (se necessário), `docs/roadmap/002-phase-status-board.md` (P1-4 resolvido), `docs/roadmap/001-development-timeline.md`, `feature_list.md` (`PH-WORKER-001` → Concluído), `docs/harness/learnings.md`. Criar `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`.
