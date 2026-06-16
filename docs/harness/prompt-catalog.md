# Prompt Catalog

Use estes prompts como ponto de partida para tarefas recorrentes.

## Discovery inicial do projeto

Leia `AGENTS.md` e `docs/harness/*`. Inspecione a estrutura do repositório, identifique stack, padrões, lacunas e comandos de validação disponíveis. Não implemente nada. Responda com contexto entendido, riscos, perguntas abertas e próximo plano recomendado.

## Nova feature

Implemente a feature descrita abaixo em pequenos slices. Antes de alterar código, faça discovery, liste arquivos envolvidos, proponha plano, confirme fora de escopo, implemente o menor slice útil, rode validações aplicáveis e registre evidências.

## Revisão de arquitetura

Revise a arquitetura da mudança abaixo considerando Clean Architecture, limites de domínio, escopo do MVP, evolução futura para broker e risco de overengineering. Priorize problemas concretos com arquivo/linha quando possível e proponha correções.

## Revisão de segurança

Revise a mudança abaixo contra `docs/harness/security.md`. Procure secrets, logs sensíveis, API Keys em claro, ausência de idempotência, webhooks sem persistência prévia, falta de assinatura e exposição de dados sensíveis.

## Criação de testes

Crie ou ajuste testes para o comportamento abaixo. Priorize testes unitários de domínio, testes de Application para casos de uso e testes de integração quando o comportamento envolver banco, API ou Worker.

## Investigação de bug

Investigue o bug abaixo. Reproduza ou explique por que não foi possível reproduzir, identifique causa provável, proponha correção mínima, valide regressão e registre evidências.

## Criação de ADR

Crie uma ADR usando `docs/harness/adr-template.md` para a decisão abaixo. Inclua contexto, decisão, consequências, alternativas consideradas, status e data.

## Atualização de documentação

Atualize a documentação relacionada à mudança abaixo. Garanta consistência com `AGENTS.md`, `docs/harness/project-context.md`, `workflow.md`, `validation.md` e `security.md`.

## Validação antes de pull request

Revise a branch como preparação para PR. Liste arquivos alterados, valide escopo, execute comandos disponíveis, relate resultados, riscos residuais e aprendizados que devem ir para `docs/harness/learnings.md`.
