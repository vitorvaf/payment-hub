# Multitenancy e Autenticacao

## Objetivo

Definir autorizacao server-to-server por API Key e isolamento por `tenant_id` e `application_id`.

## Escopo

- Modelo multitenant por coluna.
- Relacao entre `Tenant`, `ApplicationClient` e `ApiKey`.
- Headers obrigatorios e escopo da chave.
- Tratamento de tenant, application ou API Key inexistente, inativo ou incompatibil.

## Fora de escopo

- OAuth, usuarios finais, painel admin completo e RBAC granular.

## Regras obrigatorias

- Toda chamada autenticada exige `Authorization`, `X-Tenant-Id` e `X-Application-Id`.
- API Key precisa pertencer ao mesmo tenant/application informado nos headers.
- API Key revogada ou inativa retorna 401.
- Tenant suspenso/desativado ou application inativa deve impedir criacao de checkout.
- API Key em claro so pode ser exibida uma vez no cadastro.
- Banco deve armazenar apenas hash e prefixo auditavel da API Key.
- Caminhos anonimos aceitos: health, swagger, raiz, favicon e webhooks externos.

## Contratos

```http
Authorization: Bearer <api_key>
X-Tenant-Id: <tenant_id>
X-Application-Id: <application_id>
```

Erros:

| Condicao | Status esperado |
|----------|-----------------|
| Header ausente ou invalido | 401 |
| API Key invalida, inativa ou revogada | 401 |
| API Key de outro tenant/application | 401 |
| Tenant ou application suspenso/inativo em fluxo de checkout | 401 ou 422 conforme camada atual |

## Criterios de aceite

- Middleware valida hash da chave sem logar a chave apresentada.
- `HttpContext.Items` ou contexto equivalente carrega tenant, application e api key id.
- Handlers nao aceitam operacao fora do tenant/application autenticado.

## Testes esperados

- API Key valida.
- API Key invalida, vazia, revogada e de outro escopo.
- Headers ausentes ou GUIDs invalidos.
- Tenant/application inativo nos fluxos de criacao.

## Arquivos relacionados

- `src/PaymentHub.Api/Auth/ApiKeyAuthenticationMiddleware.cs`
- `src/PaymentHub.Domain/Entities/ApiKey.cs`
- `src/PaymentHub.Infrastructure.Postgres/Security/`
