# Copilot Surface Map

Use a superficie que melhor combina autonomia, risco e necessidade de validacao.

## Superficies

- VS Code Ask Mode: duvidas rapidas, leitura de codigo, explicacoes, comparacao com specs e perguntas sem alteracao.
- VS Code Plan Mode: demandas medias ou grandes, definicao de escopo, desenho tecnico, riscos, validacoes e divisao em slices.
- VS Code Agent Mode: implementacao local acompanhada, debugging, criacao de testes e tarefas que precisam rodar comandos locais.
- Copilot Coding Agent no GitHub: issues bem definidas, tarefas isoladas, draft PR e execucao em sandbox com checks.
- Copilot CLI: tarefas de terminal, scripts, automacoes, busca em arquivos e validacoes locais.
- Copilot Code Review: primeira revisao automatizada de regressao, seguranca, padroes e escopo antes da revisao humana.

## Matriz

| Tipo de tarefa | Melhor superficie | Por que | Riscos | Validacao esperada |
|----------------|-------------------|---------|--------|--------------------|
| Explicar codigo ou spec | Ask Mode | Baixo custo e sem alteracao | Resposta fora de contexto | Conferir arquivos citados |
| Planejar feature | Plan Mode | Permite separar escopo e riscos | Plano amplo demais | Revisar specs e aceite |
| Implementar slice local | Agent Mode | Pode editar, testar e iterar | Mudancas fora de escopo | `dotnet build`, `dotnet test` conforme risco |
| Corrigir bug reproduzivel | Agent Mode | Permite hipotese, teste e correcao | Corrigir sintoma | Teste de regressao |
| Issue pequena e isolada | Coding Agent | Bom para draft PR assinado por checks | Contexto incompleto | CI e revisao humana |
| Script ou comando shell | Copilot CLI | Feedback rapido no terminal | Comando destrutivo | Dry-run ou revisao do script |
| Revisao de PR | Code Review | Achados iniciais padronizados | Falso positivo/negativo | Revisao humana final |
| Mudanca de auth, banco ou provider | Plan Mode + Agent Mode + humana | Risco alto e contratos sensiveis | Quebra de seguranca ou dados | Specs, ADR, testes, build, revisao humana |

## Regra pratica

Comece com leitura ou planejamento quando o escopo estiver incerto. Use Agent Mode para executar localmente. Use Coding Agent apenas quando a issue ja estiver pequena e verificavel.
