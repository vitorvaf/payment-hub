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
| Webhook externo | Assinatura validada quando provider oferecer |
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

## Arquivos relacionados

- `docs/harness/security.md`
- `.github/instructions/security.instructions.md`
- `src/PaymentHub.Infrastructure.Postgres/Security/`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
