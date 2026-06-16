# Decisões do MVP

Este documento registra as decisões técnicas relevantes do Payment Gateway MVP. Adicione uma nova entrada sempre que tomar uma decisão que afete a arquitetura ou o modelo operacional.

## 2026-06-16 — Adoção de .NET 10 + EF Core 10

- **Contexto**: o ambiente local tem SDK 10.0.109 e runtime 10.0.9. EF Core 10.0.0 está disponível no NuGet.
- **Decisão**: usar `net10.0` para todos os projetos, EF Core 10 e Npgsql 10.
- **Evidência**: `dotnet --list-sdks` reporta 10.0.109; `dotnet build` finaliza com 0 erros em 9 projetos.
- **Impacto**: garante consistência entre API, Worker, testes e migrations.

## 2026-06-16 — Sem mensageria externa no MVP

- **Contexto**: queremos chegar a um MVP testável rápido, sem operar RabbitMQ/Kafka/Azure Service Bus.
- **Decisão**: usar Inbox/Outbox diretamente no PostgreSQL, com workers `BackgroundService`.
- **Evidência**: tabelas `webhook_events` e `outbox_events` no schema inicial; `WebhookProcessorWorker` e `OutboxDispatcherWorker` consumindo-as.
- **Impacto**: é necessário substituir os workers por publishers para um broker real quando a escala exigir. O domínio não precisa mudar; basta criar uma implementação alternativa de `IApplicationWebhookDispatcher`.

## 2026-06-16 — Sem armazenamento de cartão, CVV ou dados sensíveis

- **Contexto**: Payment Hub MVP não é uma instituição de pagamento.
- **Decisão**: usar checkout hospedado (provider hospeda o formulário de pagamento). O `Payment` armazena apenas referência externa, valor em centavos, moeda, status canônico e provider.
- **Evidência**: entidade `Payment` sem campos para número de cartão, validade ou CVV; `PaymentStatus` é canônico e nunca inclui dados sensíveis.
- **Impacto**: o MVP é seguro para PCI-DSS SAQ-A em provedores compatíveis.

## 2026-06-16 — API Key server-to-server com hash HMAC

- **Contexto**: precisamos identificar aplicações clientes sem expor credenciais em texto puro.
- **Decisão**: armazenar `ApiKey.KeyHash` (HMAC-SHA256 com `PaymentHub:ApiKeyHashSecret`). A chave em claro é entregue **uma única vez** no momento de criação.
- **Evidência**: `HmacApiKeyHasher` no projeto `PaymentHub.Infrastructure.Postgres`; `ApiKey` carrega apenas hash e prefixo para auditoria.
- **Impacto**: vazamentos de banco não expõem chaves em claro; auditoria ainda pode correlacionar uso via prefixo.

## 2026-06-16 — Criptografia AES para credenciais de provedores

- **Contexto**: API keys de provedores não podem ser armazenadas em texto puro.
- **Decisão**: serializar credenciais em JSON e proteger com AES-256-CBC; a chave é derivada de `PaymentHub:CredentialEncryptionKey`. No MVP usamos uma chave determinística; produção deve usar um KMS.
- **Evidência**: `AesCredentialProtector` em `PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs`.
- **Impacto**: vazamentos de banco não expõem credenciais em claro, mesmo para o time de banco.

## 2026-06-16 — Retry policy fixa para webhooks e outbox

- **Contexto**: provedores externos e aplicações clientes podem estar temporariamente fora do ar.
- **Decisão**: política `0s → 1m → 5m → 15m → 1h → Failed` aplicada tanto em `WebhookEvent` quanto em `OutboxEvent`.
- **Evidência**: `Domain.Services.RetryPolicy` com 5 tentativas; usado por `ProcessWebhookEventHandler` e `OutboxDispatcherWorker`.
- **Impacto**: tempo máximo de recuperação sem intervenção é ~1h17; após 5 falhas o item vai para `Failed` e exige ação manual.

## 2026-06-16 — Status canônico independente do provider

- **Contexto**: cada provedor tem vocabulário próprio para status de pagamento.
- **Decisão**: a entidade `Payment` armazena sempre o `PaymentStatus` canônico. A tradução é feita por `PaymentStatusMapper`.
- **Evidência**: enum `PaymentStatus` com 11 valores; testes em `PaymentStatusMapperTests`.
- **Impacto**: aplicações clientes consomem um único vocabulário; trocar de provedor não muda a integração.

## 2026-06-16 — Workers no mesmo processo da API e como serviço separado

- **Contexto**: queremos um MVP executável com `docker compose up -d` sem processos extras.
- **Decisão**: a API sobe junto, mas a separação de processos é feita via `docker compose` (serviço `payment-gateway-worker`). Cada um é um executável .NET isolado.
- **Evidência**: `src/PaymentHub.Worker/Program.cs` com `AddHostedService<WebhookProcessorWorker>` e `AddHostedService<OutboxDispatcherWorker>`.
- **Impacto**: permite escalar API e Worker independentemente; o MVP pode ser implantado em um único host sem mudanças.

## 2026-06-16 — Override da dependência vulnerável `System.Security.Cryptography.Xml`

- **Contexto**: EF Core 9 traz transitivamente `System.Security.Cryptography.Xml 9.0.0`, marcado como vulnerável pelo `NU1903`.
- **Decisão**: adicionar PackageReference explícita para `System.Security.Cryptography.Xml 10.0.9` no projeto `PaymentHub.Infrastructure.Postgres` para forçar a versão segura.
- **Evidência**: `dotnet build` retorna 0 warnings após o override.
- **Impacto**: build limpo e seguro até que a dependência transitiva seja atualizada.

## 2026-06-16 — Substituir `dotnet new sln` por `slnx`

- **Contexto**: o template `dotnet new sln` no SDK 10 gera `PaymentHub.slnx` (XML) e não um `.sln` legado.
- **Decisão**: usar `PaymentHub.slnx` como arquivo de solução. CI/scripts devem apontar para ele.
- **Evidência**: arquivo presente na raiz; `dotnet build PaymentHub.slnx` resolve 9 projetos.
- **Impacto**: pipelines devem usar `slnx`. Em ambientes legados, rodar `dotnet sln migrate` para gerar `.sln` se necessário.
