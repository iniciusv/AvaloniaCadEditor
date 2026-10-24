# Exportar a cena para o SketchUp (COLLADA `.dae`, componentes por peça)

**Status:** Proposto · **Data:** 2026-10-23 · **Branch:** `feature/exportar-collada` (a criar a partir de `master`)

## Objetivo

Levar a cena para o SketchUp **organizada**, em vez da malha única de triângulos do STL:

- cada **peça** (`PartInstance`) chega como **instância de componente**: a geometria vai uma vez por
  definição, e editar uma cópia no SketchUp muda todas;
- as instâncias ficam dentro de um **grupo por tipo de peça** (`SuporteC`, `BlocoFixacaoBelt`…), que faz
  o papel de tag: o COLLADA não tem tags nem camadas que o SketchUp leia;
- cada **sólido solto** da cena chega como um **grupo** na raiz;
- tudo com o nome do código, a cor, a posição certa e em milímetros.

```
cena.dae
├── SuporteC                     ← grupo do tipo (faz o papel de tag)
│   ├── suporte_c_esquerdo       ← instância do componente "SuporteC"
│   └── suporte_c_direito        ← outra instância do mesmo componente
├── BlocoFixacaoBelt
│   └── bloco_fixacao_belt_front
├── mesa                         ← sólido solto: grupo
└── chapa_mdf_mesa
```

O formato é o COLLADA 1.4.1 (`.dae`): o SketchUp o importa sem extensão (*Arquivo → Importar*). Não
usamos o SDK do `.skp`: ele exige cadastro e licença da Trimble e P/Invoke, e não organiza nada melhor.

Requisito de projeto: **código isolado**. O export inteiro tem de caber em uma pasta por camada, atrás
de uma porta, de modo que apagá-lo seja apagar essas pastas e um comando.

## Situação atual

- `ExportStlCommand` ([`CadViewModel.cs`](../../AvaloniaCadEditor.UI/ViewModels/CadViewModel.cs), ~linha 1207)
  chama `GetExportMesh()`: com seleção, junta as malhas dos sólidos selecionados; sem seleção, usa a
  `SolidMesh` composta. Grava pelo `IStlService`
  ([`IStlService.cs`](../../AvaloniaCadEditor.Application/Interfaces/IStlService.cs),
  [`StlService.cs`](../../AvaloniaCadEditor.Infrastructure/IO/StlService.cs)).
- `ISceneMeshManager.GetFeatureMesh(sketch, id, null)` devolve a malha **já em coordenadas do mundo**. Para
  uma `PartInstance`, o kernel transforma a malha da definição pelo `Placement`
  (`OpenCascadeGeometryKernel.BuildFeatureMesh`). A malha **local** da peça, que é o que o componente
  precisa, sai de `GetMeshFor(instance.Definition)`. O kernel a guarda por Id de definição
  (`_definitionMeshCache`), então não constrói de novo.
- Peças com os mesmos parâmetros compartilham a mesma `Definition` (§5.4, `SceneBuilder.GetOrBuildDefinition`).
  O Id da definição é a chave natural do componente.
- O que a tela desenha é `sketch.Features.Where(f => f.IsVisible)` (`SceneMeshManager.BuildCompositeMesh`).
  Ferramentas de booleana ficam escondidas (`IsVisible = false`). Plano de trabalho e entidades de esboço
  devolvem malha vazia.
- `IFeature.Color` é um hex `#RRGGBB` opcional (ex.: `Color = "#A67B50"` em `AiCore/Cena.cs`).
- **Construção assíncrona:** enquanto `ISceneMeshManager.PendingCount > 0`, as malhas ainda em construção
  vêm vazias. Um export nesse momento sairia faltando sólidos, sem erro.
- `Placement` não expõe a matriz, só `Apply`/`ApplyVector`/`Translation`/`IsMirrored`. A matriz 4×4 sai
  das imagens dos eixos unitários, sem mexer no Domain.

## Arquitetura

```
UI         CadViewModel.ExportSketchUpCommand ─ só escolhe o caminho e mostra o status
              │
Application   Export/SceneExportService ─────── junta coletor + porta; recusa com cena pendente
              ├─ Export/SceneExportCollector    Sketch + ISceneMeshManager → ExportScene (puro, testável)
              ├─ Export/ExportScene.cs          DTO neutro: nós, componentes, materiais
              └─ Interfaces/ISceneFileWriter    porta: Task WriteAsync(ExportScene, string path)
                     ▲
Infrastructure   Export/Collada/ColladaSceneWriter (public, implementa a porta)
                 Export/Collada/ColladaDocument  (internal: monta o XML)
                 Export/Collada/VertexWelder     (internal: solda vértices coincidentes)
```

Por que assim:

- **O DTO é neutro** (`ExportScene` não sabe o que é COLLADA). Um segundo formato no futuro (OBJ, glTF) é
  só outro `ISceneFileWriter`, sem tocar no coletor.
- **O coletor fica no Application** porque depende do `ISceneMeshManager`. Ele é o único lugar que decide
  o que vira componente, grupo ou pasta de tipo.
- **O escritor fica no Infrastructure**, ao lado do `StlService` (I/O de arquivo), com tudo `internal`
  menos a classe que implementa a porta.
- **O ViewModel só delega**: não monta XML nem percorre features. Isso não aumenta o god object (§6.3).
- **Sem default de construtor** que instancie Infrastructure (a dívida de §6.2): a composição injeta o
  escritor em `App.axaml.cs`.

Alternativa considerada: um projeto próprio (`AvaloniaCadEditor.Export`). Dá isolamento garantido pelo
compilador, mas custa `.slnx`, §2 do ARCHITECTURE.md e uma seta nova no `ArchitectureTests` por algumas
centenas de linhas. Pastas `Export/` com `internal` bastam. Vale reconsiderar se surgir um segundo formato.

### DTO (`Application/Export/ExportScene.cs`)

```csharp
public sealed record ExportScene(
    IReadOnlyList<ExportNode> Nodes,             // raiz da cena
    IReadOnlyList<ExportComponent> Components,   // definições compartilhadas
    IReadOnlyList<ExportMaterial> Materials,
    IReadOnlyList<string> Skipped);              // nomes sem malha (viram aviso no status)

/// <summary>Geometria de uma peça em coordenadas locais, escrita uma vez.</summary>
public sealed record ExportComponent(string Id, string Name, TriangleMesh Mesh, string? MaterialId);

/// <summary>
/// Um nó é exatamente um destes: grupo com malha (<see cref="Mesh"/>), instância de componente
/// (<see cref="ComponentId"/>) ou pasta que só agrupa (<see cref="Children"/>).
/// </summary>
public sealed record ExportNode(
    string Name,
    double[] Matrix,                // 4x4 linha a linha; identidade para grupos e pastas
    TriangleMesh? Mesh,
    string? MaterialId,
    string? ComponentId,
    IReadOnlyList<ExportNode> Children);

public sealed record ExportMaterial(string Id, string Name, byte R, byte G, byte B);
```

### Coletor (`Application/Export/SceneExportCollector.cs`)

1. **Escolha das features:** com seleção, só as selecionadas; sem seleção, todas com `IsVisible`. É a mesma
   regra do STL, para o botão se comportar igual.
2. **`PartInstance`:**
   - chave do componente = `(instance.Definition.Id, cor)`, com cor = `instance.Color ?? instance.Definition.Color`.
     A cor entra na chave porque, no COLLADA, o material fica preso à geometria do componente, não à
     instância. Duas cópias da mesma peça com cores diferentes viram dois componentes;
   - malha local = `GetMeshFor(instance.Definition)`. Vazia → instância vai para `Skipped`;
   - nome do componente = `PartTypeName`. Se o tipo tiver mais de uma definição (parâmetros ou cores
     diferentes), sai `SuporteC`, `SuporteC_2`…;
   - nó = instância com `Matrix` do `Placement` e o nome da instância;
   - o nó entra na pasta do tipo (`ExportNode` só com `Children`, nome `PartTypeName`). A pasta existe
     mesmo com uma instância só, para a organização ser previsível.
3. **Sólido solto:** `GetFeatureMesh(sketch, f.Id, null)` (já em mundo) → grupo na raiz, com matriz
   identidade. Malha vazia → `Skipped`.
4. **Nomes:** `f.Name` ou `solido_N`. Nome repetido (não deveria, pela §5.2, mas o escritor exige nomes
   únicos) ganha `_2`, `_3`.
5. **Materiais:** um por cor distinta, e um padrão cinza para quem não tem cor.
6. **Matriz do `Placement`:** colunas = `ApplyVector(UnitX/UnitY/UnitZ)`, última coluna = `Translation`.

Sub-peças usadas como operando dentro do `Build()` (`.Instance("nome")`, §5.4) já fazem parte da malha da
definição hospedeira e não viram componentes próprios. Isso basta para o SketchUp.

### Escritor (`Infrastructure/Export/Collada/`)

COLLADA 1.4.1 com `System.Xml.Linq`, sem pacote externo:

- `<asset>`: `<unit meter="0.001" name="millimeter"/>` e `<up_axis>Z_UP</up_axis>`. É o mesmo eixo do
  app, sem trocar Y por Z.
- `<library_effects>` + `<library_materials>`: um `<effect>` lambert com `<diffuse><color>` por material.
- `<library_geometries>`: uma `<geometry>` por componente e por grupo.
  - Antes, `VertexWelder` funde as posições idênticas: a tesselação do OCCT duplica vértices por face.
    Sem a solda, o arquivo fica maior e cada face chega solta, sem arestas compartilhadas.
  - `<source>` de posições (`float_array`), `<vertices>` e `<triangles>` com o `material` do nó.
  - **Sem normais.** O SketchUp recalcula as normais pelas faces, e a solda tornaria as normais por
    vértice erradas nas quinas. Menos código e arquivo menor.
- `<library_nodes>`: um `<node>` por componente, com `<instance_geometry>` + `<bind_material>`. É isso que
  o SketchUp transforma em definição de componente.
- `<library_visual_scenes>`: a árvore de `ExportNode`:
  - pasta de tipo e grupo → `<node>` (o grupo com `<instance_geometry>`);
  - instância → `<node>` com `<matrix>` + `<instance_node url="#componente"/>`.
- `name` leva o nome original (UTF-8, acentos ok). `id` é sanitizado para um `xs:ID` válido e único.
- **Números sempre em `CultureInfo.InvariantCulture`**, formato `0.######`. A máquina é pt-BR:
  `ToString()` puro escreveria vírgula e o SketchUp leria lixo.
- Triângulos degenerados (área ~0 depois da solda) são descartados.

### UI

- `CadViewModel.ExportSketchUpCommand`: `SaveFileAsync("Exportar para SketchUp", "dae", "cena")` →
  `SceneExportService.ExportAsync(Sketch, path)` → `StatusText` com
  "✓ N peças (M componentes) e K sólidos exportados" ou a lista de ignorados.
- Botão ao lado do "Export STL" no `MainWindow.axaml`, mais `ExportSketchUp`/`ExportSketchUpTooltip` em
  `Resources/Strings.resx` e `Strings.cs`. O tooltip lembra de marcar "mesclar faces coplanares" na
  importação.

## Etapas

- [ ] `Application/Export/ExportScene.cs` (DTO)
- [ ] `Application/Interfaces/ISceneFileWriter.cs` (porta)
- [ ] `Application/Export/SceneExportCollector.cs` (componentes, pastas por tipo, grupos soltos)
- [ ] `Application/Export/SceneExportService.cs`: recusa com `PendingCount > 0` ("aguarde a cena terminar
      de construir") em vez de exportar faltando sólidos; gera o XML em `Task.Run`
- [ ] `Infrastructure/Export/Collada/ColladaSceneWriter.cs`, `ColladaDocument.cs`, `VertexWelder.cs`
- [ ] Comando, botão e strings na UI; injeção em `App.axaml.cs`
- [ ] Testes (abaixo)
- [ ] ARCHITECTURE.md: linha na §4 ("Exportar a cena para o SketchUp") citando as pastas `Export/`.
      O `ArchitectureDocTests` confere os caminhos
- [ ] Verificação manual pelo usuário: importar `AiCore` no SketchUp com "mesclar faces coplanares".
      Conferir se as peças repetidas chegam como componente, se as pastas de tipo estão certas e se as
      instâncias **giradas e espelhadas** estão no lugar

## Testes

Em `Tests/Export/` (pasta nova):

- **Coletor** (com `ISceneMeshManager` falso):
  - duas instâncias da mesma definição → um componente e duas instâncias;
  - mesma definição com cores diferentes → dois componentes;
  - duas definições do mesmo tipo → `SuporteC` e `SuporteC_2`, na mesma pasta de tipo;
  - a matriz de um `Placement` girado e transladado leva os pontos locais aos mesmos pontos que
    `Placement.Apply`;
  - sólido solto vira grupo na raiz; ferramenta escondida fica de fora; só os selecionados quando há
    seleção;
  - malha vazia vai para `Skipped`; nomes repetidos ficam únicos;
  - uma cor por material, e sem cor usa o material padrão.
- **Serviço**: com `PendingCount > 0` não chama o escritor.
- **Escritor** (lê o XML de volta com `XDocument`):
  - unidade em mm e `Z_UP`;
  - componente escrito uma vez em `<library_nodes>`, e cada instância com `<instance_node>` + `<matrix>`;
  - `name` original (inclusive com acento) e `id`s únicos e válidos;
  - números com ponto **rodando sob `CultureInfo("pt-BR")`**;
  - o volume da malha relida bate com o da original (a solda não estraga a geometria);
  - a solda de um cubo de 24 vértices (4 por face) dá 8.
- Ponta a ponta opcional: uma peça instanciada duas vezes + um `BoxFeature` solto, pelo kernel real →
  `.dae` → 1 componente, 2 instâncias e 1 grupo, com volumes certos (no estilo de `Tests/BehaviorTests/`).

## Decisões

- 2026-10-23: peças viram **componentes desde a primeira entrega**, sem uma v1 só com grupos.
- 2026-10-23: as tags são simuladas por um **grupo pai por tipo de peça**. Os sólidos soltos ficam na raiz.

## Perguntas em aberto

1. **Modo hierárquico** (subgrupo por sólido interno da peça, ex.: `corpo`, `furo_x`): fica de fora. Só
   faz sentido se o SketchUp for usado para editar o interior das peças.

## Riscos

- **Instância espelhada** (`Placement.IsMirrored`, determinante −1): o SketchUp aceita escala negativa
  em componente, mas é preciso conferir na importação se as faces não saem invertidas. Se saírem, o
  coletor passa a exportar a instância espelhada como grupo com a malha do mundo (`GetFeatureMesh`, que
  já desfaz o espelho), fora do componente.
- **Faces trianguladas no SketchUp:** sem "mesclar faces coplanares" na importação, cada triângulo mostra
  as arestas. Mitigação: a solda de vértices e a dica no tooltip. Se não bastar, juntar os triângulos
  coplanares em polígonos no próprio escritor (`<polylist>`), o que dá bem mais trabalho.
- **Superfícies curvas facetadas:** furos e cilindros chegam como prismas da tesselação. No SketchUp é
  preciso suavizar as arestas (*Suavizar arestas*). Não há como mandar arcos analíticos: o OcctNet.Wrapper
  não expõe a topologia.
- **Cena grande:** a cena da impressora tem centenas de milhares de triângulos. Os componentes reduzem
  o arquivo, mas é preciso medir o tempo na `AiCore` antes de fechar.
- **Export com chanfro suprimido:** se `SuppressFillets` estiver ligado, exporta a versão sem chanfro (é
  o que está na tela). Avisar no status.

## Referências

- Especificação COLLADA 1.4.1 (Khronos), consultada de memória: `asset/unit`, `up_axis`,
  `library_geometries`, `library_nodes`, `library_visual_scenes`, `instance_geometry`, `instance_node`,
  `bind_material`, `matrix`. Antes de executar, resumir em `docs/referencias/collada-1.4.1.md` o que for
  usado.
