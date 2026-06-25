# Skill Index

Skills sao contexto sob demanda para reduzir o prompt inicial do OpenCode. Use apenas quando o trabalho encaixar na descricao.

## OpenCode skills

| Skill | Quando usar | Evidencia principal |
| --- | --- | --- |
| `payment-slice` | Feature, bugfix, auditoria ou harness slice multi-step | Plano, arquivos, validacoes e riscos em `agent-progress.md` |
| `dotnet-validation` | Restore, build, test, format, Docker config e scripts | Comandos executados e resultados |
| `architecture-fitness` | Clean Architecture, referencias e limites de camada | Findings de dependencias e ADR/spec impactados |
| `security-review` | Multitenancy, API Key, HMAC, webhooks, logs e secrets | Scan de secrets, specs lidas e achados |
| `docs-maintenance` | Specs, ADRs, docs, OpenCode, Copilot e progresso | Links validos e ausencia de duplicacao |

## Como carregar

Peça a skill pelo nome quando iniciar a etapa. Exemplo: use `payment-slice` para planejar um slice ou `security-review` antes de concluir uma mudanca sensivel.

## Regras

- Skills nao substituem specs nem ADRs.
- Skills devem apontar para fontes de verdade, nao copiar contratos extensos.
- Skills experimentais devem usar nomes explicitos e permissao `ask` no OpenCode.
- Toda nova skill precisa de `SKILL.md`, frontmatter `name` e `description` especifica.

## Manutencao

Ao criar ou mudar skill:

1. Atualize este indice.
2. Rode `scripts/agent-docs-check.sh`.
3. Rode `scripts/agent-verify.sh`.
4. Registre evidencia em `agent-progress.md`.
