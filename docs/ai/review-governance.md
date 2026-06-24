# Review Governance

## Niveis de autonomia

- Leitura apenas: explicar codigo, specs e riscos sem alterar arquivos.
- Comentarios: sugerir diffs, revisar PRs e propor testes.
- Alteracoes locais: editar arquivos no workspace e rodar validacoes.
- Draft PR: preparar mudancas isoladas com checks.
- Automacao com gates: executar tarefas repetiveis apenas com validacao mecanica e revisao humana onde necessario.

## Regras de revisao

- Toda mudanca sensivel precisa de revisao humana.
- Alteracoes em banco exigem plano de migracao e rollback.
- Alteracoes de autenticacao/autorizacao exigem testes.
- Alteracoes cross-repo exigem plano de impacto.
- Nenhum agente deve fazer merge automatico.
- Nao remover testes para fazer build passar.
- Nao mudar contrato HTTP sem atualizar specs e consumidores afetados.

## Regras de seguranca

- Nao expor secrets.
- Nao gravar tokens em arquivos.
- Nao alterar pipelines sem revisao.
- Nao alterar scripts destrutivos sem aprovacao explicita.
- Nao logar credenciais, API Keys, CVV, numero de cartao ou payload sensivel.
- Tratar `docker-compose.yml` como dev-only; valores fake nao podem virar referencia de producao.

## Evidencia minima para PR

- Escopo e fora de escopo.
- Specs/ADRs consultadas.
- Arquivos alterados.
- Testes e comandos executados.
- Riscos residuais.
- Plano de rollback quando houver banco, auth ou provider real.
