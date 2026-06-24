---
applyTo: "tests/**/*.cs;src/**/*.cs"
---

# Testing Instructions

- Crie testes unitarios para regras de dominio.
- Crie testes de Application para use cases, idempotencia e transicoes de estado.
- Crie testes de integracao para banco, API e Worker quando possivel.
- Evite testes frageis baseados em detalhes internos sem valor comportamental.
- Valide webhooks duplicados, invalidos, orfaos e fora de ordem.
- Valide Outbox com criacao, tentativa de entrega, retry e marcacao de sucesso/falha.
- Valide que dados sensiveis nao aparecem em logs ou respostas.
- Em testes com `DefaultHttpContext` que leem `Response.Body`, configure `ctx.Response.Body = new MemoryStream()`.
- Compare `ContentType` com prefixo quando ASP.NET puder adicionar charset.

Comandos esperados:

```bash
dotnet restore
dotnet build
dotnet test
```
