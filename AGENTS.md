# Agents Harness

Este repositorio usa um harness operacional para orientar agentes de IA, GitHub Copilot, Codex e OpenCode. Use este arquivo como indice de sessao, nao como enciclopedia.

## Inicio de sessao

1. Leia este arquivo.
2. Leia `docs/harness/project-context.md`, `docs/harness/workflow.md`, `docs/harness/validation.md`, `docs/harness/security.md` e `docs/harness/learnings.md`.
3. Leia `.github/copilot-instructions.md` e as instrucoes em `.github/instructions/` que se aplicam aos arquivos do slice.
4. Leia a spec relacionada em `docs/specs/README.md` antes de alterar codigo.
5. Leia ADRs em `docs/adr/` quando tocar arquitetura, banco, seguranca, checkout hospedado, API Key, Inbox/Outbox ou providers.
6. Inspecione a estrutura atual e o estado do git antes de editar.

## Escopo

- Trabalhe em um slice pequeno por vez.
- Separe claramente objetivo, fora de escopo, riscos e validacoes planejadas.
- Nao implemente dominio de pagamento quando a tarefa for apenas de harness/documentacao.
- Nao faca refatoracao ampla sem necessidade e sem evidencia.
- Se o codigo divergir da spec, registre o gap e corrija em slice separado quando possivel.

## Progresso

- Use `agent-progress.md` para registrar plano, arquivos tocados, comandos executados e evidencias quando a tarefa tiver mais de um passo.
- Use `feature_list.md` para organizar features, bugs ou melhorias que ainda nao serao implementadas.
- Atualize `docs/harness/learnings.md` apenas quando surgir um aprendizado reutilizavel para proximos agentes.

## Validacao

- Escolha validacoes proporcionais pelo guia `docs/harness/validation.md`.
- Para mudancas de codigo .NET, considere no minimo `dotnet restore`, `dotnet build` e `dotnet test`.
- Para Docker, valide `docker compose config` antes de subir servicos.
- Para mudancas apenas Markdown/harness, valide existencia dos arquivos, links principais, ausencia de secrets e consistencia entre `AGENTS.md`, Copilot e `docs/ai/`.
- Se um comando nao puder rodar, registre o motivo e o risco residual.

## Done

Uma tarefa so pode ser declarada concluida quando:

- O escopo pedido foi atendido sem mudancas fora de contexto.
- Specs/ADRs/instrucoes relevantes foram respeitadas ou gaps foram registrados.
- Validacoes aplicaveis foram executadas ou justificadas.
- Evidencias e arquivos alterados foram listados.
- Aprendizados relevantes foram atualizados em `docs/harness/learnings.md`.

## Seguranca

- Nunca commite secrets, tokens, senhas, API Keys reais, `.env` real ou credenciais de provider.
- Nunca armazene numero de cartao ou CVV.
- Nao remova testes para fazer build passar.
- Nao altere scripts destrutivos, pipelines, autenticacao, autorizacao ou banco sem revisar specs/ADRs e registrar risco.
- Nenhum agente deve fazer merge automatico.

## Referencias

- [Contexto do projeto](docs/harness/project-context.md)
- [Fluxo de trabalho](docs/harness/workflow.md)
- [Validacao](docs/harness/validation.md)
- [Seguranca](docs/harness/security.md)
- [Aprendizados](docs/harness/learnings.md)
- [Specs formais](docs/specs/README.md)
- [ADRs](docs/adr/)
- [Copilot](.github/copilot-instructions.md)
- [OpenCode](.opencode/)
- [Workflow IA](docs/ai/agent-workflow.md)
- [Surface map Copilot](docs/ai/copilot-surface-map.md)
- [Model routing](docs/ai/model-routing.md)
- [Governanca](docs/ai/review-governance.md)
