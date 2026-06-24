# Model Routing

Use o menor modelo que resolva a etapa atual com confianca, e aumente a capacidade quando houver raciocinio critico, seguranca ou muitas dependencias.

## Modelo economico

Use para:

- Explorar arquivos.
- Buscar referencias.
- Resumir docs.
- Levantar hipoteses iniciais.
- Tarefas simples de Markdown.

Evite para decisoes arquiteturais, seguranca, banco e debugging complexo.

## Modelo balanceado

Use para:

- Implementacao comum.
- Testes unitarios e de application.
- Refatoracoes pequenas.
- Ajustes de API com contrato claro.
- Atualizacao de docs apos codigo.

## Modelo forte

Use para:

- Arquitetura e ADRs.
- Debugging complexo.
- Analise cross-repo.
- Seguranca, auth, secrets e multitenancy.
- Mudancas de banco, migrations e processamento confiavel.
- Decisoes com impacto em contracts, Job Search ou providers.

## Politica

- Comece barato para explorar.
- Use modelo forte para raciocinio critico.
- Volte para modelo balanceado para implementar.
- Use revisao humana para decisoes arquiteturais e mudancas sensiveis.
- Registre a decisao quando a escolha do modelo impactar custo, risco ou prazo.
