# Booleana e Sweep dentro da peça aberta

**Status:** Implementado, falta verificar no app · **Data:** 2026-11-07 · **Branch:** `feature/modelar-na-peca-isolada`

Continua o [plano de modelar dentro da peça](2026-11-07-modelar-na-peca-isolada.md) (próxima fatia 1).
Com uma peça aberta por duplo clique, Cortar, Unir e Sweep gravam no `Build()` dela em coordenadas
locais, como o Extrude já faz.

## Decisões (com o usuário, 2026-11-07)

1. **A base é a entidade aberta.** Com uma entidade aberta, Cortar/Unir já começam pedindo os operandos;
   não se escolhe a base.
2. **Operando sólido solto: é consumido**, como numa booleana comum. Ele sai da `Cena.cs` e entra no
   `Build()` (o "Incorporar à peça", `IPartMerger`). Como isso muda dois arquivos, o `Ctrl+Z` desfaz os
   dois juntos.
3. **Operando que é outra peça (ex.: o motor): é copiado como sub-peça**, `new Tipo(...).Instance("nome",
   at: ...)`, com os parâmetros como literais e o `at:` no referencial local. A peça original fica na
   cena. Nada é referenciado: a cópia não acompanha a original.
4. **Cortar → ferramenta da diferença** (`SubtractAsTool`); **Unir → depois dos cortes**
   (`UnionAfterCuts`), como o Extrude com altura positiva.
5. **Sweep** passa pelo `PartSolidInserter`, como o Extrude: o merger aprende as coordenadas de
   `SweepPath.FromPolyline`, `SweepPath.FullCircle` e do `upReference` da `SweepFeature`.
6. **Entidade aberta que não é peça** (sólido solto ou booleana): a booleana é a de sempre, na cena, e a
   entidade aberta passa a ser o resultado, para não sair dela a cada operação.

## Etapas

- [x] **1. `PlacementSource`** (Application): qualquer `Placement` como código. Formas legíveis quando dá
      (`Translate`, `At(..., rotationZDegrees:)`, `FromAxes` com eixos inteiros); ângulo qualquer sai com
      todas as casas, porque arredondar tiraria os eixos da tolerância do `FromAxes`.
- [x] **2. Merger:** o recorte que é só `var x = new Tipo(...).Instance("x");` ganha o `at:` local em vez
      de ter coordenadas reescritas; a varredura (`SweepPath.FromPolyline`, `FullCircle`, `Arc` e o
      `upReference`) tem as coordenadas reescritas por papel.
- [x] **3. `PartBooleanOperation`** (Application): operandos em sequência, cada um conferido pelo
      `PartMergeCheck`, tudo ou nada. Os parâmetros da peça copiada saem como literais, só os que diferem do
      padrão do construtor. Recusa a peça dentro dela mesma (direta ou por sub-peça), que não terminaria de
      construir.
- [x] **4. `Ctrl+Z` de vários arquivos** (`SceneScriptViewModel.ApplyCrossFileChangesAsync`): vale enquanto
      os arquivos estiverem como a mudança os deixou. O "Incorporar à peça" ganhou o mesmo desfazer.
- [x] **5. UI:** `BooleanTool.LockedBase` (a entidade aberta, pelo nome) e `IntoPart`; `SweepCommand.IntoPart`
      e `SolidForPartRequested`; `CadViewModel.ApplyEditContextToTool` liga os dois; numa entidade que não é
      peça, a booleana nova vira a entidade aberta (`_reopenEntityAfterRun`).
- [x] **6. Testes:** `Tests/Scenes/PartBooleanOperationTests.cs` (sólido consumido, peça copiada em peça girada
      em qualquer eixo, dois operandos, recusas, Sweep, `PlacementSource`) e `OpenEntityTests` (corte de
      ponta a ponta com Ctrl+Z dos dois arquivos; resultado reaberto numa entidade solta).
- [x] **6b. O que a IA vê com uma entidade aberta** (`AiContextRequest.OpenEntity`/`SelectedFace`): lá dentro
      só se selecionam faces, então a IA não sabia em que peça o usuário estava, e o arquivo dela podia nem ir
      no contexto. Agora a entidade aberta entra como seleção, marcada "ABERTA" (numa peça, com o aviso de que
      o pedido vai no `Build()` em coordenadas locais), e a face selecionada vai com normal e centro nas
      coordenadas do código. O lint de editabilidade não acusa nada no que as operações gravam
      (`PartBooleanOperationTests.OCodigoGravadoNaPeca_NaoTemErroDeEditabilidade`).
- [ ] **7. Verificação no app** (pedir permissão): cortar o motor na placa, `Ctrl+Z`, Sweep dentro da peça.

## Limites conhecidos

- Peça copiada com parâmetro que não é valor simples (record, lista) é recusada, com o nome do parâmetro.
- A cópia não acompanha a original: mudar o motor depois não muda o rebaixo. Isso é a fatia "vínculo por
  parâmetro" do plano anterior.
- O Sweep dentro da peça sempre une. Cortar com uma varredura (um canal) fica para depois.
- O `Ctrl+Z` de vários arquivos deixa de valer se um dos arquivos for editado depois; aí o desfazer volta
  a ser o comum, do arquivo ativo.
