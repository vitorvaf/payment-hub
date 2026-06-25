# Feature List

Backlog leve para agentes. Use para registrar itens que nao serao implementados no slice atual.

| ID | Tipo | Titulo | Status | Spec/ADR | Observacoes |
|----|------|--------|--------|----------|-------------|
| AI-001 | Harness | Criar CI com restore/build/test | Concluido | `docs/ai/validation-checklist.md` | Workflow `.github/workflows/ci.yml` valida harness, restore, build e test. |
| AI-002 | Testes | Evoluir testes de integracao | Aberto | `docs/specs/013-testing-strategy.md` | Projeto existe, mas precisa cenarios reais. |
| AI-003 | Testes | Avaliar smoke/E2E local | Aberto | `docs/specs/009-api-contracts.md` | Validar API + Postgres + Worker em fluxo minimo. |
| AI-004 | Harness | Aprimorar CI com cache, resultados e gate de secrets | Concluido | `docs/ai/validation-checklist.md` | CI usa cache NuGet, publica `.trx` e roda `scripts/agent-verify.sh` com scan simples de secrets. |
| AI-005 | Harness | Alinhar agentes OpenCode ao harness Copilot/Codex | Concluido | `AGENTS.md` | `.opencode/README.md` e agentes apontam para Copilot instructions, specs, agents equivalentes e validações. |
| AI-006 | Docs | Documentar uso diario do harness | Concluido | `docs/ai/harness-engineering.md` | README e docs IA indicam `agent-init`, prompts, agents, skills, progresso e validacao. |
| AI-007 | Auditoria | Rodar proxima auditoria specs versus codigo | Concluido | `docs/audits/spec-adherence-refresh-2026-06-24.md` | Auditoria curta executada sem corrigir produto; gaps viraram backlog. |
| AI-008 | Segurança | Avaliar secret scanning dedicado no CI | Aberto | `docs/specs/011-security-and-compliance.md` | Considerar Gitleaks ou ferramenta equivalente alem do scan simples atual. |
| AI-009 | Harness | Evoluir OpenCode com agentes, skills e checks locais | Concluido | `docs/harness/opencode.md` | `opencode.json` usa `agent`, skills locais, permissao segura e scripts docs/architecture/smoke. |
| PH-SEC-001 | Segurança | Proteger `ApplicationClient.WebhookSecret` em repouso | Aberto | `docs/specs/011-security-and-compliance.md` | Gap P1; decidir ADR-0007 e implementar protecao. |
| PH-WORKER-001 | Worker | Substituir dispatcher no-op do Outbox worker | Aberto | `docs/specs/007-inbox-outbox-workers.md` | Gap P1; evitar marcar envio sem dispatch HTTP real. |
| PH-SEC-002 | Segurança | Validar assinatura de webhooks externos reais | Aberto | `docs/specs/006-provider-webhooks.md` | Necessario antes de ativar providers reais fora de sandbox. |
| PH-AUD-001 | Auditoria | Gravar `AuditLog` em acoes administrativas | Aberto | `docs/specs/012-observability-and-audit.md` | Criacao/alteracao de tenants, applications e provider accounts. |
| PH-DB-001 | Banco | Decidir FKs obrigatorias versus referencias logicas | Aberto | `docs/specs/010-database-contract.md` | Atualizar spec/ADR antes de migrations. |
| DOC-001 | Docs | Alinhar overview ao HMAC interno atual | Aberto | `docs/specs/011-security-and-compliance.md` | Usar contrato `{timestamp}.{rawBody}` com headers atuais. |

## Convenções

- Status sugeridos: `Aberto`, `Planejado`, `Em progresso`, `Bloqueado`, `Concluido`.
- Cada item deve apontar spec, ADR ou doc de harness quando existir.
- Nao use este arquivo como substituto de issue tracker quando houver GitHub Issues.
