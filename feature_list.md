# Feature List

Backlog leve para agentes. Use para registrar itens que nao serao implementados no slice atual.

| ID | Tipo | Titulo | Status | Spec/ADR | Observacoes |
|----|------|--------|--------|----------|-------------|
| AI-001 | Harness | Criar CI com restore/build/test | Concluido | `docs/ai/validation-checklist.md` | Workflow `.github/workflows/ci.yml` valida harness, restore, build e test. |
| AI-002 | Testes | Evoluir testes de integracao | Aberto | `docs/specs/013-testing-strategy.md` | Projeto existe, mas precisa cenarios reais. |
| AI-003 | Testes | Avaliar smoke/E2E local | Aberto | `docs/specs/009-api-contracts.md` | Validar API + Postgres + Worker em fluxo minimo. |

## Convenções

- Status sugeridos: `Aberto`, `Planejado`, `Em progresso`, `Bloqueado`, `Concluido`.
- Cada item deve apontar spec, ADR ou doc de harness quando existir.
- Nao use este arquivo como substituto de issue tracker quando houver GitHub Issues.
