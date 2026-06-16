# .NET Clean Architecture Instructions

## Camadas esperadas

- Domain: entidades, value objects, regras de domínio e contratos essenciais, sem dependência de infraestrutura.
- Application: casos de uso, comandos, queries, validações de aplicação e orquestração transacional.
- Infrastructure: PostgreSQL, adapters de provedores, criptografia, repositórios, Inbox/Outbox e integrações externas.
- API: camada de entrada HTTP, autenticação, autorização, validação de requests e Swagger/OpenAPI.
- Worker: processamento assíncrono, webhooks, retries, Outbox e tarefas recorrentes.

## Regras

- Evitar lógica de domínio em controllers.
- Controllers devem chamar casos de uso da camada Application.
- Infrastructure pode depender de Application e Domain; Domain não depende de nenhuma camada externa.
- Preferir interfaces nos limites entre Application e Infrastructure.
- Manter dependências explícitas e testáveis.
- Não criar abstrações genéricas antes de haver necessidade real.
