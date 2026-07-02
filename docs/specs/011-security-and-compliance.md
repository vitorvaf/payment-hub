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

## Politica de bootstrap e admin seed

- Toda criacao automatica de dados iniciais (tenant, application, API Key, provider account) deve passar por `IBootstrapPolicy`.
- `Production` nao cria dados sensiveis automaticamente. `Bootstrap:AllowProductionBootstrap` precisa estar `true` para que qualquer seed rode em `Production`. Padrao seguro: `false`.
- `Development` e `Test` podem rodar seed automatico apenas com `Bootstrap:Enabled=true` e `Bootstrap:SeedDevelopmentData=true`. Padrao seguro: `false`.
- O seedor nunca loga API Key raw, secrets, webhook secrets, tokens, senhas ou connection strings. Logs podem registrar ids, slugs, ambiente e decisao politica.
- O seedor e idempotente: usa `ITenantRepository.GetBySlugAsync` e `IApplicationClientRepository.GetByTenantAndNameAsync` antes de criar; rodar N vezes nao duplica dados.
- Configuracao ausente ou invalida deve produzir comportamento seguro: `Production` nao cria nada; `Development`/`Test` apenas loga "skipped" se o opt-in nao estiver presente.
- Endpoints publicos de bootstrap (`POST /api/v1/tenants`, `POST /api/v1/applications`, `POST /api/v1/provider-accounts`) permanecem sob `ApiKeyAuthenticationMiddleware`. A politica de bootstrap nao introduz bypass de autenticacao no MVP.
- Detalhes tecnicos em `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`.

## Contratos

| Area | Contrato |
|------|----------|
| API Key | `Authorization: Bearer`, hash HMAC no banco, claro exibido uma vez |
| Provider credentials | JSON protegido por criptografia |
| Application webhook secret | `webhook_secret` persistido como blob cifrado (AES-CBC, chave em `PaymentHub:WebhookSecretEncryptionKey`). Cru apenas em memoria no ponto de assinatura HMAC |
| Webhook interno | `X-PaymentHub-Signature` HMAC-SHA256 sobre `timestamp.rawBody` |
| Webhook externo (AbacatePay) | `X-Webhook-Signature` HMAC-SHA256(Base64) sobre `rawBody`; segredo compartilhado persistido como `webhookSecret` em `ProviderAccount.EncryptedCredentials` (JSON protegido por AES). Controller rejeita 401 antes de persistir quando header ausente. Worker valida apos resolver `ProviderAccount`. |
| Logs | correlation id e contexto sem secrets |
| Tenant/application em endpoints autenticados | Derivado exclusivamente de `ITenantContext` (populado pelo middleware). Body/headers do request nunca podem sobrescrever tenant/application. |

### HMAC de webhook interno

- Algoritmo: `HMAC-SHA256`.
- Encoding do payload: UTF-8.
- Formato da assinatura: hexadecimal lowercase.
- Header de timestamp: `X-PaymentHub-Timestamp`, em Unix time seconds.
- Header de assinatura: `X-PaymentHub-Signature`.
- String assinada: `{timestamp}.{rawBody}`.
- Tolerancia recomendada para consumidores: 5 minutos.
- Prevencao de replay: consumidor deve rejeitar timestamp fora da janela e aplicar idempotencia por `eventId`.
- `rawBody` deve ser o corpo HTTP exatamente como recebido, sem reserializar o JSON.
- Secrets de webhook nunca devem aparecer em logs, erros ou traces.
- Assinaturas devem ser comparadas em tempo constante quando a plataforma permitir.

Exemplo:

```text
rawBody = corpo HTTP exatamente como enviado
timestamp = valor do header X-PaymentHub-Timestamp
signedPayload = timestamp + "." + rawBody
signature = HMACSHA256(webhookSecret, UTF8(signedPayload))
signatureFormat = hexadecimal lowercase
```

Exemplo C# para gerar hex:

```csharp
var signatureBytes = HMACSHA256.HashData(secretBytes, signedPayloadBytes);
var signature = Convert.ToHexString(signatureBytes).ToLowerInvariant();
```

### HMAC de webhook externo — AbacatePay (Slice 2-B — 2026-06-29)

- Algoritmo: `HMAC-SHA256`.
- Encoding do payload: UTF-8.
- Formato da assinatura: **Base64**.
- Header canonico: `X-Webhook-Signature`.
- Header de fallback: `X-Provider-Signature` (legacy, ainda suportado em AbacatePay).
- Sem header de timestamp. Replay/duplicate protection vem da coluna `webhook_events.provider_event_id` (idempotencia por `provider_code + provider_event_id`) e da coluna `processing_status` (MarkProcessing).
- String assinada: `rawBody` (sem timestamp).
- Tolerancia: nao ha janela — o provider e quem decide quando reenvia. Nos dependemos de idempotencia no Inbox.
- `rawBody` deve ser o corpo HTTP exatamente como recebido, sem reserializar o JSON.

Exemplo (JS do doc oficial AbacatePay, comecando a ser traduzido em C#):

```text
rawBody = corpo HTTP exatamente como enviado
signature = Convert.ToBase64String(HMACSHA256(webhookSecret, UTF8(rawBody)))
header = "X-Webhook-Signature: <signature>"
```

Comparacao deve acontecer em **tempo constante** via `CryptographicOperations.FixedTimeEquals(byte[], byte[])`. Mensagens de erro NAO podem vazar:
- `webhookSecret` (raw ou Base64).
- A assinatura recebida.
- O `rawBody` completo.
- A `apiKey` do `ProviderAccount.EncryptedCredentials`.
- Stack traces.

Pipeline por camadas (controller + handler + adapter):

- **Controller** (`ProviderWebhooksController.Receive`):
  - Le `[FromHeader(Name = "X-Webhook-Signature")] string? abacateSignature` e `[FromHeader(Name = "X-Provider-Signature")] string? legacySignature`.
  - Seleciona `X-Webhook-Signature` quando ambos chegam.
  - Quando `providerCode == "AbacatePay"` (case-insensitive) e o header de assinatura esta ausente ou branco, retorna `401 Unauthorized { error = "missing_signature" }` **sem** gravar `WebhookEvent` nem chamar o handler.
  - Providers nao-AbacatePay preservam comportamento legacy.

- **Handler** (`ProcessWebhookEventHandler`):
  - Para `ProviderCode.AbacatePay` resolve o `ProviderAccount` via `IProviderAccountRepository.GetByCodeAsync(tenantId, applicationId, code)`. Roteamento inicial vem de `data.metadata.{tenantId, applicationId, paymentId}` do payload bruto (parsing tolerante, sem tentativas amplas).
  - Desprotege `EncryptedCredentials` via `ICredentialProtector.Unprotect`. Extrai `webhookSecret` (preferindo campo explicito, caindo para `secret` legacy).
  - Passa o segredo ao adapter via `ProviderWebhookRequest.WebhookSecret` (init-only). O segredo NAO e persistido em `WebhookEvent`, NAO e logado, NAO aparece em `LastError`.
  - Quando o `ProviderAccount` ou o pagamento nao podem ser resolvidos, marca o evento como `Failed` com `LastError` categorizado e seguro.
  - Quando o adapter diz `IsValid=false`, marca como `Failed` com `LastError` sanitize-ado (remove quebras de linha, NULs, cap em 2000 chars).

- **Adapter** (`AbacatePayProviderAdapter.ParseWebhookAsync`):
  - Recusa silencioosamente quando `WebhookSecret` ou `Signature` estao ausentes.
  - Verifica HMAC via `IAbacatePayWebhookSignatureVerifier`. Categorias: `None`, `MissingSignature`, `MalformedSignature`, `MissingSecret`, `SignatureMismatch`.
  - Normaliza via `IAbacatePayWebhookNormalizer` (eventos `transparent.completed|refunded|disputed|lost`).
  - Mapeia `PaymentStatus` canonico via `MapEvent` (decisoes documentadas em teste).
  - Em qualquer falha, `ProviderWebhookParseResult.IsValid=false`, `ErrorMessage` categorizado.

#### Falha de HMAC e seguranca da mensagem

`AbacatePayClientException` e `AbacatePayWebhookClientException` NAO carregam o segredo nem o body bruto. Mesma politica aplicada em todo o pipeline. `.LastError` no banco (coluna `webhook_events.last_error`) nunca tera mais de 2000 caracteres, sem quebra de linha, sem NUL, sem stack.

### Protecao de `ApplicationClient.WebhookSecret` em repouso

O segredo de webhook da application e usado internamente para assinar webhooks internos (`X-PaymentHub-Signature` HMAC-SHA256 sobre `{timestamp}.{rawBody}`). O sistema precisa **recuperar** o segredo em memoria para assinar e verificar; portanto, **nao** se usa hash unidirecional.

Regra central:

```text
persistencia: valor protegido (AES-CBC com IV randomico + prefixo de proposito)
uso interno: valor desprotegido apenas no momento da assinatura
resposta/log: nunca expor (nem raw, nem protegido)
```

Implementacao:

- Interface: `IWebhookSecretProtector` em `PaymentHub.Application/Abstractions/Security/ICrypto.cs` (mesma familia de `ICredentialProtector`).
- Implementacao: `AesWebhookSecretProtector` em `PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs`. Cifra AES-CBC com chave lida de `PaymentHub:WebhookSecretEncryptionKey` (32 bytes; valores menores sao preenchidos com `0` ate 32 bytes; valor ausente lanca `InvalidOperationException`). O payload cifrado carrega um prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1` antes do segredo raw, e o `Unprotect` rejeita blobs sem esse prefixo (comparacao em tempo constante via `CryptographicOperations.FixedTimeEquals`).
- Registro DI: `services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>()` em `PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs`.
- Persistencia: a coluna `application_clients.webhook_secret` armazena o blob cifrado (Base64). `ApplicationClient.WebhookSecret` (private set) so aceita blobs ja protegidos. Construtor e `UpdateWebhook(...)` exigem `protectedWebhookSecret`.
- API: o DTO `ApplicationClientResponseDto` expoe apenas `hasWebhookSecret: bool`. Nao expoe `webhookSecret`, `protectedWebhookSecret`, `encryptedWebhookSecret` ou similar. O DTO `RegisterApplicationClientRequestDto` aceita `webhookSecret` (raw) no body para criacao, e o handler o protege antes de persistir.
- Leitura: `HttpApplicationWebhookDispatcher.DispatchAsync` chama `IWebhookSecretProtector.Unprotect` imediatamente antes de `_signer.Sign(...)`. Se `Unprotect` falhar (chave diferente, blob corrompido), o dispatcher loga o erro e **nao** envia a requisicao HTTP.
- Logs: o segredo raw nunca aparece em logs. O valor protegido tambem nao deve aparecer em logs (a unica exposicao via log do seeder e a flag `hasProtectedWebhook={bool}`).
- Configuracao: `PaymentHub:WebhookSecretEncryptionKey` e obrigatorio para qualquer codepath que cifre ou decifre. Em `Development`/`Test`, `appsettings.Development.json` traz valor fake explicito (`dev-webhook-secret-key-change-me-32bytes`). Em `Production`, a chave precisa vir de variavel de ambiente ou secret manager; nenhum fallback hardcoded e gerado em runtime (ausencia da chave lanca `InvalidOperationException`).
- Migration: nenhuma migration estrutural foi necessaria. A coluna `webhook_secret` (maxLength=500, nullable) foi mantida; o conteudo passa a ser o blob cifrado em Base64. Nao ha dados produtivos pre-existentes.
- Tests: `AesWebhookSecretProtectorTests` (11 testes), `RegisterApplicationClientHandlerTests` (10 testes), `DevelopmentDataSeederTests` (3 testes novos), `HttpApplicationWebhookDispatcherTests` (3 testes). Total adicionado pelo Slice 6-C: 27 testes.

#### Configuracao da chave por ambiente (Worker e API)

A chave `PaymentHub:WebhookSecretEncryptionKey` e lida por `PaymentHubOptions.WebhookSecretEncryptionKey` (secao `PaymentHub`) e consumida pelo protector **em todos os pontos que cifram ou decifram** o segredo de webhook. O mesmo valor precisa estar disponivel na **API** (que cifra em `RegisterApplicationClientHandler.HandleAsync`) e no **Worker** (que decifra em `HttpApplicationWebhookDispatcher.DispatchAsync` antes de assinar HMAC). Sem o mesmo valor nos dois processos, `Unprotect` falha e o dispatcher aborta o envio (slice 6-C).

Regras de configuracao:

- `appsettings.json` (production): placeholder explicito vazio.
  ```json
  {
    "PaymentHub": {
      "WebhookSecretEncryptionKey": ""
    }
  }
  ```
  O placeholder documenta o nome canonico da chave e obriga o operador a fornecer valor real por canal externo. **Nenhum valor real pode ser commitado**.
- `appsettings.Development.json` (dev/test): valor fake explicito de pelo menos 32 caracteres (ex.: `dev-webhook-secret-key-change-me-32bytes`). Serve apenas para que o Worker e a API subam localmente e para que os testes passem sem variavel de ambiente. **Nunca usar em producao**.
- Producao: valor real vem por variavel de ambiente (ex.: `PaymentHub__WebhookSecretEncryptionKey=<valor-real>`), secret manager, Docker secret ou mecanismo equivalente. O mesmo valor precisa ser fornecido para a API e o Worker; divergencia provoca `InvalidOperationException("Protected webhook secret purpose mismatch.")` no primeiro dispatch e o Worker entra em loop de retry (slice 6-C + slice 7-A.3 + slice 7-A.6).
- Fail-fast no Worker: `src/PaymentHub.Worker/Program.cs:53-56` resolve `IWebhookSecretProtector` em um scope anonimo antes de `host.Run()`. Se a chave estiver ausente, a excecao e capturada pelo `try/catch` externo e logada como fatal (slice 7-A.3). A API tem o mesmo comportamento por meio do `AddPaymentHubPostgres` que registra o protector como Singleton — a primeira resolucao falha com `InvalidOperationException` antes do request pipeline subir.
- Tamanho minimo: o protector preenche com `0` ate 32 bytes quando o valor e menor. Isso evita crash em dev com valor curto, mas nao dispensa o requisito de chave com entropia razoavel em producao (>= 32 bytes aleatorios).

### Protecao SSRF em `ApplicationClient.WebhookUrl`

O `WebhookUrl` cadastrado por uma application e o destino real dos webhooks internos disparados pelo Worker. Sem validacao, um atacante que possua uma API Key valida poderia apontar a URL para servicos internos (cloud metadata service, bancos internos, redes privadas) e usar o Payment Hub como proxy para exfiltrar dados ou atacar a propria infraestrutura.

Regra central:

```text
Webhooks internos NUNCA devem ser entregues em destinos nao-publicos.
Toda escrita de WebhookUrl passa por RegisterApplicationClientValidator.
```

Regras obrigatorias:

- **URI absoluta obrigatoria**: a entrada deve ser uma URI absoluta bem-formada (`Uri.TryCreate(value, UriKind.Absolute, out _)`). Caminhos relativos, fragmentos sem scheme, espacos em branco e valores vazios sao rejeitados.
- **HTTPS obrigatorio**: o scheme deve ser `https`. O scheme `http` e permitido **apenas** em ambiente `Development` e **apenas** quando o host e loopback (`localhost`, `127.0.0.0/8`, `::1`).
- **Hostnames bloqueados** (sempre, mesmo em Development para HTTPS): `localhost`, qualquer `*.localhost` (mDNS) e qualquer `*.local` (link-local).
- **Enderecos IP bloqueados** (sempre): loopback IPv4 (`127.0.0.0/8`), loopback IPv6 (`::1`), IPv4-mapped IPv4 loopback (`::ffff:127.0.0.1` via `IPAddress.MapToIPv4()`), RFC1918 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), link-local/IMDS (`169.254.0.0/16`, `fe80::/10`), unspecified (`0.0.0.0`, `::`) e broadcast (`255.255.255.255`).
- **Excecao de Development**: `http://localhost`/`http://127.0.0.1` sao aceitos em `Development` para permitir testes locais com tunelamento interno. Em `Production` ou `Staging`, esses URLs sao rejeitados.
- **Mensagem unica de erro**: o validator devolve `WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.` para qualquer caso de rejeicao, sem revelar qual regra foi violada (anti-enumeration).
- **Boundary do RFC1918**: `172.15.x.x` e `172.32.x.x` permanecem publicos (sao intencionalmente fora do bloco `172.16.0.0/12`).

Implementacao:

- Helper puro: `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`. Assinatura `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)`. Sem dependencia de DI, logging ou exceptions; totalmente unit-testable.
- Validator: `RegisterApplicationClientValidator` em `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` recebe `IRuntimeEnvironment environment` por injecao de construtor e aplica `RuleFor(x => x.WebhookUrl).Must((req, url) => WebhookUrlValidator.IsAllowed(url, environment.IsDevelopment, out _))` quando `WebhookUrl` esta preenchida.
- Auto-wiring: `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()` em `src/PaymentHub.Api/Program.cs:81` resolve `RegisterApplicationClientValidator` com `IRuntimeEnvironment` registrado como Singleton em `src/PaymentHub.Api/Program.cs:66`.
- Cobertura de testes: `WebhookUrlValidatorTests` (66 casos expandidos de Theory) e `RegisterApplicationClientValidatorTests` (17 testes). Total adicionado pelo Slice 7-A.5: 80+ testes.
- Cobre todos os vetores de SSRF mapeados em auditoria M3 (Worker chama `HttpApplicationWebhookDispatcher.DispatchAsync(outboxEvent)` → `client.WebhookUrl` → HTTP POST; qualquer URL privada seria um SSRF direto).

### Protecao de credenciais AbacatePay em fluxo outbound (Slice 2-A — 2026-06-27)

O Adapter AbacatePay chama a API REST externa a partir de um
`ProviderAccount` persistido. O contrato de credencial e o mesmo ja
documentado para webhooks: **raw nunca aparece em log, response,
DTO, raw response JSON, exception message ou `OutboxEvent.LastError`**.
O adapter tem camada propria de protecao alem do `ICredentialProtector`
porque outbound HTTP tem superficie de leak diferente da persistencia:

```text
persistencia: ProviderAccount.EncryptedCredentials (AES, mesmo padrao do webhook secret)
uso interno: apiKey desprotegida apenas no header Authorization no momento da chamada HTTP
resposta/log/exception: nunca expor (nem raw, nem protegida)
```

Implementacao:

- Interface `IAbacatePayClient` vive em `PaymentHub.Infrastructure.Providers/AbacatePay/`. Recebe `apiKey` como parametro de metodo (nao tem estado portando credencial). Cada metodo (`CreateTransparentPixAsync`, `CheckTransparentPixAsync`, `SimulateTransparentPixPaymentAsync`) recebe a chave explicitamente; o construtor da classe nunca ve credencial.
- Implementacao `AbacatePayClient` registra um `HttpClient` nomeado `"abacatepay"` via `IHttpClientFactory` em `PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs:AddHttpClient`. `BaseAddress` deriva de `AbacatePayOptions.BaseUrl`, e `Timeout` de `AbacatePayOptions.TimeoutSeconds`.
- Header `Authorization: Bearer <api-key>` e setado em `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey)`. O adapter nao chama `ToString()`, nao loga o header, nao imprime em exception.
- `ICredentialProtector.Unprotect` e chamado **uma unica vez** no inicio de `AbacatePayProviderAdapter.CreateCheckoutAsync`; o JSON desprotegido (`{ "apiKey": "...", "secret": "..." }`) e descartado apos o `JsonDocument.Parse`. O `apiKey` extraido fica em variavel local e nao e persistido em lugar nenhum.

#### `AbacatePayClientException`: mensagem segura

`AbacatePayClientException` carrega apenas `Category` (enum
`AbacatePayErrorCategory`), `StatusCode?` (int quando aplicavel) e
`IsTransient` (bool, default derivado da categoria). Sua mensagem:

```text
NAO inclui a API key.
NAO inclui o header Authorization.
NAO inclui o request body.
NAO inclui o response body, especialmente brCodeBase64.
NAO inclui brCode nem customer.name/email mesmo em sucesso.
Pode incluir o codigo HTTP (e.g. "AbacatePay HTTP 429.").
Pode incluir a categoria (e.g. "AbacatePay error (RateLimited).").
```

Categorias e defaults:

| Categoria | Quando | `IsTransient` default |
|-----------|--------|------------------------|
| `BadRequest` (1) | HTTP 400 | `false` |
| `Unauthorized` (2) | HTTP 401/403 | `false` |
| `NotFound` (3) | HTTP 404 | `false` |
| `RateLimited` (4) | HTTP 429 | `true` |
| `ServerError` (5) | HTTP 5xx | `true` |
| `Network` (6) | `HttpRequestException` | `true` |
| `Timeout` (7) | `TaskCanceledException` por `HttpClient.Timeout` | `true` |
| `EnvelopeFailure` (8) | HTTP 2xx com `success=false` ou JSON malformado | `false` |
| `Unexpected` (9) | catch-all | `false` |
| `SimulationDisabled` (10) | `SimulateTransparentPixPaymentAsync` com `AllowDevModeSimulation=false` | `false` |

Caller `CancellationToken` cancelado propaga como
`OperationCanceledException` (nao e envelopado) — distinguindo cancelamento
do operador de timeout do `HttpClient`.

`AbacatePayProviderAdapter.CreateCheckoutAsync` traduz a exception em
`CreateCheckoutProviderResult { Success = false, ErrorMessage = "AbacatePay error (Category)." }`. A `ErrorMessage` tambem e segura: contem apenas a categoria enum em texto. O `Payment.RegisterAttempt` recebe a categoria enum via `providerResult.ErrorMessage`, mas o `PaymentAttempt.LastError` NAO armazena a mensagem do client.

### Slice 2-C.1 — Cliente HTTP real de gerenciamento de webhook AbacatePay (2026-06-30)

A Slice 2-C.1 substitui o `NoOpProviderWebhookManagementClient` (que apenas logava o callback) por um client HTTP real que chama `POST /webhooks/create` no upstream AbacatePay. A interface `IProviderWebhookManagementClient` e o handler `ConfigureProviderAccountWebhookHandler` (Slice 2-C) NAO foram alterados — os 3 gates de remote registration (`RegisterRemotely=true`, `WebhookSecret` nao-nulo, `Providers:AbacatePay:AllowWebhookRegistration=true`) ja' existem, e o Slice 2-C.1 apenas substitui a implementacao por tras da interface.

#### Fluxo de secrets no client real

1. **apiKey** (claro, em memoria apenas):
   - O handler passa o `ProtectedCredentials` (blob AES-protegido) para o client via `RegisterWebhookAsync(..., string protectedCredentials, ...)`.
   - O client chama `IProviderAccountCredentialsReader.ReadApiKey(protectedCredentials)` que **unprotecta** o blob e devolve o `apiKey` plaintext. O `apiKey` vive apenas na variavel local `apiKey` do metodo, usado para construir o `Authorization: Bearer {apiKey}` header. Nao e persistido, nao e logado, nao e incluido em response.
   - Adicionada `InternalsVisibleTo("PaymentHub.Infrastructure.Postgres")` em `PaymentHub.Application.csproj` para que o adapter de `IProviderAccountCredentialsReader` em `Infrastructure.Postgres` possa chamar o inspector `ProviderAccountCredentialsInspector` (que continua `internal static` na Application).

2. **webhookSecret** (claro, transiente):
   - O handler passa o `WebhookSecret` plaintext (recebido do request) para o client via parametro `webhookSecret`.
   - O client usa o valor para popular o campo `secret` do JSON body enviado ao upstream. Nao e logado, nao e persistido em `last_error`, nao e incluido em response.
   - O secret retornado pelo upstream NAO existe (o endpoint `POST /webhooks/create` retorna apenas `{ data: { id } }`).

3. **EncryptedCredentials** (blob AES-protegido):
   - O handler **NUNCA** retorna o `EncryptedCredentials` em response. Validado por reflexao em `ProviderAccountsWebhookControllerTests` e `AbacatePayWebhookManagementE2ETests` (DTO nao expoe `EncryptedCredentials`).

#### No-leak guarantees (anti-patterns MUST-NOT-REGRESS)

- `LastError` do `OutboxEvent` NAO e tocado pelo client de webhook management (a slice e' somente sobre `webhook_remote_status` que ja' e' um enum serializado).
- `last_error` em caso de categoria de erro NAO inclui `ex.Message`. A mensagem da `AbacatePayClientException` carrega apenas a categoria enum + status code generico (`"AbacatePay HTTP {statusCode}."`).
- **NUNCA** loga `apiKey`, `webhookSecret`, `Authorization` header, body request, body response, ou signature.
- Loga apenas: `providerCode` (enum value), `endpoint.Length` (int), `eventCount` (int), `category` (enum value), `statusCode` (int).
- **NUNCA** retorna o `apiKey` no body do PUT/GET. Validado por reflexao em 2 testes do controller + 1 teste E2E.

#### Categorizacao de erros

Reusando `AbacatePayClientException` / `AbacatePayErrorCategory` (ja documentado acima para o Slice 2-A):

| Status | Categoria | Transient |
|--------|-----------|-----------|
| 400 | BadRequest | nao |
| 401/403 | Unauthorized | nao |
| 404 | NotFound | nao |
| 429 | RateLimited | sim |
| 5xx | ServerError | sim |
| `HttpRequestException` | Network | sim |
| `TaskCanceledException` (sem caller cancel) | Timeout | sim |
| `TaskCanceledException` (com caller cancel) | `OperationCanceledException` propaga | n/a |
| envelope `success=false` com HTTP 2xx | EnvelopeFailure | nao |
| flag off (defensivo) | `RegistrationFailed` (categoria nova `RegistrationDisabled = 11` disponivel mas nao usada) | nao |
| Provider != AbacatePay | `RegistrationFailed` (sem HTTP) | n/a |

#### `IProviderAccountCredentialsReader` (nova abstraction)

Promovida a interface publica em `PaymentHub.Application/Abstractions/Security/IProviderAccountCredentialsReader.cs` para permitir que o client de Infrastructure acesse a leitura do `apiKey` sem expor o helper privado `ProviderAccountCredentialsInspector` (que continua `internal static`). Mantem a invariante "no exception on bad input" — `ReadApiKey` retorna `null` em vez de lancar quando o blob nao pode ser unprotected.

#### Di

- `IProviderWebhookManagementClient` registrado como `AbacatePayWebhookManagementClient` (Singleton). `NoOpProviderWebhookManagementClient` removido.
- Named `HttpClient` dedicado: `abacatepay-webhooks` (distinto de `abacatepay` que serve transparent-PIX). Permite tunar timeout/retry/rate-limit do ciclo de webhook management independentemente.
- `IProviderAccountCredentialsReader` registrado como Singleton em `Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs`.

#### Anti-regression rules (re-asserting Slice 2-C)

- `webhook_events` permanece `text` (NAO `jsonb`).
- `webhookSecret` NAO ganha coluna propria.
- DTOs de request NAO aceitam `tenantId`/`applicationId` (Slice 6-B).
- 3-gate rule no handler preservada.
- `OutboxDispatcherWorker` e HMAC interno NAO foram alterados.
- A migration Slice 2-C (`20260630001726_AddProviderAccountWebhookColumns`) NAO foi alterada por esta slice.

#### Simulacao opt-in

`Providers:AbacatePay:AllowDevModeSimulation=true` liga o endpoint
`POST /transparents/simulate-payment`. Default seguro: **false** em
`appsettings.json` (production) e so `true` em `appsettings.Development.json`. O `AbacatePayClient.SimulateTransparentPixPaymentAsync` lanca `AbacatePayClientException(SimulationDisabled)` **antes** de montar qualquer HTTP request quando a flag esta desligada — preferencia explicita por "falhar cedo" em vez de enviar sem ciente.

Cobertura de testes do contrato:

- `AbacatePayClientTests` afirma que `Exception.Message` NAO contem a API key, NAO contem `brCodeBase64`, NAO contem o response body bruto, em todos os caminhos de erro (categoria 400/401/403/404/429/5xx, network, timeout, envelope failure, malformed JSON).
- `AbacatePayProviderAdapterTests` afirma que `CreateCheckoutProviderResult.RawResponseJson` NAO contem a API key, o `Secret`, ou o marker do `FakeCredentialProtector`.
- Nenhuma chamada externa real e feita em testes: `ScriptedHandler` + `SingleHandlerHttpClientFactory` isolam totalmente o IO.

O Worker dispara webhooks internos para a `WebhookUrl` da `ApplicationClient` apos persistir um `OutboxEvent`. Antes do Slice 7-A, o Worker usava um `NoopApplicationWebhookDispatcher` que marcava eventos como `Sent` sem envio HTTP real (gap P1-4). O dispatcher HTTP real introduzido pelo Slice 7-A tem as seguintes garantias de seguranca:

#### Localizacao e dependencia

- `HttpApplicationWebhookDispatcher` vive em `src/PaymentHub.Infrastructure.Postgres/Webhooks/`. Nao depende de `PaymentHub.Api`. Validado por `scripts/agent-architecture-check.sh` (Worker continua sem depender de Api).
- DI centralizado em `AddPaymentHubPostgres` (registro `Scoped`). Nao ha registro duplicado na API nem no Worker.
- `HttpClient` obtido via `IHttpClientFactory.CreateClient("application-webhook")` (registrado em `AddPaymentHubPostgres`).

#### Tenant guard

- O dispatcher busca o `ApplicationClient` via `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)`. Em miss, loga warning com `tenantId`/`applicationId`/`outboxEventId` e retorna sem lancar. Tenant guard explicito impede que um `applicationId` ambiguo (ou atacante) leve o dispatcher a entregar webhook em application de outro tenant.
- Erro e registrado como retry com `WebhookDispatcherCategory.UnexpectedDispatcherError` (ou categoria dedicada, conforme evolucao).

#### `LastError` seguro (politica)

`OutboxEvent.LastError` armazena apenas:

- `WebhookDispatcherCategory` (enum de 8 valores: `HttpFailure`, `NetworkError`, `Timeout`, `UnprotectFailure`, `MissingWebhookUrl`, `MissingWebhookSecret`, `UnexpectedDispatcherError`, `ProcessingOrphaned`).
- `int?` (HTTP status code, quando aplicavel).

**Nao** armazena:

- `ex.Message` (pode conter body HTTP retornado pelo consumer, com dados de pagamento ou query strings com credenciais).
- `ex.StackTrace` (caminhos internos, versao de runtime).
- URL com credenciais em query string (consumidor malicioso pode forcar sua inclusao em `Exception.Message`).
- Segredo raw ou protegido do consumer.

Metodos publicos: `OutboxEvent.MarkRetryWithStatus(WebhookDispatcherCategory, int statusCode, DateTime nextRetryAt)` e `OutboxEvent.MarkFailedWithStatus(WebhookDispatcherCategory, int statusCode)`. Worker usa **apenas** esses metodos. Logs do Worker podem carregar a mensagem completa para debugging, mas ela nunca chega ao banco.

Categorias e semantica:

| Categoria | Quando dispara | `StatusCode` obrigatorio | Retry? |
|-----------|----------------|---------------------------|--------|
| `HttpFailure` (1) | Consumer retornou nao-2xx | sim | sim |
| `NetworkError` (2) | DNS, conexao reset, TLS handshake | nao | sim |
| `Timeout` (3) | `HttpClient` excedeu `WebhookHttpTimeoutSeconds` | nao | sim |
| `UnprotectFailure` (4) | `IWebhookSecretProtector.Unprotect` falhou | nao | sim |
| `MissingWebhookUrl` (5) | Application sem `WebhookUrl` | nao | nao (Failed direto) |
| `MissingWebhookSecret` (6) | Reservado (nao deve ocorrer no codigo atual) | nao | depende |
| `UnexpectedDispatcherError` (7) | Excecao nao esperada | nao | sim |

#### Comportamento `UnprotectFailure`

Quando `IWebhookSecretProtector.Unprotect` falha (chave divergente entre API e Worker, blob corrompido, prefixo invalido), o dispatcher **nao** envia HTTP request. Marca o evento como retry com `UnprotectFailure`. Preferencia explicita por "abortar cedo" em vez de "enviar sem assinatura".

#### Comportamento `MissingWebhookUrl`

Application sem `WebhookUrl` configurada (ou seja, foi registrada com `WebhookUrl=null`) e marcada como `Failed` direto, sem retry — o endereco nao vai aparecer magicamente. A categoria e `MissingWebhookUrl`.

#### Validacao de `WebhookUrl` em camadas

Toda escrita de `WebhookUrl` passa por `RegisterApplicationClientValidator` (HTTPS/SSRF, vide secao anterior). O dispatcher confia que `ApplicationClient.WebhookUrl` foi validado na entrada; o dispatcher nao re-valida (reduz duplicacao, e o `OutboxEvent` ja persiste `ApplicationId` + `TenantId` garantindo que vem de fonte ja validada).

#### Fail-fast de `IWebhookSecretProtector` no Worker

`src/PaymentHub.Worker/Program.cs` resolve `IWebhookSecretProtector` em um scope anonimo antes de `host.Run()`. Se `PaymentHub:WebhookSecretEncryptionKey` estiver ausente, o startup falha com `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")` capturada pelo `try/catch` externo. Isso reduz MTTR em deploys com configuracao errada — sem o fail-fast, o Worker subiria normalmente e so falharia no primeiro dispatch.

#### HMAC de webhook interno

Mantido conforme contrato existente (vide secao "HMAC de webhook interno" acima). O dispatcher HTTP real usa o mesmo `HmacWebhookSigner` ja documentado; a unica diferenca e que o segredo agora vem via `IWebhookSecretProtector.Unprotect` em vez de `ApplicationClient.WebhookSecret` raw (vide `docs/adr/ADR-0007-webhook-secret-protection.md`).

#### Segredos, logs e respostas

- Segredo raw nunca aparece em logs, respostas HTTP, DTOs ou `OutboxEvent.LastError`.
- Segredo protegido nao aparece em logs (unica excecao: flag `hasProtectedWebhook={bool}` no seeder).
- `LastError` nao contem body HTTP, query strings, stack traces ou secrets.
- `WebhookUrl` validada por HTTPS/SSRF antes de qualquer persistencia (vide `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`).

#### Gaps conhecidos (deferidos)

- Sweep automatico de eventos `Processing` orfaos (recovery apos crash do Worker). Multi-instancia nao e problema no MVP single-instance.
- Concorrencia multi-instancia via `FOR UPDATE SKIP LOCKED` (Phase 7 multi-instance, fora do MVP).
- Headers adicionais B4-security (`X-PaymentHub-Tenant` / `X-PaymentHub-Application`) — deferred; HMAC ja garante autenticidade.
- API `appsettings.json` ainda sem placeholder `PaymentHub` (paridade com Worker).

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
- WebhookUrl publica HTTPS aceita.
- WebhookUrl nao-HTTPS rejeitada fora de Development.
- WebhookUrl localhost / loopback / RFC1918 / link-local / IMDS / wildcard rejeitada.
- WebhookUrl IPv4-mapped IPv4 loopback (`::ffff:127.0.0.1`) rejeitada.
- WebhookUrl malformada rejeitada.
- WebhookUrl HTTP loopback aceita somente em Development.
- HMAC AbacatePay: valido/invalido, secret ausente, header ausente, base64 malformado, body adulterado, body malformado, evento unsupported, metadata ausente, secret nao-AbacatePay preserva caminho legacy.
- Nenhum teste loga ou persiste `webhookSecret`, `apiKey`, signature recebida, raw body completo ou stack trace.
- Cobertura E2E do `OutboxDispatcherWorker` (Slice 7-IT): happy path com HMAC valido, HTTP 500/429 como retry seguro, `UnprotectFailure` SEM HTTP POST, evento ja `Sent` nao e' redespachado, fluxo completo AbacatePay ate delivery interno. Detalhes tecnicos em `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md` e na nova secao "Slice 7-IT — End-to-end dispatcher" abaixo.

### Slice 7-IT — End-to-end dispatcher (2026-06-30)

A Slice 7-IT fecha o gap "cobertura E2E do dispatcher real" do Phase 7. A suite
`tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs`
exercita o pipeline de producao integral (`PaymentHub.Api` +
`HttpApplicationWebhookDispatcher` + `OutboxDispatcherWorker.DispatchOnceAsync`
+ Postgres real) sem chamada externa real. Os invariants abaixo passam a ser
verificados em E2E alem dos unit tests existentes:

- **HMAC de webhook interno**: `X-PaymentHub-Signature =
  sha256_hex_lowercase(webhookSecret, "{timestamp}.{rawBody}")` em todos os
  deliveries; tamper no body OU no timestamp invalida a assinatura; o helper
  puro `InternalWebhookHmac.Compute/Matches` vive em
  `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs`
  para que nenhum teste copie a logica do `HmacWebhookSigner`.
- **Headers `X-PaymentHub-*`**: `event-id`, `event-type`, `timestamp` e
  `signature` sao todos preenchidos pelo dispatcher real; o fake receiver os
  expõe em `CapturedRequest` para assercao direta.
- **Transicao `Sent`**: 2xx do consumer leva o `OutboxEvent` para `Sent`,
  `SentAt` populado, `LastError = null`, `RetryCount = 0`,
  `NextRetryAt = null`.
- **Retry seguro (HTTP nao-2xx)**: `LastError` tem o formato canonico
  `"HttpFailure: status={code}"`. **NUNCA** contem URL, segredo, blob
  protegido, body da response ou reason phrase. `RetryCount` incrementa
  exatamente 1 por iteracao; `NextRetryAt` e futuro.
- **`UnprotectFailure` aborta cedo**: blob protegido invalido no
  `ApplicationClient.WebhookSecret` leva a `LastError = "UnprotectFailure"`
  com `CallCount == 0` no fake receiver. O dispatcher NAO envia HTTP request
  sem assinatura valida — esse e' o invariant de seguranca mais importante
  deste slice.
- **Eventos `Sent`/`Processing`/`Failed` NAO sao reenviados**: a query do
  worker filtra `Pending` com `next_retry_at` vencido ou nulo; o teste P2.2
  invoca `DispatchOnceAsync` duas vezes e valida `CallCount == 1` na
  segunda iteracao.
- **Caminho completo AbacatePay**: o teste P2.1 dirige o pipeline real
  checkout -> webhook externo -> `ProcessWebhookEventHandler` ->
  `OutboxEvent` -> `OutboxDispatcherWorker` -> `HttpApplicationWebhookDispatcher`
  -> assinatura HMAC valida contra o segredo da `ApplicationClient`,
  provando que nao ha gap entre Inbox e Outbox em producao.

Os testes E2E NAO dependem do worker hospedado (`BackgroundService`) rodar
dentro do `WebApplicationFactory`. `OutboxDispatcherWorker.DispatchOnceAsync`
e' exposto via `InternalsVisibleTo("PaymentHub.IntegrationTests")` em
`PaymentHub.Worker.csproj` e invocado manualmente pelos testes — a mesma
decisao de testabilidade da Slice 3-IT.

## Arquivos relacionados

- `docs/harness/security.md`
- `.github/instructions/security.instructions.md`
- `src/PaymentHub.Infrastructure.Postgres/Security/`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/*`
- `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`
- `docs/audits/slice-2c-abacatepay-webhook-management-report-2026-06-30.md`
- `docs/audits/slice-7-m1-outbox-multi-instance-report-2026-06-30.md`
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs` (claim + sweep)
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260630184619_AddOutboxProcessingStartedAtAndIndexes.cs`
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherConcurrencyTests.cs` (2 testes)
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxProcessingSweepTests.cs` (5 testes)

### Slice 7-M1 — Outbox multi-instancia e sweep de `Processing` orfao (2026-06-30)

A Slice 7-M1 fecha os gaps `M1-security` (sweep automatico de eventos `Processing` orfaos) e `C.3-qa` (`FOR UPDATE SKIP LOCKED` em `OutboxRepository`) que estavam documentados como pendentes de Phase 7. As decisoes abaixo sao imperativas:

#### Claim atomico com `FOR UPDATE SKIP LOCKED`

`IOutboxRepository.ClaimPendingForDispatchAsync(int batchSize, DateTime now, CancellationToken)` substitui o antigo `GetPendingForDispatchAsync`. A implementacao roda `SELECT ... FOR UPDATE SKIP LOCKED LIMIT @batchSize` seguido de `UPDATE status='Processing', processing_started_at=@now` em uma unica transacao `ReadCommitted` no `NpgsqlConnection` raw (EF Core 10 nao traduz `SKIP LOCKED` em LINQ). O Worker NAO chama `MarkProcessing` separado: as rows ja voltam em `Processing` na mesma transacao.

Implicacoes de seguranca:

- **Sem double-dispatch:** dois workers concorrentes nunca pegam a mesma row. Testado por `OutboxDispatcherConcurrencyTests.ShouldNotDoubleDispatch_WhenTwoInstancesRunConcurrently`. Sem isso, o consumidor final receberia cada `payment.approved` duas vezes, podendo acionar logic de idempotencia fragil em produtos consumidores (ex.: Job Search creditar duas vezes o mesmo pedido).
- **Sem cross-tenant access:** o claim preserva o `tenant_id`/`application_id` ja presente na row. O `HttpApplicationWebhookDispatcher` continua chamando `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)` (tenant guard documentado acima). Nenhuma nova superficie cross-tenant e introduzida.
- **Anti-regressao `processing_started_at`:** o Worker faz sanity check no claim (`Status == Processing && ProcessingStartedAt != null`) e pula rows em estado invalido com log `Error`. Isso protege contra uma regressao futura que remova o `UPDATE` do claim path.

#### `OutboxEvent.ProcessingStartedAt`

Nova coluna non-sensitive (`timestamp with time zone`, nullable). **NAO** contem dados sensiveis: e' apenas o instante UTC do claim. Seguro para log de operacao e auditoria. Limpa em toda saida de `Processing` (`MarkSent`, `MarkRetryWithCategory`, `MarkRetryWithStatus`, `MarkFailedWithCategory`, `MarkFailedWithStatus`, `RequeueOrphaned`).

#### Sweep de `Processing` orfao

`IOutboxRepository.SweepOrphanedProcessingAsync(DateTime cutoff, CancellationToken)` move rows com `processing_started_at < cutoff` de `Processing` para `Pending`. Um unico `UPDATE ... WHERE status='Processing' AND processing_started_at < @cutoff` (atomic, idempotente). `cutoff = _clock.UtcNow.AddSeconds(-PaymentHubOptions.OutboxProcessingTimeoutSeconds)` (default 900s).

Implicacoes de seguranca:

- **`last_error` recebe APENAS o literal `"ProcessingOrphaned"` (enum value).** Nunca o motivo original do crash, a URL, o segredo, o body, a stack trace. Esta politica e' identica a' aplicada pelo Worker (ver "LastError seguro" acima) e e' enforced pela query SQL: o literal `'ProcessingOrphaned'` e' hardcoded no template.
- **`next_retry_at` recebe `NULL` (nao `@now`).** Garante que a row e' imediatamente re-disparavel na mesma iteracao. Se gravassemos `@now`, a comparacao `next_retry_at <= @now` no claim poderia falhar por microsegundos e atrasar a entrega em um tick. Decisao explicita para evitar double-tick no caminho orfao.
- **`Sent`/`Failed` NUNCA sao reabertos.** O `WHERE status='Processing'` torna o sweep restrito a' estado transitorio. Testado por `OutboxProcessingSweepTests.OutboxSweep_ShouldNotReopenTerminalEvents`.
- **Sweep NAO perturba workers ativos.** Cutoff e' `now - timeout`. Se o `processing_started_at` de uma row e' mais recente que o cutoff, o sweep ignora. Testado por `OutboxProcessingSweepTests.OutboxSweep_ShouldNotRequeueRecentProcessingEvents`.

#### Anti-regression rules

- `outbox_events.payload` permanece `jsonb` (decisao do Slice 7-IT, NAO regressao). A Slice 7-M1 NAO toca nesta coluna.
- `webhook_events.raw_payload` permanece `text` (decisao do Slice 3-IT, NAO regressao).
- `provider_accounts.webhook_events` permanece `text` (decisao do Slice 2-C, NAO regressao).
- `outbox_events.processing_started_at` e' `timestamptz NULL`, NAO `jsonb`. Nao confunda: e' um timestamp, nao JSON.
- A conexao do `NpgsqlConnection` usada pelo claim e' obtida via `_db.Database.GetDbConnection()` (EF Core owns the connection). NAO chamar `Dispose()` na conexao. O `connectionWasClosed` flag rastreia se a conexao ja estava aberta antes do claim.

#### Cobertura E2E (498 testes)

- `OutboxDispatcherE2ETests` (Slice 7-IT, 7 testes): dispatcher real, HMAC, retry, no-redispatch de Sent, fluxo AbacatePay.
- `OutboxDispatcherConcurrencyTests` (Slice 7-M1, 2 testes): 2 workers concorrentes nao causam double-dispatch; 10 eventos distribuidos entre 3 workers preservam `CallCount == 10`.
- `OutboxProcessingSweepTests` (Slice 7-M1, 5 testes): requeue de orfao, preservacao de Processing recente, nao-reabertura de terminais, respect a `NextRetryAt`, respect a `OutboxWorkerBatchSize`.

#### Anti-patterns proibidos

- **NAO** trocar `FOR UPDATE SKIP LOCKED` por `FOR UPDATE` puro. Sem `SKIP LOCKED`, workers concorrentes serializam em vez de pularem rows bloqueadas, anulando o ganho de concorrencia.
- **NAO** mover o sweep para o mesmo `BeginTransactionAsync` do claim. Sao concerns separados (recovery de crash vs. claim de trabalho novo) e devem rodar em transacoes independentes para que o sweep nao bloqueie o claim path.
- **NAO** usar `ExecuteSqlInterpolated` no sweep. Use `ExecuteSqlRawAsync` com parametros nomeados (`@now`, `@cutoff`) para evitar SQL injection e manter a query estavel para `EXPLAIN`.
- **NAO** reintroduzir `MarkProcessing` separado no Worker. O claim ja entrega rows em `Processing`; voltar atras re-introduz o race window que esta slice fecha.
- **NAO** persistir o motivo do crash (mensagem da exception original, URL, body, stack) em `last_error` no caminho do sweep. Use exclusivamente o literal `"ProcessingOrphaned"`.

### Gerenciamento de webhook AbacatePay via API (Slice 2-C — 2026-06-30)

Os endpoints `PUT`/`GET /api/v1/provider-accounts/{providerAccountId}/webhook` introduzem configuracao explicita de webhook por `ProviderAccount`. As decisoes abaixo sao imperativas:

- `webhookSecret` **nunca** ganha coluna propria. O segredo continua a trafegar apenas dentro de `ProviderAccount.EncryptedCredentials` (JSON protegido por `ICredentialProtector`). A regra em "Provider credentials" continua valida.
- Toda escrita de `callbackUrl` passa por `WebhookUrlValidator` (re-uso da Slice 7-A.5). Mesma politica HTTPS-only + SSRF (RFC1918, link-local, IMDS, loopback) + excecao `http://localhost` em Development.
- Eventos aceitos sao **whitelist** literal na camada de validacao: `transparent.completed`, `transparent.refunded`, `transparent.disputed`, `transparent.lost`. Qualquer outro valor e rejeitado em `400`.
- Tenant e application continuam derivados **exclusivamente** de `ITenantContext` (re-asserting Slice 6-B). O DTO de request NAO expoe `tenantId`/`applicationId`.
- Controller retorna matriz controlada de status codes: `200`, `400` (validation), `401` (contexto ausente), `404` (id nao existe no escopo), `409` (conta inativa OU nao-AbacatePay), `500` apenas em catch-all. Os payloads de erro NAO carregam `tenantId`/`applicationId`/`providerAccountId`.
- Response `ProviderAccountWebhookResponseDto` nao expoe `apiKey`, `webhookSecret`, `protectedWebhookSecret` ou `encryptedCredentials` (validado por reflexao em testes). A unica mencao ao segredo e o boolean `hasWebhookSecret`.
- Memoria: o handler chama `ICredentialProtector.Unprotect` para fazer round-trip das credenciais (`apiKey` + `webhookSecret`). O blob raw nao e mantido em campo de classe; somente em variavel local durante o `HandleAsync`. O GC recolhe ao fim do scope do request.
- Logs estruturados podem mencionar `providerAccountId`/`tenantId`/`applicationId` e a categoria do `WebhookRemoteStatus` (`RemoteRegistrationDeferred` etc.), mas **nunca** `apiKey`, `webhookSecret` (raw ou Base64), `EncryptedCredentials` ou a URL completa de `callbackUrl` quando ela carrega segredo em query string.
- `webhook_events` column no banco e **`text`** (NAO `jsonb`). Mesma anti-regression que `webhook_events.raw_payload` (Slice 3-IT, 2026-06-29): `jsonb` reformata o JSON no insert (espaco apos `:` e `,`), o que quebra qualquer consumidor que depender do shape byte-exact. Documentado inline em `EntityConfigurations.cs` e na migration `20260630001726_AddProviderAccountWebhookColumns`.
- Configuracao da chamada remota: `Providers:AbacatePay:AllowWebhookRegistration` (default `false`). Combinado com `registerRemotely=true` + `webhookSecret` nao-nulo + a policy retornar `true`, o handler chama `IProviderWebhookManagementClient.RegisterWebhookAsync(...)`. O client default (`NoOpProviderWebhookManagementClient`) nao faz HTTP — apenas loga metadata. A implementacao HTTP real (chamada a `POST /webhooks/create`) sera adicionada em slice proprio (sub-seguinte de Slice 2-C), mantendo o no-op como padrao.
- Erros do `IProviderWebhookManagementClient` NAO propagam o segredo. O handler mapeia exito para `Registered`, falha para `RegistrationFailed` ou `NotRegistered`. Mensagens de erro da implementacao concreta continuam sob a politica "mensagem segura" herdada do Slice 2-A.

### Anti-regression "jsonb normaliza whitespace" (Slice 2-C, 2026-06-30)

Adicionada ao Stack de Conhecimento (Slice 3-IT ja tinha documentado para `webhook_events.raw_payload`):

- `provider_accounts.webhook_events` tem que permanecer como `text`.
- Qualquer nova coluna que armazene JSON e que possa ser rodada byte-exact round-trip (ex.: `webhookSecret` em `encrypted_credentials` embora esse ja seja `text` por outro motivo) NAO pode ser `jsonb`.
- Reverter essa coluna para `jsonb` em nova migration quebra `ProviderAccountWebhookPersistenceTests.ProviderAccount_ShouldPersistAllWebhookConfigurationColumns` com `"Expected [...].WebhookEvents to be '[...]' with a length of 48, but '[...]' has a length of 49, differs near " t""`.

### Observabilidade anti-vazamento (Slice 9-O1, 2026-07-01)

A slice 9-O1 introduz o catalogo `PaymentHubLogEvents` + helpers `SafeLog`
+ gate regex em `scripts/agent-docs-check.sh`. As regras abaixo reforcam a
politica "logs nunca carregam segredo" da secao `Regras obrigatorias`
acima:

- O codigo de producao NAO pode chamar `Log(Warning|Information|Error|Debug|Critical|Trace)`
  interpolando tokens `apiKey`, `webhookSecret`, `rawPayload`, `signature`,
  `Authorization`, `body`. O gate `scripts/agent-docs-check.sh` falha o
  build quando o regex encontra um hit. `NoLeakLogTests` (reflection) cobre
  a mesma propriedade em runtime.
- Mensagens de log devem usar SOMENTE:
  - Identificadores truncados via `SafeLog.Id(Guid?)` (8 primeiros chars).
  - Comprimento via `SafeLog.Length(string?)` (sem conteudo).
  - Booleanos canonicos via `SafeLog.Flag(label, bool?)`.
  - Categorias enum via `SafeLog.Category<TEnum>(TEnum)`.
- `OutboxEvent.LastError` continua persistindo apenas categoria enum + status
  code (decisao Slice 7-A.7). NAO `ex.Message`, URL, body, signature, stack.
- `WebhookEvent.LastError` e sanitizado por `ProcessWebhookEventHandler.Sanitize`
  (Slice 2-B): remove `\r`/`\n`/`\0` e limita a 2000 chars.
- O middleware de CorrelationId (`CorrelationIdMiddleware`) NAO loga o valor
  recebido quando rejeita um header invalido. Loga apenas a observacao
  `observability.correlation_id_generated` com o path da request, NAO o
  valor. O `NoLeakLogTests.NoLeak_ShouldNotLogRejectedCorrelationIdValue`
  cobre esta propriedade.
- Tag whitelist em `PaymentHubMetrics.AllowedTagKeys` rejeita em runtime
  qualquer chave de tag fora de: `provider`, `operation`, `status`,
  `error_category`, `event_type`, `environment`, `worker`. NAO HA chave
  que exponha `apiKey`/`webhookSecret`/`rawPayload`/`signature`/`body`/
  `Authorization`. Adicionar uma nova chave exige edicao explicita da
  whitelist.
- `CorrelationId` em si NAO e dado sensivel (identificador opaco gerado
  pelo gateway, sem semantica de tenant ou application), por isso e
  persistido em `webhook_events.correlation_id` e `outbox_events.correlation_id`
  e propagado no header `X-Correlation-Id`. Nao confunda com `apiKey`,
  `tenantId` ou `applicationId` (esses NAO vao em logs nem em headers
  outbound).
