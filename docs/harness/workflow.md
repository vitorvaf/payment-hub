# Agent Workflow

Todo agente deve seguir este ciclo:

```text
1. Discovery
2. Understanding
3. Plan
4. Implementation Slice
5. Validation
6. Evidence
7. Harness Learning
```

## 1. Discovery

- Ler `AGENTS.md`.
- Ler os documentos relevantes em `docs/harness`.
- Ler a spec relacionada em `docs/specs` antes de alterar código.
- Ler ADRs em `docs/adr` quando a tarefa tocar decisões arquiteturais.
- Inspecionar a estrutura atual do repositório.
- Identificar stack, padrões, testes existentes e arquivos relacionados.

## 2. Understanding

- Explicar o objetivo da tarefa com as próprias palavras.
- Separar escopo de fora de escopo.
- Identificar riscos técnicos, de segurança e de produto.
- Confirmar decisões já registradas no harness antes de propor novas.

## 3. Plan

- Criar um plano curto antes de alterar muitos arquivos.
- Dividir a tarefa em pequenos slices.
- Listar arquivos provavelmente envolvidos.
- Informar validações planejadas.

## 4. Implementation Slice

- Implementar uma mudança pequena e coesa por vez.
- Respeitar Clean Architecture e limites de camada.
- Evitar mudanças amplas sem necessidade.
- Não implementar domínio de pagamento se a tarefa for apenas de harness.

## 5. Validation

- Executar os comandos aplicáveis em `docs/harness/validation.md`.
- Se algum comando não puder rodar, explicar claramente o motivo.
- Ajustar a validação ao escopo real da mudança.

## 6. Evidence

- Listar arquivos criados ou alterados.
- Resumir comandos executados e resultados.
- Explicar riscos residuais.
- Registrar decisões novas quando necessário.

## 7. Harness Learning

- Atualizar `docs/harness/learnings.md` quando a tarefa revelar um padrão, limitação, decisão ou armadilha relevante para próximos agentes.
- Não registrar ruído operacional sem valor futuro.

## Regras obrigatórias

- Não sair codando sem analisar a estrutura.
- Não alterar muitos arquivos de uma vez sem plano.
- Dividir mudanças em pequenos slices.
- Sempre listar arquivos alterados.
- Sempre explicar riscos.
- Sempre executar ou indicar comandos de validação.
- Sempre registrar aprendizados relevantes em `docs/harness/learnings.md`.
- Se não conseguir rodar algo, explicar claramente o motivo.
