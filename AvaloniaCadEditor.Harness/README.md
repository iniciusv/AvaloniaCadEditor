# AvaloniaCadEditor.Harness

Este módulo reúne o harness de ferramentas da IA: o contrato e o registro de ferramentas, o laço
de agente, os papéis (Editor e Inspetor), a vista em texto (HLR em CPU), as cotas e o protocolo de
ferramenta em texto.

O plano completo está em [docs/planos/2026-10-16-ia-ferramentas-tool-calling.md](../docs/planos/2026-10-16-ia-ferramentas-tool-calling.md).

## Regras de dependência

```
UI (composition root) ─┐
PromptEval ────────────┼──► Harness ──► Application, Drafting, Analysis, Domain, AiCore
Harness.Tests ─────────┘
Application ──X──► Harness / Drafting
Domain, Drafting, Analysis, AiCore, Infrastructure ──X──► Harness
```

- **Só o Harness conhece o Harness.** Nenhum projeto do app o referencia; quem o liga é o
  composition root (UI) ou o PromptEval.
- **O Harness não referencia Infrastructure nem UI.** O kernel (`IGeometryKernel`) e o avaliador
  (`ISceneEvaluator`) chegam pelas portas do Application.
- Essas regras são verificadas por [ArchitectureTests](../AvaloniaCadEditor.Harness.Tests/Architecture/ArchitectureTests.cs).

## Regra de promoção (YAGNI)

Um algoritmo nasce aqui, mesmo que seja genérico (o HLR, por exemplo). Ele só sobe para
Domain ou Drafting quando o app ganhar um **segundo consumidor**, como uma folha 2D com linhas
ocultas.

Nos projetos do app só entram dois tipos de mudança, e cada uma vai em um commit separado:
- mudanças com valor próprio para o app, como os perfis de endpoint, que permitem usar um modelo
  local no chat de hoje;
- extrações sem mudança de comportamento, como uma interface nova ou uma classe que passa de
  `internal` a `public`.

## Organização

| Pasta | Conteúdo |
|---|---|
| `Agents/` | Laço de agente (`tool_calls` → executa → reenvia) e limites de passos e tempo |
| `Roles/` | Papéis, configuração própria (`harness_settings.json`) e resolução de papel → `LlmOptions` |
| `Tools/` | `ICadTool`, o contexto do turno (cópia de trabalho + `Revision`), o registro e as ferramentas em `Query/`, `Edit/` e `Verify/` |
| `Inspection/` | Avaliação da cópia de trabalho com cache, resolução de alvo, malha e referencial |
| `Views/` | Base de projeção (vistas padrão e Iso), HLR em CPU e texto da vista |
| `Protocol/` | Fallback ```` ```tool ```` para servidores sem tool calling e a detecção do suporte |

## Boas práticas

- **Ferramentas finas.** Validam os argumentos, delegam a um serviço e formatam o resultado. Não
  há geometria dentro de uma `ICadTool`.
- **Algoritmos puros.** As classes de `Views/` não têm I/O nem estado e são testadas isoladamente.
- **Estado.** Fica no serviço ou no contexto do turno, nunca em campo estático.
- **Texto para a LLM.** Números em `CultureInfo.InvariantCulture` com no máximo 3 casas decimais
  (formato `0.###`) e saída determinística.

## Modelos locais

Os detalhes de cada servidor (chave, tools, campo de raciocínio, contexto) estão em
[docs/referencias/servidores-locais-openai.md](../docs/referencias/servidores-locais-openai.md).

No **Ollama**, o contexto padrão é baixo e não dá para mudá-lo pela API OpenAI. Crie um modelo com
`PARAMETER num_ctx <n>` num Modelfile; sem isso, o histórico é truncado sem nenhum aviso.
