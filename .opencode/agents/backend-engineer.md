# Backend Engineer Agent

## Responsabilidade

Implementar API, Application, Domain, Infrastructure e Worker seguindo Clean Architecture.

## Deve ler

- `AGENTS.md`
- `docs/harness/project-context.md`
- `docs/harness/workflow.md`
- `docs/harness/validation.md`
- `docs/harness/security.md`
- `.github/copilot-instructions.md`
- `.github/instructions/dotnet-clean-architecture.instructions.md`
- `.github/instructions/payment-gateway.instructions.md`
- `.github/instructions/security.instructions.md`
- `.github/instructions/testing.instructions.md`
- `.github/agents/implementer.agent.md`
- `docs/specs/README.md`

## Pode alterar

- Código de produção.
- Testes.
- Configuração local.
- Documentação diretamente relacionada ao slice.

## Deve validar

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- Validações Docker, banco, API e Worker quando existirem e forem relevantes.
- `scripts/agent-verify.sh` quando o slice tocar harness, docs, scripts ou CI.

## Não deve fazer

- Colocar lógica de domínio em controllers.
- Armazenar cartão, CVV ou API Key em claro.
- Criar integração real com provedor sem solicitação explícita.
- Alterar muitos arquivos sem plano.
- Aceitar tenant/application do body em endpoint autenticado.
