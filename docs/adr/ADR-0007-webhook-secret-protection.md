# ADR-0007 - Protecao de `ApplicationClient.WebhookSecret` em repouso

## Status

Aceito

## Data

2026-06-25 (decisao registrada), 2026-06-26 (consolidacao no Slice 7-A.9)

## Contexto

O `ApplicationClient.WebhookSecret` e usado internamente para assinar webhooks internos (`X-PaymentHub-Signature` HMAC-SHA256 sobre `{timestamp}.{rawBody}`). O sistema precisa **recuperar** o segredo em memoria para assinar e verificar; portanto, hash unidirecional nao serve.

Ate o Slice 6-C (2026-06-25), o segredo era persistido em texto claro na coluna `application_clients.webhook_secret`. Em caso de vazamento de banco, um atacante poderia forjar webhooks internos assinados para qualquer aplicacao cliente. As credenciais de provider ja usavam `ICredentialProtector` + `AesCredentialProtector` (AES-CBC com chave em `PaymentHub:CredentialEncryptionKey`), mas o webhook secret nao tinha mecanismo equivalente.

Decisoes pre-relacionadas:

- ADR-0001: stack .NET 10.
- ADR-0003: hosted checkout only (consumidor nao envia cartao).
- ADR-0004: API Key server-to-server.
- Spec 011: HTTPS obrigatorio em producao, HMAC para webhooks internos.

## Decisao

Proteger `ApplicationClient.WebhookSecret` em repouso usando criptografia simetrica reversivel (AES-CBC) com chave separada de credenciais de provider:

1. **Interface em Application**: `IWebhookSecretProtector` em `src/PaymentHub.Application/Abstractions/Security/ICrypto.cs`, com `Protect(string)` e `Unprotect(string)`. Mesma familia de `ICredentialProtector`, mas com pre-fixo de proposito proprio para evitar reuse acidental de blobs entre sistemas.

2. **Implementacao em Infrastructure.Postgres**: `AesWebhookSecretProtector` em `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs`. Cifra AES-CBC com chave lida de `PaymentHub:WebhookSecretEncryptionKey` (32 bytes; valores menores preenchidos com `0` ate 32 bytes; valor ausente lanca `InvalidOperationException` no construtor). Cada blob cifrado leva um prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1` verificado em tempo constante via `CryptographicOperations.FixedTimeEquals`. O pre-fixo de proposito impede que um blob gerado por outro sistema seja decifrado por este.

3. **Registro DI**: `services.AddSingleton<IWebhookSecretProtector, AesWebhookSecretProtector>()` em `PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs`. O lifetime e Singleton porque a chave e resolvida uma unica vez no construtor.

4. **Persistencia**: `ApplicationClient.WebhookSecret` (private set) so aceita blobs ja protegidos. Construtor e `UpdateWebhook(...)` exigem parametro nomeado `protectedWebhookSecret`. A coluna `application_clients.webhook_secret` (varchar(500) nullable) foi mantida; o conteudo passa a ser blob cifrado em Base64. Nenhuma migration estrutural necessaria.

5. **API / DTO**: `ApplicationClientResponseDto` expoe apenas `hasWebhookSecret: bool`. Nao expoe `webhookSecret`, `protectedWebhookSecret` ou similar. `RegisterApplicationClientRequestDto` aceita `webhookSecret` raw no body; o handler o protege via `IWebhookSecretProtector.Protect` antes de persistir.

6. **Leitura interna**: `HttpApplicationWebhookDispatcher.DispatchAsync` chama `IWebhookSecretProtector.Unprotect` imediatamente antes de `_signer.Sign(...)`. Se `Unprotect` falhar (chave diferente, blob corrompido, prefixo invalido), o dispatcher loga o erro como `WebhookDispatcherCategory.UnprotectFailure` e **nao** envia a requisicao HTTP. Preferencia explicita por "abortar cedo" em vez de "enviar sem assinatura".

7. **Logs**: o segredo raw nunca aparece em logs. O valor protegido tambem nao deve aparecer (a unica exposicao via log do seedor e a flag `hasProtectedWebhook={bool}`).

8. **Configuracao por ambiente** (consolidada no Slice 7-A.6):

   - `appsettings.json` (production): placeholder vazio `"PaymentHub": { "WebhookSecretEncryptionKey": "" }`.
   - `appsettings.Development.json`: valor fake explicito `dev-webhook-secret-key-change-me-32bytes` (39 caracteres).
   - Producao: valor real vem por variavel de ambiente `PaymentHub__WebhookSecretEncryptionKey=<valor-real>`, secret manager, Docker secret ou mecanismo equivalente. **Nunca commitar valor real**.
   - Fail-fast: `src/PaymentHub.Worker/Program.cs:53-56` resolve `IWebhookSecretProtector` em um scope anonimo antes de `host.Run()`. Em producao sem chave, o startup falha com `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")`.

9. **Compartilhamento API/Worker**: o mesmo valor precisa estar disponivel na API (que cifra em `RegisterApplicationClientHandler.HandleAsync`) e no Worker (que decifra em `HttpApplicationWebhookDispatcher.DispatchAsync`). Divergencia provoca `InvalidOperationException("Protected webhook secret purpose mismatch.")` no primeiro dispatch; o Worker entra em loop de retry ate a chave ser corrigida.

## Consequencias

- Worker e API precisam da mesma chave `PaymentHub:WebhookSecretEncryptionKey` quando ambos precisarem acessar o segredo. A gestao de sincronizacao e rotação fica fora do escopo desta ADR.
- Producao deve falhar cedo se a chave obrigatoria estiver ausente — fail-fast no startup do Worker e na primeira resolucao na API.
- Rotacao completa de segredo (re-cifrar todos os blobs) nao e resolvida por esta ADR. Quando necessario, deve ser feita em slice proprio.
- A coluna `webhook_secret` nao exige migration estrutural. Operadores que ja tem dados produtivos em texto claro precisariam de migration one-shot antes de subir esta versao (mas nao ha dados produtivos pre-existentes no MVP).
- A exposicao via DTO foi reduzida a `hasWebhookSecret: bool`. Logs nunca devem incluir o segredo raw nem o protegido.

## Seguranca

- Segredo raw nunca aparece em logs, respostas HTTP, DTOs ou `OutboxEvent.LastError`.
- Segredo protegido nao deve aparecer em logs (a unica excecao e a flag booleana `hasProtectedWebhook`).
- `LastError` do Outbox armazena apenas categoria + status code HTTP, nao `ex.Message` (vide Slice 7-A.7).
- `WebhookUrl` deve passar por validacao HTTPS/SSRF antes de qualquer persistencia (vide Slice 7-A.5).

## Alternativas consideradas

- **Hash unidirecional com sal** (ex.: PBKDF2): descartado porque o sistema precisa **recuperar** o segredo em memoria para assinar HMAC. Hash nao permite essa operacao.
- **IDataProtectionProvider (Data Protection do ASP.NET Core)**: descartado porque exigiria injecao de dependencia web-framework-specific em `PaymentHub.Application`, quebrando Clean Architecture. O projeto ja optou por AES proprio em `AesCredentialProtector`; consistencia foi escolhida sobre conveniencia.
- **Cifragem com chave derivada do `CredentialEncryptionKey`**: descartado para reduzir blast radius. Rotacao da chave de webhook nao deve re-cifrar credenciais de provider, e vice-versa.
- **Hash via KMS externo** (AWS KMS, Azure Key Vault): fora do escopo do MVP. Pode ser considerado em fase futura quando o projeto tiver infra de cloud.

## Decisao final

Padrao obrigatorio para qualquer segredo reversivel em repouso no projeto: `I<Suffix>Protector` em `Application.Abstractions.Security`, implementacao `Aes<Suffix>Protector` em `Infrastructure.Postgres.Security`, chave separada em `PaymentHubOptions`, registro singleton em `PostgresServiceCollectionExtensions`, parametro nomeado `protected*` na entidade, log de erro sem expor valor.

## Arquivos relacionados

- `src/PaymentHub.Application/Abstractions/Security/ICrypto.cs`
- `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs` (AesWebhookSecretProtector, AesCredentialProtector, HmacApiKeyHasher, HmacWebhookSigner, Sha256IdempotencyRequestHasher)
- `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs`
- `src/PaymentHub.Domain/Entities/ApplicationClient.cs` (parametro `protectedWebhookSecret`)
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (Protect antes de persistir)
- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (Unprotect antes de Sign)
- `src/PaymentHub.Worker/Program.cs` (fail-fast via scope anonimo)
- `src/PaymentHub.Worker/appsettings.json` (placeholder production)
- `src/PaymentHub.Worker/appsettings.Development.json` (valor dev)
- `src/PaymentHub.Api/appsettings.Development.json` (valor dev)
- `docs/specs/011-security-and-compliance.md` (Politica de protecao)
- `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`
- `docs/audits/slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`
- `docs/harness/learnings.md` (entrada de 2026-06-25 sobre o padrao)
