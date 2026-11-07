# Modelar dentro da peça: duplo clique abre a peça e o Extrude grava no `Build()`, em coordenadas locais

**Status:** Em andamento: entregas 1 e 2 implementadas e conferidas por teste na cena real, falta só a verificação no app (etapa 5) · **Data:** 2026-11-07 · **Branch:** `feature/modelar-na-peca-isolada`

## Objetivo

Trazer para o app o modelo de edição por níveis, à maneira dos grupos do SketchUp:

| Gesto | Efeito |
|---|---|
| **Duplo clique** numa entidade | entra nela: muda o que se seleciona e **para onde as ferramentas gravam** |
| **Esc** | sai um nível |
| **H** | só visibilidade: esconde ou mostra o resto, dentro ou fora de uma entidade |

Dentro de uma peça (`PartInstance`), as ferramentas gravam no `Build()` do record, em coordenadas
locais da peça. Hoje isso exige dois passos: extrudar no mundo e depois "Incorporar à peça". O app
passa a saber *onde* está modelando e converte mundo → local num ponto só, em vez de cada ferramenta
resolver por conta própria.

## Decisões (com o usuário, 2026-11-07)

1. **O contexto nasce do duplo clique**, não do H. O duplo clique já abre um "grupo"
   (`CadViewModel.ActiveBooleanGroupName`), e esse modo é ampliado. O H não muda de sentido.
2. **No nível da cena só se selecionam sólidos inteiros.** Faces e arestas, só dentro de uma entidade
   aberta. Para empurrar uma face, entra-se antes. (Entrega 2.)
3. **Entrar num sólido solto** muda a seleção, mas o referencial continua o mundo, porque o código dele
   está na `Cena.cs` em coordenadas do mundo. Numa peça, o referencial é o local.
4. **Níveis aninhados** (entrar numa sub-peça `.Instance(...)` dentro do `Build()`) ficam para depois.
5. **Peça com várias instâncias:** editar uma muda todas. O aviso fica visível **enquanto** a peça está
   aberta, no letreiro do grupo, e não só depois de gravar.
6. **O modo vem do sinal da altura:** positivo → `PartMergeMode.UnionAfterCuts` (os furos que a peça já
   tem não apagam o material novo); negativo → `PartMergeMode.SubtractAsTool`. É a mesma regra do
   `Push/Pull` de hoje.
7. **O caminho é composto, não novo:** o sólido é gerado *autônomo*, posto na cena **só em memória**,
   incorporado pelo `IPartMerger` e conferido pelo `PartMergeCheck`. Só então é gravado. A `Cena.cs`
   não muda.
8. **Recusa do merger não perde trabalho:** o sólido é gravado solto na cena, como hoje, e a barra de
   status diz por quê.
9. **Antes de gravar, o arquivo da peça vira o ativo no editor.** O histórico do `Ctrl+Z` é do arquivo
   ativo (`SceneScriptViewModel._history`). Sem isso, a mudança na peça não teria desfazer, como já
   acontece hoje no "Incorporar à peça". De quebra, o editor mostra o código da peça que está sendo
   editada.
10. **Nenhuma mudança no Domain.** Evita o problema do app aberto compilando a cena contra o
    `Domain.dll` antigo.
11. **A grade continua no `PartFrame`**, porque as cotas dependem do octante positivo. Só a leitura de
    coordenadas na barra de status passa a usar o referencial do `Build()` (X, Y e Z).

## Situação atual

### Toda geometria nova nasce solta, no mundo, na `Cena.cs`

```
ExtrudeCommand.OnMouseDown / OnValueEntered
  → FeatureCodeGenerator.GenerateExtrusionCode(profile, height, sketch)   literais em coordenadas do MUNDO
  → evento CodeGenerated
  → CadViewModel.CurrentTool_CodeGenerated
  → SceneScriptViewModel.AppendCode                                       sempre no arquivo de entrada
  → RunCodeAsync
```

Para virar parte de uma peça, é preciso o "Incorporar à peça" (`SceneScriptViewModel.MergeIntoPartAsync`).
Ele usa o `IPartMerger` (`RoslynPartMerger`): recorta o sólido da cena, reescreve cada
`Point3D`/`Vector3D` mundo → local pelo papel no construtor, insere no `Build()`, avalia e confere a
geometria com `PartMergeCheck` (caixa, prisma e cilindro pelos vértices).

### O duplo clique já abre um "grupo"

`ViewportInputHandler` (`ClickCount == 2`) liga `ActiveBooleanGroupName` numa `BooleanFeature` solta
ou numa `PartInstance` **que tenha vazios**. Com o grupo aberto:
- os vazios (ferramentas das booleanas) aparecem em vermelho (`ActiveToolFeatures`);
- mover, girar ou espelhar um vazio de peça grava no `Build()`, já em coordenadas locais
  (`CadViewModel.MoveTool_FeaturesMoved` com `owner.Placement.Inverse()`);
- o raycast (`CadViewModel.RaycastScene`) **atravessa o próprio sólido do grupo**, para alcançar os vazios;
- o Esc fecha o grupo, e um letreiro vermelho ("Editando Grupo Booleano") mostra o nome.

### O que a sondagem (etapa 0) mostrou

| Pergunta | Resposta |
|---|---|
| Que perfil sai do clique numa face de peça? | `FaceProfile(instância, plano, malha)` (`ViewportInputHandler.BuildFaceProfile`). `ParametricBoxFace`/`ParametricCylinderFace` só para caixa e cilindro soltos |
| O que o gerador faz com ele? | retângulo → `BoxFeature` autônomo (serve); círculo → `CylinderFeature` + `BooleanFeature("Push/Pull", ..., <variável da instância>)` (o merger recusa: lê variável da cena); polígono → prisma, que pode inferir um `LinkedWorkPlane` + `FindFace` (idem) |
| Altura negativa sem `Push/Pull`? | funciona: o kernel extruda em `normal * altura` e orienta a face pelo sentido (`BuildExtrude`); a caixa é normalizada no gerador |
| O `Ctrl+Z` desfaz o arquivo da peça? | só se ele for o arquivo ativo (`SetDocumentText`). Daí a decisão 9 |
| O Extrude acerta a peça com o grupo aberto? | não: o raycast atravessa o sólido do grupo. Com o Extrude ativo, ele passa a acertar **só** o sólido do grupo |

## Arquitetura (entrega 1)

```
duplo clique numa PartInstance
  → ActiveBooleanGroupName = nome da instância          (qualquer peça, não só as com vazios)
  → CadViewModel.EditContext = PartEditContext.For(...)  (refeito por nome a cada execução)
  → Extrude: Target = Standalone                         (gera o sólido sem ler variáveis da cena)
  → ExtrudeCommand dispara SolidForPartRequested(code, isCut)   em vez de CodeGenerated
  → SceneScriptViewModel.InsertSolidIntoPartAsync(context, code, mode)
  → PartSolidInserter (Application):
        1. cena + trecho, só em memória
        2. avalia → o sólido solto (referência da conferência) e o Placement atual da instância
        3. IPartMerger.Merge(linha do AddFeature do trecho, Placement, modo)
        4. documentos originais + só o arquivo da peça alterado → avalia → PartMergeCheck.Verify
        5. devolve a mudança da peça, ou a recusa
  → ativa o arquivo da peça → ApplyExternalChangesAsync → RunCodeAsync
```

| Peça nova ou alterada | Camada | Papel |
|---|---|---|
| `Application/EditContext/PartEditContext.cs` | Application | record puro: instância, tipo, `Placement`, número de instâncias da mesma peça |
| `Application/EditContext/PartSolidInserter.cs` | Application | passos 1 a 5; depende só de `ISceneEvaluator` e `IPartMerger` |
| `Application/Services/FeatureCodeGenerator.cs` | Application | `ExtrusionTarget.Standalone`: sem `Push/Pull`, sem `LinkedWorkPlane`/`FindFace` |
| `Application/Commands/ExtrudeCommand.cs` | Application | `Target` e evento `SolidForPartRequested` (padrão do `FilletTool.PartFilletRequested`) |
| `UI/ViewModels/SceneScriptViewModel.cs` | UI | `InsertSolidIntoPartAsync`: delega, ativa o arquivo da peça e aplica; na recusa, `AppendCode` |
| `UI/ViewModels/CadViewModel.cs` | UI | `EditContext`, roteamento do Extrude, raycast no grupo, letreiro e coordenadas locais |
| `UI/Controls/Input/ViewportInputHandler.cs` | UI | duplo clique abre qualquer `PartInstance` |

## Etapas

### Entrega 1: abrir a peça e extrudar dentro dela

- [x] **0. Sondagem** (resultados acima).
- [x] **1. Gerador autônomo:** `ExtrusionTarget.Standalone` em `GenerateExtrusionCode`. Testes: nenhuma
      saída contém `Push/Pull`, `FindFace` nem a variável de origem.
- [x] **2. `PartEditContext` + `PartSolidInserter`**, com testes em `Tests/Scenes/PartSolidInserterTests.cs`
      (avaliador e merger reais, como `PartSolidMergeTests`):
  - [x] peça só transladada: `BoxFeature` local no `Build()`, mesma geometria no mundo, cena intacta;
  - [x] peça girada (180° em Z; 90° em X): coordenadas e direções reescritas;
  - [x] altura negativa: entra como ferramenta da diferença;
  - [x] face circular: `CylinderFeature` local;
  - [x] prisma num `WorkPlane` inclinado: plano reescrito;
  - [x] peça declarada na `Cena.cs`: recusa com o motivo do merger;
  - [x] duas instâncias: aviso de que ambas mudam.
- [x] **3. UI:** duplo clique em qualquer peça; `EditContext`; Extrude roteado; raycast só no sólido do
      grupo com o Extrude ativo; letreiro "Editando peça: Tipo · n instâncias"; coordenadas locais na
      barra de status; arquivo da peça ativo antes de gravar.
- [x] **4. Documentação:** `ARCHITECTURE.md` (§3.2 e §4) e status no `docs/README.md`.
- [ ] **5. Verificação.** A parte sem janela já foi feita, em
      `Tests/Scenes/PartSolidInserterAiCoreTests.cs`, sobre a cena AiCore em memória:
  - [x] `placa_suporte_carrinho_y_a` (girada 180° em Z): para fora e para dentro, com as medidas locais certas;
  - [x] `extrusao_2020_trilho_z_a` (deitada 90° em X): idem;
  - [x] tempo: avaliação da cena entre 0,13 e 0,26 s, já aquecida (2,5 s na primeira); inserção entre 0,65
        e 1,65 s. Depois vem a reexecução normal, `RunCodeAsync`. Aceitável para uma extrusão; não é
        preciso otimizar antes de ampliar para outras ferramentas;
  - [ ] no app (pedir permissão antes de abrir): o clique de verdade na face, o `Ctrl+Z` e o letreiro.

Notas da implementação:
- A sondagem na AiCore mostrou que a `Extrusao2020` tem 19 instâncias, cada uma com seu `Comprimento`. Um
  sólido gravado no topo de uma delas fica fora do lugar nas outras. O aviso passou a dizer quais
  parâmetros diferem (`PartMergeWarnings.DifferingParameters`), tanto depois de gravar quanto no letreiro,
  com a peça aberta ("19 instâncias: todas mudam, mas com Comprimento diferente"). O espelho por parâmetro
  continua com aviso próprio.
- A peça girada 90° em X não entrou nos testes automáticos; a rotação em Z sim, e a reescrita em
  qualquer eixo é a do merger (`CaixaComPecaGirada90_GanhaDirecoes`, `WorkPlaneDeDoisArgumentos_...`).
  Fica para a verificação manual.
- O trecho entra na cena composta por `PartSourceEditor.InsertTopLevelStatement`, e não no fim do
  arquivo: um arquivo de entrada que termina em declaração de tipo não compilaria (achado pelo teste da
  peça declarada na cena).
- Os textos do letreiro e do status ficaram no ViewModel, como os demais status do `CadViewModel`, e não
  em `Resources/Strings.cs`.

### Entrega 2: seleção por nível

- [x] No nível da cena, clique e retângulo pegam só sólidos inteiros. O seletor Sólido/Face/Aresta e a
      tecla F saíram; no lugar do seletor, a barra lateral mostra o nível ("Cena: sólidos inteiros",
      "Dentro de X: faces e vazios", "Esboço: linhas"). O tipo de seleção é derivado
      (`CadViewModel.SelectionLevel` → `CurrentSelectionMode`).
- [x] Dentro de uma entidade aberta: faces **dela** e os vazios dela; um vazio ganha do corpo mesmo atrás
      dele. O resto do modelo fica visível, esmaecido (opaco, sem transparência), sem seleção e **com
      snap**. Entrar ou sair limpa a seleção. O H, dentro de uma entidade, isola a própria entidade.
- [x] O Extrude no nível da cena deixa de pegar faces (fica com as regiões do esboço); dentro, só o
      corpo da entidade, nunca um vazio.
- [x] Sólido solto e booleana abrem com duplo clique (decisão 3): mesma seleção, referencial do mundo,
      letreiro "Editando sólido:" / "Editando grupo booleano:".
- [x] Raio de **referência** (`CadViewModel.RaycastReference`) separado do de seleção: o duplo clique (passar
      de uma entidade aberta direto para outra), o plano de trabalho tirado de uma face e o `BooleanTool`
      continuam vendo a cena toda.
- [x] Se a entidade aberta some numa reexecução (apagada ou renomeada no código), volta ao nível da cena
      (`ReapplyOpenEntity`).
- [x] Testes: `SelectionLevelsTests` (regra pura) e `OpenEntityTests` (pelas ViewModels, inclusive o Extrude
      da entrega 1 de ponta a ponta, com projeto em disco).

- [x] **Correção depois do primeiro uso (2026-11-07):** com uma face selecionada, o L (e as outras
      ferramentas de desenho) punha o editor em esboço, e só uma extrusão concluída ou o botão do plano o
      tiravam de lá. O Esc só trocava a ferramenta. Preso no nível `Sketch`, nenhum clique pegava face,
      nem depois de entrar em outra entidade. Ficou assim:
  - o Esc sai um nível: ferramenta → seleção → esboço → entidade → plano XY;
  - entrar em outra entidade (ou voltar à cena) sai do esboço;
  - o esboço fica **por cima** do nível de baixo: o clique que não pega linha nem região cai no que a
    cena ou a entidade aberta pegariam (`SelectionLevels.Beneath`, `CadViewModel.SolidSelectionMode`).
      Testes em `OpenEntityTests` (seção "Esboço por cima do nível").

Notas:
- A regra pura mora em `Application/EditContext/SelectionLevels.cs`; o `CadViewModel` só a consulta.
- A **booleana** com uma entidade aberta ainda escolhe sólidos da cena, como antes: levá-la para dentro da
  peça é a próxima fatia.
- Mover/girar uma **face** selecionada dentro da entidade não foi tratado (as ferramentas de transformação
  continuam agindo sobre sólidos e vazios).

### Entrega 3: níveis aninhados (depois)

- [ ] Duplo clique dentro de uma peça aberta entra na sub-peça; caminho na barra de status
      (Cena › placa_suporte_a › fixacao_1).

## Riscos

- **Instância espelhada:** o merger desiste da reescrita e embrulha em `.Transformed(...)`. Funciona,
  mas o código fica menos editável, e o aviso precisa aparecer.
- **Custo:** três avaliações por extrusão em vez de uma. As peças vêm do `ScenePartsLibrary` em cache,
  mas a peça editada recompila. Medir na etapa 5 antes de ampliar para outras ferramentas.
- **Sinal da altura num esboço:** num perfil desenhado (e não numa face), "negativo = corte" segue a
  normal do plano de trabalho, que pode não apontar para fora da peça. Aceito na entrega 1; revisar se
  confundir na prática.

## Próximas fatias (fora deste plano)

1. **Sweep e Boolean pelo mesmo `PartSolidInserter`.** O operando de fora da peça é copiado para dentro
   como ferramenta local, nunca referenciado.
2. **Referências a outros objetos:** com o resto visível e com snap (entrega 2), o ponto pego vira
   **literal local** (cópia do valor). Liga com
   [Projetar referências](2026-10-17-projetar-referencias-esboco.md).
3. **Vínculo por parâmetro:** quando a referência deve acompanhar a outra peça, o valor vira parâmetro
   do record e a cena passa a medida da outra instância (`TracePartArgument` acha o parâmetro).
4. **Esboço como código dentro da peça aberta:** a aba do esboço
   ([plano de 2026-10-21](2026-10-21-esboco-como-codigo.md)) pertence ao contexto.

A regra que guia as fatias: **forma pertence à peça, posição pertence à cena.** O `Build()` continua
puro (§5.4, §5.6). Uma referência a outro objeto só entra nele como valor recebido.
