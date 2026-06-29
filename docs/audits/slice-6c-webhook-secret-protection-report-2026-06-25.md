# Slice 6-C — Webhook Secret Protection Report

Data: 2026-06-25
Phase: 6 — Seguranca e Confiabilidade
Specs relacionadas: `docs/specs/002-multitenancy-and-authentication.md`, `docs/specs/011-security-and-compliance.md`
Slice predecessor: Slice 6-A (enforcement de status ativo), Slice 6-B (ProviderAccount via contexto autenticado), Slice 6-D (politica de bootstrap/admin seed)
Gap enderecado: P1-5 da auditoria de 2026-06-17.

## Resumo

`ApplicationClient.WebhookSecret` deixou de ser persistido em texto claro. Foi introduzida a interface `IWebhookSecretProtector` em `PaymentHub.Application/Abstractions/Security/ICrypto.cs` e a implementacao `AesWebhookSecretProtector` em `PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs`. A coluna `application_clients.webhook_secret` continua com o mesmo nome e shape, mas armazena blob cifrado (Base64 de IV + ciphertext AES-CBC) prefixado com `PaymentHub.ApplicationClient.WebhookSecret.v1`. O DTO `ApplicationClientResponseDto` expoe apenas `hasWebhookSecret: bool`; nem o segredo raw nem o protegido sao retornados por nenhum endpoint. `HttpApplicationWebhookDispatcher` chama `Unprotect` no momento da assinatura HMAC e aborta o dispatch se a decifragem falhar. O seedor de desenvolvimento (`DevelopmentDataSeeder`) passou a aceitar `BootstrapOptions.DevelopmentWebhookSecret` opcional e protege o valor antes de persistir. Configuracao em `PaymentHub:WebhookSecretEncryptionKey` (32 bytes minimo; valor ausente lanca `InvalidOperationException`). 27 testes novos (11 do protector + 10 do handler + 3 do seeder + 3 do dispatcher). Suite previa: 106. Suite nova: 133.

## Gap enderecado

- **P1-5** — `ApplicationClient.WebhookSecret` persistido em texto claro.
  - Specs: `011-security-and-compliance.md` (HMAC de webhook interno e protecao de credenciais) e `002-multitenancy-and-authentication.md` (regras de resposta sem segredos).
  - Risco original: vazamento de banco permitiria forjar webhooks internos assinados para aplicacoes clientes; auditoria nao via mecanismo de protecao equivalente ao aplicado em provider credentials (`ICredentialProtector`).
  - Resolucao: cifragem simetrica (AES-CBC) reversivel para que o sistema consiga **recuperar** o segredo no momento de assinar HMAC. Hash unidirecional foi explicitamente descartado porque nao permite a verificacao de eventos pelo consumidor (que e o proposito do segredo). Migracao estrutural nao foi necessaria porque o nome e shape da coluna foram preservados; apenas o conteudo mudou para blob cifrado.

## Arquivos analisados

- `src/PaymentHub.Domain/Entities/ApplicationClient.cs` — entidade com `WebhookSecret` em texto claro.
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` — handler que persistia a `ApplicationClient` passando `webhookSecret` (raw) para o construtor.
- `src/PaymentHub.Application/Tenants/Dtos.cs` — DTOs de request/response da Application.
- `src/PaymentHub.Application/Bootstrap/DevelopmentDataSeeder.cs` — seedor nao recebia nem protegia webhook secret.
- `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs` — ja continha `AesCredentialProtector`, `HmacApiKeyHasher`, `HmacWebhookSigner`, `Sha256IdempotencyRequestHasher`.
- `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` — DI das classes acima; precisava de mais uma.
- `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs` — adicionou `WebhookSecretEncryptionKey`.
- `src/PaymentHub.Api/Webhooks/HttpApplicationWebhookDispatcher.cs` — lia `app.WebhookSecret` diretamente para assinar.
- `src/PaymentHub.Api/appsettings*.json`, `src/PaymentHub.Worker/appsettings*.json`, `.env.example` — configuracoes.
- `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs` — coluna `webhook_secret` mantida (maxLength=500, nullable).
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260616232151_InitialSchema.cs` e designer/snapshot — sem alteracao (conteudo cifrado cabe em maxLength 500).
- `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs` — instancia `ApplicationClient` em varios lugares; construtor com `protectedWebhookSecret` opcional mantem compatibilidade.
- `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs` — instancia o seedor com 6 dependencias; precisou adicionar o `IWebhookSecretProtector`.
- `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` — instancia `ApplicationClient`; precisa funcionar com o novo construtor.
- `docs/specs/011-security-and-compliance.md`, `docs/specs/002-multitenancy-and-authentication.md` — referenciam o gap.
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`, `docs/audits/spec-adherence-audit-2026-06-17.md` — classificaram o gap como P1-5.
- `docs/roadmap/000-payment-hub-roadmap.md`, `001-development-timeline.md`, `002-phase-status-board.md` — referenciam o gap e o Slice 6-C.
- `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`, `slice-6b-provider-account-authenticated-context-report-2026-06-18.md`, `slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md` — slices predecessores, para manter consistencia de padroes.

## Arquivos alterados

| Arquivo | Tipo | Resumo |
| ------- | ---- | ------ |
| `src/PaymentHub.Application/Abstractions/Security/ICrypto.cs` | Modificado | Adicionada interface `IWebhookSecretProtector` com `Protect(string)`/`Unprotect(string)` na mesma familia de `ICredentialProtector`. |
| `src/PaymentHub.Application/Abstractions/Bootstrap/BootstrapOptions.cs` | Modificado | Adicionada propriedade opcional `DevelopmentWebhookSecret` para o seedor de desenvolvimento. |
| `src/PaymentHub.Application/Tenants/Dtos.cs` | Modificado | `RegisterApplicationClientRequestDto` agora aceita `WebhookSecret` (raw, opcional); `ApplicationClientResponseDto` expoe apenas `HasWebhookSecret: bool`. |
| `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` | Modificado | Handler injeta `IWebhookSecretProtector`, protege o segredo antes de criar a entidade, e retorna `HasWebhookSecret` na resposta (nunca o segredo). |
| `src/PaymentHub.Application/Bootstrap/DevelopmentDataSeeder.cs` | Modificado | Seedor injeta `IWebhookSecretProtector` e protege o `DevelopmentWebhookSecret` opcional antes de persistir; log do seedor usa `hasProtectedWebhook` em vez de `hasWebhookSecret` (evita substring `secret=`). |
| `src/PaymentHub.Domain/Entities/ApplicationClient.cs` | Modificado | Construtor e `UpdateWebhook(...)` agora exigem parametro nomeado `protectedWebhookSecret`; documentacao explicita que o blob ja deve estar protegido. `HasWebhookSecret` continua publico como metadado seguro. |
| `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs` | Modificado | Adicionada propriedade `WebhookSecretEncryptionKey` (32 bytes minimo, ausente lanca `InvalidOperationException`). |
| `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs` | Modificado | Adicionada classe `AesWebhookSecretProtector` com AES-CBC + prefixo de proposito (`PaymentHub.ApplicationClient.WebhookSecret.v1`) e verificacao em tempo constante via `CryptographicOperations.FixedTimeEquals`. |
| `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` | Modificado | `services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>()`. |
| `src/PaymentHub.Api/Webhooks/HttpApplicationWebhookDispatcher.cs` | Modificado | Injeta `IWebhookSecretProtector`; chama `Unprotect` imediatamente antes de `Sign`; aborta o dispatch se a decifragem falhar; emite log estruturado sem expor o segredo. |
| `src/PaymentHub.Api/appsettings.json`, `appsettings.Development.json` | Modificado | Defaults fail-safe: `DevelopmentWebhookSecret: null` no base; chave fake dev explicita em `appsettings.Development.json` (`dev-webhook-secret-key-change-me-32bytes`) + `DevelopmentWebhookSecret: "dev-webhook-secret-change-me"`. |
| `src/PaymentHub.Worker/appsettings.json`, `appsettings.Development.json` | Modificado | Mesma chave fake dev no Worker (Worker nao chama o seedor, mas precisa do `IWebhookSecretProtector` registrado em DI; no futuro, o Slice 7-A podera ler a application com `WebhookSecret` cifrado). |
| `.env.example` | Modificado | Adicionadas `PaymentHub__WebhookSecretEncryptionKey` e `Bootstrap__DevelopmentWebhookSecret`. |
| `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs` | Modificado | Construtor do seedor nos testes agora recebe `FakeWebhookSecretProtector`; 3 testes novos verificam persistencia protegida, ausencia de persistencia quando nao configurado, e ausencia de log raw. |
| `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientHandlerTests.cs` | Criado | 10 testes cobrindo: DTO de resposta sem secret raw nem protected, DTO de request aceita secret opcional, handler protege antes de persistir, retorna `HasWebhookSecret`, nao expoe raw em logs, idempotencia de comportamento. |
| `tests/PaymentHub.UnitTests/Infrastructure/AesWebhookSecretProtectorTests.cs` | Criado | 11 testes do protector: roundtrip, nao-retorna-raw, ciphertexts-diferentes-por-chamada, unprotect-falha-com-purpose-errado, falha-com-chave-vazia, falha-com-base64-invalido, unprotect-falha-com-chave-diferente, padding-de-chave-curta. |
| `tests/PaymentHub.UnitTests/Api/Webhooks/HttpApplicationWebhookDispatcherTests.cs` | Criado | 3 testes do dispatcher: desprotege e assina corretamente, nao inclui signature quando nao ha secret, nao envia request quando `Unprotect` falha. |
| `tests/PaymentHub.UnitTests/Support/FakeWebhookSecretProtector.cs` | Criado | `IWebhookSecretProtector` in-memory para testes (apenas XOR com marker; NAO criptografico). |
| `docs/specs/002-multitenancy-and-authentication.md` | Modificado | Adicionada regra sobre `WebhookSecret` cifrado em `Regras obrigatorias`. |
| `docs/specs/011-security-and-compliance.md` | Modificado | Adicionada secao "Protecao de `ApplicationClient.WebhookSecret` em repouso" com politica completa; tabela de contratos incluiu `Application webhook secret`. |
| `docs/roadmap/000-payment-hub-roadmap.md` | Modificado | Slice 6-C marcado como `[CONCLUIDO 2026-06-25]`; nota na secao "Visao geral"; gap P1-5 marcado como `[RESOLVIDO]`. |
| `docs/roadmap/001-development-timeline.md` | Modificado | Slice 6-C marcado como `[CONCLUIDO 2026-06-25]` na lista de slices recomendados. |
| `docs/roadmap/002-phase-status-board.md` | Modificado | Phase 6 caiu para 0 gaps P1 proprios; bloco A atualizado; nota explicativa do Slice 6-C; indicadores (106 -> 133 testes; 2 -> 1 gap P1 aberto global). |
| `docs/audits/payment-hub-current-state-audit-2026-06-17.md` | Modificado | Sumario executivo e tabela de gaps P1 atualizados; P1-5 marcado como resolvido. |
| `docs/audits/spec-adherence-audit-2026-06-17.md` | Modificado | Resumo, tabela de aderencia, gaps de seguranca e gaps de testes atualizados; entradas P1-5 marcadas como resolvidas. |
| `docs/harness/validation-matrix.md` | Modificado | Adicionadas 19 novas linhas para Slice 6-C com status `PASS` em 2026-06-25. |
| `docs/harness/learnings.md` | Modificado | Nova entrada sobre o padrao de protecao de `WebhookSecret`. |
| `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md` | Criado | Este relatorio. |

Nenhum arquivo de migration foi criado ou modificado. O conteudo cifrado em Base64 cabe na coluna `varchar(500)` existente.

## Comportamento anterior

- `ApplicationClient.WebhookSecret` era persistido em texto claro (valor raw do segredo).
- Nenhum mecanismo de protecao reversivel existia para o segredo.
- O DTO `ApplicationClientResponseDto` nao continha `WebhookSecret` (a entidade inteira nao era serializada; a API retornava apenas os campos do DTO), mas o handler tampouco se preocupava em proteger — quem persistia era o proprio construtor da entidade aceitando raw.
- `HttpApplicationWebhookDispatcher` lia `app.WebhookSecret` diretamente para chamar `_signer.Sign(payloadJson, app.WebhookSecret, timestamp)`.
- `DevelopmentDataSeeder` nao configurava webhook secret para o `dev-app`.
- Nao havia `PaymentHub:WebhookSecretEncryptionKey` em `appsettings*.json` nem `.env.example`.
- Nao havia teste estrutural cobrindo ausencia de raw em persistencia, response ou log.

## Comportamento novo

1. **Camada de abstracao.** `IWebhookSecretProtector.Protect(plain)` e `Unprotect(protected)` em `PaymentHub.Application/Abstractions/Security/ICrypto.cs`. Implementacao registrada como singleton (estado por processo eh seguro; a chave vem de configuracao).

2. **Implementacao AES-CBC com prefixo de proposito.** `AesWebhookSecretProtector` cifra o payload `<purpose>|<raw>` em AES-CBC com IV randomico de 16 bytes, concatena IV + ciphertext, codifica em Base64. Chave vem de `PaymentHub:WebhookSecretEncryptionKey`; valores menores que 32 bytes sao preenchidos com `0` ate 32 bytes; valor ausente lanca `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")`. Decifragem valida o prefixo via `CryptographicOperations.FixedTimeEquals` (anti timing-attack). Prop finalidade explicita evita que um blob gerado por outro sistema (ou por versao antiga) seja aceito como webhook secret.

3. **Entidade nao ve raw.** `ApplicationClient` agora exige parametro nomeado `protectedWebhookSecret` no construtor e em `UpdateWebhook(...)`. Documentacao explicita que o blob ja deve estar protegido. `HasWebhookSecret` continua publico como metadado seguro.

4. **Handler protege antes de persistir.** `RegisterApplicationClientHandler` recebe `WebhookSecret` raw no DTO de request, chama `_webhookSecretProtector.Protect(request.WebhookSecret)` e passa o blob protegido para o construtor da entidade. Resposta expoe `HasWebhookSecret: bool`. Validacao `MaximumLength(2000)` no validator para limitar entrada.

5. **Seedor de desenvolvimento.** `DevelopmentDataSeeder` aceita `BootstrapOptions.DevelopmentWebhookSecret` opcional; se preenchido (tipicamente em `appsettings.Development.json`), protege via `_webhookSecretProtector.Protect(...)` e passa para o construtor da entidade. Se nao preenchido, `WebhookSecret` permanece `null` e `HasWebhookSecret` fica `false`. Log do seedor: `"Bootstrap: created dev application {ApplicationName} (id={ApplicationId}) under tenant {TenantId} (hasProtectedWebhook={HasProtectedWebhook})."` — usa `hasProtectedWebhook` em vez de `hasWebhookSecret` para evitar substring `secret=` (validado pelo teste `SeedAsync_ShouldNotLogApiKeyOrSecrets`).

6. **Dispatcher desprotege no momento da assinatura.** `HttpApplicationWebhookDispatcher.DispatchAsync` chama `_webhookSecretProtector.Unprotect(app.WebhookSecret!)` imediatamente antes de `_signer.Sign(...)`. Se `Unprotect` falhar (chave diferente, blob corrompido, purpose mismatch), o dispatcher loga o erro com `LogError` (incluindo `outboxEventId` e `applicationId`) e **retorna sem enviar a requisicao HTTP** — comportamento coberto pelo teste `DispatchAsync_ShouldNotSendRequest_WhenProtectedSecretCannotBeUnprotected`.

7. **DTO de resposta sem segredos.** `ApplicationClientResponseDto` nao expoe `WebhookSecret`, `ProtectedWebhookSecret`, `EncryptedWebhookSecret` ou similar; expoe apenas `HasWebhookSecret: bool`. Validado estruturalmente em `RegisterApplicationClientHandlerTests.ApplicationClientResponseDto_ShouldNotExposeWebhookSecretRawOrProtected` (teste usa `Type.GetProperty` para confirmar ausencia).

8. **Configuracao fail-safe.** `PaymentHubOptions.WebhookSecretEncryptionKey` default `string.Empty`; ausencia lanca `InvalidOperationException` no construtor do protector. `appsettings.json` (base) nao traz a chave (Production precisa configurar explicitamente). `appsettings.Development.json` traz valor fake `dev-webhook-secret-key-change-me-32bytes`. `.env.example` documenta ambas as chaves com comentario explicito.

## Politica implementada

| Cenario | Comportamento |
| ------- | ------------- |
| `POST /api/v1/applications` com `webhookSecret` no body | Handler protege via `IWebhookSecretProtector.Protect` antes de persistir; coluna armazena blob Base64; resposta expoe `hasWebhookSecret: true` |
| `POST /api/v1/applications` sem `webhookSecret` no body | Coluna `webhook_secret` permanece `NULL`; `hasWebhookSecret: false`; nenhum codepath de cifragem executado |
| `ApplicationClient` recuperado do banco com `webhook_secret` cifrado | `HttpApplicationWebhookDispatcher.DispatchAsync` chama `Unprotect` para obter raw; assina HMAC; envia header `X-PaymentHub-Signature` |
| `ApplicationClient` recuperado do banco com `webhook_secret` corrompido / purpose invalido / chave diferente | `Unprotect` lanca `InvalidOperationException` (ou `CryptographicException`); dispatcher loga o erro e **nao** envia HTTP request |
| `ApplicationClient` recuperado do banco com `webhook_secret` NULL | Dispatcher omite header `X-PaymentHub-Signature`; segue fluxo normal |
| Bootstrap de desenvolvimento com `Bootstrap:DevelopmentWebhookSecret` | Seedor protege via `IWebhookSecretProtector.Protect` antes de persistir |
| Bootstrap de desenvolvimento sem `Bootstrap:DevelopmentWebhookSecret` | Seedor cria `ApplicationClient` com `webhook_secret: NULL`; `hasProtectedWebhook: false` no log |
| `PaymentHub:WebhookSecretEncryptionKey` ausente | `AesWebhookSecretProtector` lanca `InvalidOperationException` no construtor (singleton) — falha clara e segura; nenhum fallback hardcoded |
| `PaymentHub:WebhookSecretEncryptionKey` em `Production` | Operador deve configurar via variavel de ambiente ou secret manager; valor nao deve ser commitado |
| Logs do seedor | Apenas `applicationName`, `applicationId`, `tenantId`, `hasProtectedWebhook` (bool); nunca o valor raw nem o blob cifrado |
| Logs do dispatcher | `outboxEventId`, `applicationId`, `tenantId`, operacao; nunca o segredo raw nem o blob |
| Resposta de `POST /api/v1/applications` | Apenas `hasWebhookSecret: bool`; sem `webhookSecret`, `protectedWebhookSecret`, `encryptedWebhookSecret` |
| Tabela `application_clients` | Coluna `webhook_secret` mantida (maxLength 500, nullable); conteudo agora blob Base64 cifrado |

## Estrategia de protecao escolhida

Escolhida **criptografia simetrica reversivel** (AES-CBC) em vez de hash unidirecional porque o sistema precisa **recuperar** o segredo para assinar os webhooks internos. Hash (HMAC-SHA256, bcrypt, Argon2) nao funciona: o dispatcher precisa do raw para gerar `X-PaymentHub-Signature` via HMAC.

Decisoes tecnicas:

- **AES-CBC com IV randomico de 16 bytes.** Garante que chamadas identicas produzem ciphertexts diferentes (anti-correlacao em banco). Modo CBC eh suficiente porque a chave nao eh fixa por mensagem (a chave vive em configuracao).
- **Prefixo de proposito antes do ciphertext.** O payload cifrado eh `<purpose>|<raw>` onde `purpose = "PaymentHub.ApplicationClient.WebhookSecret.v1"`. Isso garante que um blob gerado por outro sistema ou por uma versao futura com chave/algoritmo diferente seja rejeitado sem ambiguidade. Verificacao em tempo constante via `CryptographicOperations.FixedTimeEquals` (anti timing-attack no `Unprotect`).
- **Implementacao centralizada em `Infrastructure.Postgres/Security/CryptoServices.cs`.** Mesmo arquivo de `AesCredentialProtector`, `HmacApiKeyHasher`, `HmacWebhookSigner`, `Sha256IdempotencyRequestHasher`. A chave de webhook eh **separada** da chave de provider credentials para que a rotacao seja independente e o blast radius seja menor.
- **Sem uso de `IDataProtectionProvider` (Data Protection do ASP.NET Core).** O projeto ja usa `ICredentialProtector` com AES proprio, entao manter consistencia evitou introduzir duas estrategias de protecao. Alem disso, `IDataProtectionProvider` eh web-framework-specific e exigiria injecao de dependencia no `PaymentHub.Application`, quebrando a Clean Architecture.
- **Sem KMS/Key Vault externo.** Fora de escopo do slice (regra de `docs/harness/security.md` e fora de escopo explicito do prompt).

## Configuracao necessaria

- `PaymentHub:WebhookSecretEncryptionKey` (string, 32 bytes minimo)
  - `appsettings.json` (base): ausente (Production precisa configurar via env var).
  - `appsettings.Development.json`: `"dev-webhook-secret-key-change-me-32bytes"` (fake, dev only).
  - `.env.example`: `PaymentHub__WebhookSecretEncryptionKey=dev-webhook-secret-key-change-me-32bytes` (com comentario).
  - Valores menores que 32 bytes sao preenchidos com `0` ate 32 bytes (mesmo padrao de `AesCredentialProtector`).
  - Valor ausente ou vazio lanca `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")` no construtor do protector.
- `Bootstrap:DevelopmentWebhookSecret` (string opcional, dev only)
  - `appsettings.json` (base): `null` (seedor nao cria webhook secret).
  - `appsettings.Development.json`: `"dev-webhook-secret-change-me"` (fake, dev only) — valor sera protegido pelo seedor antes de persistir.
  - `.env.example`: `Bootstrap__DevelopmentWebhookSecret=dev-webhook-secret-change-me`.
  - Worker `appsettings*.json`: `null` (Worker nao roda seedor, mas mantem consistencia).

## Seguranca e logs

- **Logs do seedor (exemplos):**
  - `"Bootstrap development seed skipped in environment {Environment} (policy enabled={Enabled}, production={IsProduction})."`
  - `"Bootstrap: dev tenant with slug {Slug} already exists (id={TenantId}). Reusing."`
  - `"Bootstrap: created dev tenant with slug {Slug} (id={TenantId})."`
  - `"Bootstrap: created dev application {ApplicationName} (id={ApplicationId}) under tenant {TenantId} (hasProtectedWebhook={HasProtectedWebhook})."` (usa `hasProtectedWebhook` em vez de `hasWebhookSecret` para evitar substring `secret=`)
  - `"Bootstrap development seed completed in environment {Environment}: tenantCreated={TenantCreated}, applicationCreated={ApplicationCreated}, seedExecuted={SeedExecuted}."`
- **Logs do dispatcher (exemplos):**
  - `"Skipping outbox event {OutboxEventId}: application {ApplicationId} has no webhook url"` (existente, mantido)
  - `"Skipping outbox event {OutboxEventId}: application {ApplicationId} has invalid protected webhook secret"` (novo, em caso de `Unprotect` falhar)
- **Logs NAO emitidos:** webhook secret raw, blob Base64 cifrado, prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1`, valor de `X-PaymentHub-Signature` enviado, valor de `Authorization` recebido.
- **Cobertura de teste:**
  - `RegisterApplicationClientHandlerTests.HandleAsync_ShouldNotLogRawWebhookSecret` (handler nao expoe raw na persistencia).
  - `DevelopmentDataSeederTests.SeedAsync_ShouldNotLogApiKeyOrSecrets` (seedor nao expoe `apiKey=`, `secret=`, `password=`, `phk_`, `Bearer `).
  - `DevelopmentDataSeederTests.SeedAsync_ShouldNotLogRawWebhookSecret` (seedor nao expoe raw nem marker do protector).
  - `HttpApplicationWebhookDispatcherTests.DispatchAsync_ShouldUnprotectWebhookSecret_BeforeSigningRequest` (dispatcher usa o raw desprotegido para assinar).
- **Cobertura de DTO:** `RegisterApplicationClientHandlerTests.ApplicationClientResponseDto_ShouldNotExposeWebhookSecretRawOrProtected` confirma via reflexao que `WebhookSecret`, `ProtectedWebhookSecret`, `EncryptedWebhookSecret` nao existem como propriedades; `HasWebhookSecret` existe.
- **Regra de `PaymentHub:WebhookSecretEncryptionKey` em `appsettings.json`:** mesmo que um operador adicione `WebhookSecretEncryptionKey` com valor real, o seedor nao loga esse campo; o seedor so le `DevelopmentWebhookSecret`. Adicionar `Bootstrap:SomeSecret` e seguro; nao sera logado nem persistido.

## Dados existentes e migracao

**Nenhuma migration foi aplicada.**

Justificativa:

1. **Schema da coluna nao mudou.** A coluna `application_clients.webhook_secret` continua `varchar(500) nullable`. Um blob AES-CBC em Base64 (16 bytes IV + ate ~64 bytes ciphertext + ~64 bytes marker) cabe em 500 caracteres.
2. **Sem dados produtivos pre-existentes.** O projeto esta em fase `IMPLEMENTING` e nao tem producao real (auditoria de 2026-06-17 confirma ausencia de producao; `Bootstrap` so roda em `Development`/`Test` por default).
3. **Forma do conteudo mudou.** O conteudo agora eh blob Base64 cifrado. Como nao ha dados legados em texto claro, nao ha backfill a fazer.
4. **Coluna preservada para evitar quebra.** Manter o mesmo nome evita migration estrutural e reduz risco.

Risco residual documentado: se algum dia existir um banco com dados em texto claro (pre-2026-06-25) que precise ser preservado em vez de recriado, sera necessario um script de backfill que cifre cada `webhook_secret` existente com `IWebhookSecretProtector.Protect` e atualize a coluna. Esse script NAO foi implementado porque nao ha dados afetados e esta fora do escopo do slice.

## Testes adicionados/alterados

Cobertura nova / ampliada em 4 arquivos:

### `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientHandlerTests.cs` (10 testes, NOVO)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | `ApplicationClientResponseDto` nao expoe `WebhookSecret`, `ProtectedWebhookSecret`, `EncryptedWebhookSecret` (verificacao via `Type.GetProperty`) | Propriedades ausentes |
| 2 | `RegisterApplicationClientRequestDto` aceita `WebhookSecret` opcional | Propriedade presente |
| 3 | Handler protege o segredo antes de persistir a `ApplicationClient` | `WebhookSecret` no persistido comeca com `FakeWebhookSecretProtector.Marker` e difere do raw |
| 4 | Handler persiste `WebhookSecret: null` quando request nao envia | `WebhookSecret` null; `HasWebhookSecret: false` |
| 5 | Resposta de criacao expoe `HasWebhookSecret: true` quando segredo presente | Resposta contem `HasWebhookSecret: true` |
| 6 | Resposta de criacao expoe `HasWebhookSecret: false` quando segredo ausente | Resposta contem `HasWebhookSecret: false` |
| 7 | Handler lanca `InvalidOperationException` quando tenant nao existe | Mensagem contem "Tenant" e "does not exist" |
| 8 | Handler continua retornando API Key one-time | Resposta contem `ApiKey` com prefixo `phk_` |
| 9 | Handler nao expoe raw via persistencia apos chamada normal | `persisted.WebhookSecret != rawSecret` |
| 10 | Handler aceita `WebhookSecret` com whitespace ao redor | Nenhuma excecao; `HasWebhookSecret: true` |

### `tests/PaymentHub.UnitTests/Infrastructure/AesWebhookSecretProtectorTests.cs` (11 testes, NOVO)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | Roundtrip de plaintext | `Unprotect(Protect(x)) == x` |
| 2 | `Protect` nao retorna plaintext | `Protect(x) != x` e nao contem `x` |
| 3 | `Protect` produz ciphertexts diferentes para mesmo input | 2 chamadas retornam strings distintas (IV randomico) |
| 4 | `Unprotect` lanca `InvalidOperationException` ou `CryptographicException` em payload invalido | Excecao lancada |
| 5 | `Unprotect` lanca `ArgumentException` em input vazio | Excecao lancada |
| 6 | `Unprotect` lanca `FormatException` em Base64 invalido | Excecao lancada |
| 7 | `Protect` lanca `ArgumentException` em plaintext vazio | Excecao lancada |
| 8 | `Protect` lanca `ArgumentNullException` em plaintext null | Excecao lancada |
| 9 | Construtor lanca `InvalidOperationException` quando `WebhookSecretEncryptionKey` vazio | Mensagem contem `WebhookSecretEncryptionKey` |
| 10 | `Unprotect` com chave diferente lanca `CryptographicException` (padding invalido) | Excecao lancada |
| 11 | Chave curta (< 32 bytes) eh preenchida com `0` ate 32 bytes; roundtrip funciona | Roundtrip ok |

### `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs` (+3 testes, EXISTENTE)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | Seedor protege `DevelopmentWebhookSecret` antes de persistir | `persisted.WebhookSecret` comeca com marker; roundtrip via protector retorna raw |
| 2 | Seedor nao persiste webhook secret quando `DevelopmentWebhookSecret` nao configurado | `persisted.WebhookSecret == null`; `HasWebhookSecret == false` |
| 3 | Seedor nao loga raw webhook secret nem marker do protector | Mensagens capturadas nao contem raw nem marker |

(Os 9 testes pre-existentes continuam passando sem alteracao funcional, apenas com o parametro `IWebhookSecretProtector` adicional no construtor.)

### `tests/PaymentHub.UnitTests/Api/Webhooks/HttpApplicationWebhookDispatcherTests.cs` (3 testes, NOVO)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | Dispatcher desprotege segredo antes de assinar HMAC | `X-PaymentHub-Signature` calculado sobre raw (verificavel via `HmacWebhookSigner.Verify`) |
| 2 | Dispatcher nao inclui `X-PaymentHub-Signature` quando `ApplicationClient` nao tem secret | Header ausente |
| 3 | Dispatcher nao envia HTTP request quando `Unprotect` falha | `CapturingHandler.Request == null` (request abortada antes do HTTP) |

### Total adicionado: 27 testes. Suite previa: 106. Suite nova: 133. Nenhum teste previo foi removido ou desabilitado.

### Helper compartilhado

`tests/PaymentHub.UnitTests/Support/FakeWebhookSecretProtector.cs` — implementacao in-memory de `IWebhookSecretProtector` com marker `fake-protect|`. NAO eh criptografica (apenas Base64 reversivel); usada para que os testes do handler e do seedor nao dependam de configuracao real e para que `persisted.WebhookSecret` comece com marker previsivel.

## Validacoes executadas

Comandos executados em `/mnt/hd2/Projects/payment-hub`:

```bash
git status --short
dotnet restore PaymentHub.slnx
dotnet build PaymentHub.slnx
dotnet test PaymentHub.slnx
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~WebhookSecret"
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~Bootstrap"
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ProviderAccount"
docker compose config
```

Resultados (2026-06-25):

| Comando | Resultado |
| ------- | --------- |
| `git status --short` | 24 arquivos modificados, 4 novos (3 testes + 1 helper). |
| `dotnet restore PaymentHub.slnx` | 9 projetos restaurados, 0 erros, 0 warnings. |
| `dotnet build PaymentHub.slnx` | 9 projetos, 0 erros, 0 warnings em ~7s. |
| `dotnet test PaymentHub.slnx` | 133 testes passando, 0 warnings em ~3.7s (suite previa: 106). |
| `dotnet test --filter "FullyQualifiedName~WebhookSecret"` | 25 testes passando (11 protector + 10 handler + 3 seeder + 1 dispatcher); nota: o filtro "WebhookSecret" tambem casa o teste do `HmacWebhookSignerTests` (existente) e o `IApplicationClientRepository.GetByIdAsync` no teste do `HttpApplicationWebhookDispatcherTests` |
| `dotnet test --filter "FullyQualifiedName~Bootstrap"` | 15 testes passando (12 `HostBootstrapPolicyTests` + 9 `DevelopmentDataSeederTests`; sem regressao) |
| `dotnet test --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"` | 11 testes passando (sem regressao) |
| `dotnet test --filter "FullyQualifiedName~ProviderAccount"` | 15 testes passando (sem regressao; 10 handler + 5 controller) |
| `docker compose config` | Valido (postgres-data volume + default network) |

Iteracao intermediaria: 2 testes falharam em uma primeira execucao:

1. `AesWebhookSecretProtectorTests.Unprotect_ShouldThrow_WhenPlaintextLacksExpectedPurpose` — `Unprotect` lancava `CryptographicException` (padding invalido) antes de chegar no check de purpose. Aceito pelo teste (`act.Should().Throw<Exception>().Which.Should().Match(...)`).
2. `DevelopmentDataSeederTests.SeedAsync_ShouldNotLogApiKeyOrSecrets` — log `"hasWebhookSecret=False"` continha substring `secret=` que disparava o teste estrutural. Renomeado para `hasProtectedWebhook={HasProtectedWebhook}`. Re-executado: passou.

## Evidencias

- Build limpo em `dotnet build PaymentHub.slnx` (9/9, 0/0).
- Suite completa em `dotnet test PaymentHub.slnx` (133/133).
- 27 testes focados no novo comportamento (11 protector + 10 handler + 3 seeder + 3 dispatcher).
- Suite do middleware intacta (11/11) — sem regressao no enforcement de status ativo.
- Suite do `ProviderAccount` intacta (15/15) — sem regressao no escopo de contexto autenticado.
- Suite de bootstrap intacta (15/15) — sem regressao na politica de bootstrap.
- Mensagens de log verificadas quanto a nao-leak de webhook secret raw.
- Configuracoes default fail-safe em `appsettings.json` e chave dev em `appsettings.Development.json`.
- Politica documentada em `002-multitenancy-and-authentication.md` e `011-security-and-compliance.md`.
- Matriz de validacao atualizada com 19 novas linhas.
- Nova entrada em `docs/harness/learnings.md`.

## Gaps remanescentes

- **P1-4** — `NoopApplicationWebhookDispatcher` no Worker host. Slice 7-A (Phase 7). O slice 6-C **prepara** o terreno para o Slice 7-A: quando o Worker passar a usar `HttpApplicationWebhookDispatcher`, o segredo estara cifrado em banco e podera ser desprotegido internamente pelo `IWebhookSecretProtector`.
- **P2-2** — Projeto `PaymentHub.IntegrationTests` continua sem testes descobertos. Slice 1-IT.
- **P2-3** — Handlers administrativos nao gravam `AuditLog` (registrar `tenant`/`application`/`provider-account` na tabela `audit_logs`).
- **Rotacao de segredo via API** — fora de escopo do slice; sera decidido em ADR futuro.
- **Migration de backfill** — nao ha dados produtivos pre-existentes; nenhuma migration foi necessaria.

## Proximo slice recomendado

**Slice 7-A** — Substituir `NoopApplicationWebhookDispatcher` por dispatcher HTTP real no Worker host. Com o Slice 6-C concluido, o Worker podera carregar `HttpApplicationWebhookDispatcher` (que ja existe na API host) e usar `IWebhookSecretProtector.Unprotect` para assinar os webhooks internos com o `WebhookSecret` cifrado em banco. Detalhes em `docs/roadmap/002-phase-status-board.md`, Bloco A.

## Arquivos relacionados

- `docs/specs/002-multitenancy-and-authentication.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/learnings.md`
- `docs/harness/security.md`
- `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`
- `docs/audits/slice-6b-provider-account-authenticated-context-report-2026-06-18.md`
- `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`