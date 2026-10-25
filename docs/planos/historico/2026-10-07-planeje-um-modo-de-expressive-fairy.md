# Revisão do modo wireframe — problemas encontrados e correções

## Contexto

Revisão crítica das mudanças não commitadas do modo wireframe (feitas nesta sessão). Elas compilam e
os testes novos passam, mas a revisão achou **dois problemas reais** e **um vazamento de
responsabilidade** na arquitetura. As três coisas têm a mesma correção. E o modo **nunca foi visto
rodando**, então os shaders ainda são um risco.

**Princípio pedido:** o wireframe é uma mudança **só visual**. Nada que o resto do software usa
(malha, cache, tesselador, kernel, fillet, seleção) pode mudar por causa dele. A correção abaixo
segue isso: tudo que o wireframe precisa é calculado e guardado dentro do próprio render pass.

Seleção e clique **nunca foram afetados**. O pick é raycast contra os triângulos
(`SceneMeshManager.Raycast3D`) e nenhum código de seleção lê dados de aresta: fora do render, só o
`OcctFilletBuilder` usa `FeatureEdges`.

## Veredito

**Bom, manter:**
- Pass separado (`WireframeRenderPass : IRenderPass`) registrado como o ARCHITECTURE manda; o pass
  sombreado só ganhou um `return` antecipado.
- `SolidRenderables` tirou do pass sombreado a lógica de quem entra no frame, qual malha, cor e
  transformação, e agora os dois modos compartilham isso. O refactor preserva o comportamento.
- `IsWireframe` fica fora de `TessellationSettings`, então não entra no `GeometryKey` (§5.7).
- `WireframeGeometry` monta os vértices de linha sem GL, por isso é testável. O pass restaura o
  estado GL que altera.

**Problemas:**

1. **Peças STL importadas somem no wireframe.** `ImportedMeshFeature` devolve uma malha sem
   `FeatureEdges` ([StlService.cs:71](AvaloniaCadEditor.Infrastructure/IO/StlService.cs#L71)). O modo
   sombreado desenha as faces dela; o wireframe só desenha arestas, então a peça fica invisível.
2. **Custo de memória pago mesmo sem usar o modo.** `InternalNormals1/2` acrescenta 24 B por aresta
   interna (~1,5 por triângulo) a **toda** malha. Isso se repete em várias cópias: no cache
   `_meshByGeometry` do kernel; no `_featureMeshCache`, porque `BuildSolidMesh([f])` passa por
   `Combine`, que copia ([SceneMeshManager.cs:74](AvaloniaCadEditor.Application/Services/SceneMeshManager.cs#L74));
   e em cada transformação de `PartInstance`. Dá algo como +50–90% de memória de malha, mesmo para
   quem nunca liga o wireframe.
3. **Responsabilidade vazada para o Domain.** Uma necessidade só do render tocou `TriangleMesh`,
   `MeshTransform`, `Combine`, `OcctTessellator`, `PureMathGeometryKernel` e `OcctFilletBuilder`. A
   tupla posicional de 7 elementos do `EdgeDetector.ClassifyEdges` é frágil: cada saída nova quebra
   todos os chamadores. O alinhamento de `InternalNormals` com `InternalEdges` depende de toda malha
   combinada ter ou não ter as normais, o mesmo ponto frágil que `FeatureNormals` já tinha.

**Menores (registrar, não necessariamente mudar):**
- `SolidFeatureRenderPass` e `WireframeRenderPass` checam `IsWireframe` cada um por conta própria;
  a regra "só um deles desenha" fica espalhada pelas duas classes.
- O `gl.Clear(DepthBuffer)` no fim do wireframe é um acoplamento com os passes seguintes (grade e
  esboço deixam de ser ocultados pelos sólidos). Está comentado e é intencional.
- O buffer de linhas é enviado duas vezes por frame (ocultas e visíveis), e o prepasse reenvia
  posições e índices a cada frame. É o mesmo padrão do pass sombreado, que é mais caro.
- Polygon offset de 2 em faces quase de perfil empurra muito a profundidade; perto da silhueta, uma
  linha atrás dessa face pode sair contínua. É limitação conhecida da técnica.

## Correção: calcular as normais sob demanda, só no wireframe

Em vez de toda malha carregar `InternalNormals`, o `WireframeGeometry` roda o `EdgeDetector` na
primeira vez que uma malha aparece no wireframe e guarda o resultado na `ConditionalWeakTable` que já
existe. Isso resolve os três problemas de uma vez: STL ganha arestas (o detector solda os vértices
duplicados), malhas não usadas no modo não pagam nada, e o Domain volta ao que era.

1. **`EdgeDetector`** ([EdgeDetector.cs](AvaloniaCadEditor.Domain/Mesh/EdgeDetector.cs)): devolver um
   `public sealed record EdgeClassification(FeatureEdges, InternalEdges, FeatureNormals1,
   FeatureNormals2, FeatureConvex, InternalNormals1, InternalNormals2)` com um
   `Deconstruct(out 5)` para os cinco campos originais. Assim os chamadores existentes
   (`var (a, b, c, d, e) = ClassifyEdges(...)`) compilam **sem mudança**. Remover `Flatten`.
2. **Reverter para o HEAD** (`git checkout --`): `TriangleMesh.cs`, `MeshTransform.cs`,
   `OcctTessellator.cs`, `PureMathGeometryKernel.cs`, `OcctFilletBuilder.cs`,
   `TriangleMeshCombineTests.cs`. Em `EdgeDetectorTests.cs`, voltar a deconstrução de 5 e escrever o
   teste novo com as propriedades nomeadas.
3. **`WireframeGeometry.BuildLineVertices(TriangleMesh mesh, float creaseAngle)`**: chama
   `ClassifyEdges(mesh.Positions, mesh.Indices, creaseAngle)` e monta tudo do resultado: quinas
   sempre desenhadas, internas com diedro > 0,5° como candidatas a silhueta. As quinas saem iguais a
   `mesh.FeatureEdges` (mesmo algoritmo, mesma entrada); para STL, elas passam a existir.
4. **`WireframeRenderPass`**: passa `context.Tessellation.CreaseAngle` para a fábrica do cache.
   Quando o modo for desligado, limpa a `ConditionalWeakTable` (`Clear()`) para não segurar memória.
   Uma mudança de `CreaseAngle` já gera malhas novas (ele está no `GeometryKey`), então o cache por
   instância de malha continua coerente.
5. **ARCHITECTURE.md**: na linha do wireframe, trocar "silhueta depende de
   `TriangleMesh.InternalNormals1/2`" por "silhueta calculada sob demanda via `EdgeDetector`, só para
   malhas exibidas no modo".

Tamanho: a correção não reduz código. O saldo fica em **cerca de +20 linhas**, porque reverter o
`PureMathGeometryKernel` devolve as ~35 linhas de concatenação manual que o `Combine` tinha
substituído. Isso foi decidido: a simplificação fica fora deste pacote (pode ser um commit separado
depois).

Custo da troca: na primeira vez que o modo é ligado, cada malha visível roda o `EdgeDetector` uma vez
na thread de render. É uma fração do tempo da tesselação, que já roda o mesmo detector. Depois disso
fica em cache.

## Verificação

- `WireframeGeometryTests`: ajustar à assinatura nova e acrescentar uma **malha sem arestas
  pré-calculadas** (estilo STL, triângulos com vértices duplicados) que precisa gerar quinas.
  Continuam valendo os casos da caixa (sem candidatas) e do cilindro (só as geratrizes).
- `EdgeDetectorTests`: o teste das normais internas, agora via `EdgeClassification`.
- `dotnet test` com `-p:BaseOutputPath=...`: esperar só as 5 falhas conhecidas
  (3 `CheckCommandProcessTests` do atalho de bin e 2 `ConexaoYAdaptador5mmTests`, que já falham no HEAD).
- `git diff --stat` no fim: `TriangleMesh`, `MeshTransform`, `OcctTessellator`, `PureMath`,
  `OcctFilletBuilder` e `TriangleMeshCombineTests` **fora** do diff.
- **Visual, ainda pendente e o maior risco**: rodar o app e conferir caixa em Iso (9 contínuas e 3
  tracejadas), cilindro em Frente com Ortho (silhuetas), um STL importado visível no modo, e o log
  sem `WIREFRAME ... SHADER ERROR`.
