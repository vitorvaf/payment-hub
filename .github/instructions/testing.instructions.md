# Testing Instructions

## Estratégia

- Criar testes unitários para regras de domínio.
- Criar testes de Application para use cases, idempotência e transições de estado.
- Criar testes de integração para banco, API e Worker quando possível.
- Evitar testes frágeis baseados em detalhes internos sem valor comportamental.
- Validar processamento de webhook com cenários duplicados, inválidos e fora de ordem.
- Validar Outbox com criação, tentativa de entrega, retry e marcação de sucesso/falha.
- Validar que dados sensíveis não aparecem em logs ou respostas.

## Comandos esperados

```bash
dotnet restore
dotnet build
dotnet test
```
