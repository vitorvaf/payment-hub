# Payment Hub — Timeline de Desenvolvimento

## Objetivo

Registrar a ordem recomendada de implementacao das fases, com prioridade, esforco, risco e dependencias explicitadas.

Legenda de prioridade:

- `P0` — bloqueia o MVP; sem isso nao ha produto
- `P1` — necessario para MVP confiavel e operacional
- `P2` — importante pos-MVP; entrega valor incremental
- `P3` — evolucao futura de produto; depende de decisao de mercado

---

## Tabela de timeline

| Ordem | Phase | Objetivo | Prioridade | Esforco | Risco | Status | Dependencias |
|-------|-------|---------|-----------|---------|-------|--------|-------------|
| 1 | Phase 0 | Produto, arquitetura e fronteiras | P0 | M | LOW | `IMPLEMENTED` | — |
| 2 | Phase 1 | Core domain MVP e API | P0 | L | MEDIUM | `IMPLEMENTED` | Phase 0 |
| 3 | Phase 2 | Primeiro adapter de provider (AbacatePay) | P0 | M | MEDIUM | `IMPLEMENTING` | Phase 1 |
| 4 | Phase 3 | Webhooks externos e internos | P0 | M | MEDIUM | `IMPLEMENTING` | Phase 1 |
| 5 | Phase 7 | Workers, Outbox e processamento assincrono | P1 | M | MEDIUM | `IMPLEMENTING` | Phase 3 |
| 6 | Phase 6 | Seguranca e confiabilidade | P1 | M | HIGH | `IMPLEMENTING` | Phase 1 |
| 7 | Phase 4 | Multi-provider (Stripe + MercadoPago) | P1 | L | MEDIUM | `SPEC_DRAFTED` | Phase 2, Phase 3 |
| 8 | Phase 9 | Relatorios, metricas e observabilidade | P2 | L | LOW | `SPEC_DRAFTED` | Phase 6, Phase 7 |
| 9 | Phase 5 | Painel admin | P2 | XL | MEDIUM | `NOT_STARTED` | Phase 1, Phase 6 |
| 10 | Phase 8 | Conciliacao financeira | P2 | XL | HIGH | `NOT_STARTED` | Phase 4, Phase 7 |
| 11 | Phase 10 | Evolucoes futuras de produto (backlog) | P3 | XL | HIGH | `NOT_STARTED` | Phases 0-9 |

---

## Timeline Decision

### Phase 7 antes de Phase 6 (Ordem 5 e 6 na tabela)

**Decisao:** Phase 7 (Workers, Outbox e Processamento Assincrono) foi posicionada na ordem 5, antes de Phase 6 (Seguranca e Confiabilidade, ordem 6).

**Justificativa:** O gap P1-4 (`NoopApplicationWebhookDispatcher` registrado no Worker host) esta tecnicamente dentro do escopo de Phase 7, mas sua correc¸ao e prerequisito para que o ciclo de webhook interno seja considerado minimamente seguro. Corrigir o dispatcher antes de enrijecer a seguranca evita que a Phase 6 seja validada com um componente no-op no caminho critico.

**Risco associado:** Phase 7 depende de Phase 3, que ainda tem o mesmo gap P1-4. Isso significa que ao iniciar Phase 7, o gap P1-4 ja existe no baseline e e o primeiro alvo (Slice 7-A). Se Slice 7-A for adiado dentro de Phase 7, o gap continua exposto durante toda a Phase 7. Mitigacao: Slice 7-A deve ser o primeiro slice de Phase 7, sem excecao.

**Alternativa considerada:** Colocar Phase 6 antes de Phase 7. Vantagem: todos os gaps de autorizacao e segredos seriam resolvidos primeiro. Desvantagem: o ciclo de outbox permanece com dispatcher no-op durante toda a Phase 6, tornando qualquer teste de outbox irreal. A escolha foi priorizar o dispatcher real para que os testes de Phase 6 possam validar o ciclo completo.

**Esta decisao esta refletida em:**

- Tabela de timeline (ordem 5 = Phase 7, ordem 6 = Phase 6).
- `docs/roadmap/002-phase-status-board.md` (Bloco A de trabalho lista Slice 7-A em segundo lugar, imediatamente apos Slice 6-A).
- `docs/audits/specs-bootstrap-report-2026-06-17.md` (timeline sugerida).

---

## Notas de sequenciamento

### Por que Phase 7 antes de Phase 6?

Phase 7 (Workers) depende de Phase 3 (Outbox baseline) e entrega o dispatcher HTTP real que resolve um gap P1 critico.
Phase 6 (Seguranca) pode evoluir em paralelo, mas o dispatcher real precisa existir antes de considerar o ciclo de vida de outbox como seguro.
Ver secao "Timeline Decision" acima para detalhes da decisao e riscos.

### Por que Phase 6 antes de Phase 4?

Phase 4 (Multi-provider) cria novas surfaces de ataque (novos adapters, novos webhooks externos). Enrijecer autorizacao e protecao de secrets antes de adicionar providers reduz risco acumulado.

### Por que Phase 9 antes de Phase 5?

Observabilidade (Phase 9) entrega instrumentacao que o painel admin (Phase 5) pode consumir. Alem disso, Phase 9 tem esforco menor e risco menor, gerando valor operacional mais rapido.

### Por que Phase 8 no final do bloco P2?

Conciliacao financeira (Phase 8) depende de dados reais de providers em producao. Sem Phase 4 (multi-provider funcional) e Phase 7 (workers confiaveis), os dados de referencia para conciliacao sao incompletos.

---

## Slices recomendados para retomada imediata (2026-06-17)

Com base no estado atual (`IMPLEMENTING` em Phases 2, 3, 6, 7) e nos achados P1 da auditoria:

1. **Slice 6-A**: Enforcement de `TenantStatus.Active` e `ApplicationStatus.Active` no middleware. `[CONCLUIDO 2026-06-17]`
2. **Slice 7-A**: Substituir `NoopApplicationWebhookDispatcher` por dispatcher HTTP real no Worker host.
3. **Slice 6-B**: Corrigir `RegisterProviderAccountHandler` para derivar tenant/application do `ITenantContext`.
4. **Slice 6-C**: Proteger `ApplicationClient.WebhookSecret` em repouso.
5. **Slice 6-D**: Gravar `AuditLog` em acoes administrativas.
6. **Slice 1-IT**: Criar primeira fixture de integracao com Postgres (migrations + indices).
7. **Slice 2-A**: Implementar adapter AbacatePay funcional com validacao de assinatura de webhook.

---

## Arquivos relacionados

- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md`
