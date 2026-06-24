# Spec Adherence Next Audit

Use este roteiro para a proxima auditoria de aderencia entre specs e codigo. Ele nao altera produto; apenas transforma divergencias em backlog priorizado.

## Escopo

- Comparar `docs/specs/` com `src/`, `tests/`, `docs/api/` e `docs/database/`.
- Confirmar se gaps ja registrados em `docs/audits/` ainda existem.
- Registrar achados em `feature_list.md` ou novo report em `docs/audits/`.
- Evitar correcoes de dominio no mesmo slice da auditoria.

## Areas prioritarias

- `ApplicationClient.WebhookSecret` em repouso.
- Testes de integracao com banco real.
- Smoke/E2E API + PostgreSQL + Worker.
- Contratos de API documentados versus controllers atuais.
- Inbox/Outbox e retries versus specs `006` e `007`.
- Regras de seguranca da spec `011`.

## Procedimento

1. Leia `AGENTS.md`, `docs/specs/README.md` e `docs/harness/security.md`.
2. Leia os reports existentes em `docs/audits/`.
3. Para cada spec, colete evidencia no codigo ou teste.
4. Classifique cada gap por severidade, risco e tamanho.
5. Atualize `feature_list.md` com itens que nao serao corrigidos imediatamente.
6. Rode `scripts/agent-verify.sh`.

## Saida esperada

- Report curto em `docs/audits/`.
- Backlog atualizado em `feature_list.md`.
- Nenhuma mudanca de produto misturada na auditoria.
