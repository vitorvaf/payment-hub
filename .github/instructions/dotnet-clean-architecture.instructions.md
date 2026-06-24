---
applyTo: "src/**/*.cs"
---

# .NET Clean Architecture Instructions

- Domain contem entidades, value objects, enums e regras de dominio sem dependencia externa.
- Application contem casos de uso, DTOs, interfaces, validacoes de aplicacao e orquestracao.
- Infrastructure contem PostgreSQL, adapters de providers, criptografia, repositorios, Inbox/Outbox e integracoes externas.
- API contem controllers, middleware, autenticacao, autorizacao, validacao HTTP e Swagger/OpenAPI.
- Worker contem `BackgroundService`, processamento assincrono, retries, Inbox/Outbox e tarefas recorrentes.
- Evite logica de dominio em controllers.
- Controllers devem chamar casos de uso da camada Application.
- Infrastructure pode depender de Application e Domain; Domain nao depende de nenhuma camada externa.
- Prefira interfaces nos limites entre Application e Infrastructure.
- Mantenha dependencias explicitas e testaveis.
- Nao crie abstracoes genericas antes de haver necessidade real.
