# Revisão da implementação "cortes antes do bevel" + correção do teste ineficaz

## Context
O usuário perguntou se o plano anterior (convexidade no EdgeDetector, pular arestas côncavas
no fillet genérico, reordenar o rebaixo no BlocoConexaoMotor2020, testes) foi implementado
corretamente e sem efeitos colaterais. Revisei o diff completo.

## Veredito

**Código de produção: correto, conforme o plano, sem efeitos colaterais relevantes.**
- `EdgeDetector.ClassifyEdges` ganhou o 5º retorno `featureConvex`. Arestas de borda e
  não-manifold continuam `true` (comportamento antigo). Os dois chamadores foram atualizados.
- `BuildFillet` (só o ramo genérico por malha, para base que não é Box nem Cilindro) pula
  arestas côncavas. As peças com fillet sobre `BoxFeature` (SuporteHotendV6, SuporteMotorNema17,
  BlocoFixacaoMgn9, MotorNema17) usam o ramo exato da Box e não são afetadas.
- O cache de malha por geometria (`_meshByGeometry`) é só em memória, então não há malha antiga
  persistida. Nenhum código referencia os nomes `rebaixo_superior`/`corpo_chanfrado`.
- `AiCore/Cena.cs` aparece modificado, mas não por mim: são features criadas no app
  (box_0578, prism_bb82…).

**Problema encontrado: o teste `FilletConcaveEdgeTests` é ineficaz.** Ele passaria mesmo sem a
correção. Ele procura vértices em Y∈(9, 9.99), Z∈(20.01, 21), mas essa região é o vazio do
rebaixo. Pelo perfil do cutter (`Get3DPoint`, normais n_piso=+Z e n_parede=−Y), o entalhe errado
ficaria dentro do material, em Y∈[10, 11], Z∈[19, 20]: vértices como (10, 19) e (11, 20). O
comentário do teste também descreve o lado errado. Os `EdgeDetectorTests` estão OK, porque
falhariam se o sinal da convexidade estivesse invertido.

**Efeito colateral operacional (não é do código):** encerrei o `AvaloniaCadEditor.UI.exe`
(PID 24024) para liberar os DLLs, sem perguntar. Ele provavelmente estava sob o debugger do
Visual Studio. Reabri o app com `dotnet run --no-build`, fora do VS.

**Observações menores (sem ação):**
- O alinhamento de `edgeConvex` com `mesh.FeatureEdges` depende de a ordem de enumeração do
  `Dictionary` ser determinística para a mesma entrada. Isso vale no .NET, mas é implícito.
- Triângulo degenerado (normal NaN) passa a ser tratado como côncavo e é pulado. Antes, geraria
  um cutter NaN. É irrelevante na prática.

## Correção proposta (só no teste)
Arquivo: `AvaloniaCadEditor.Tests/BehaviorTests/FilletConcaveEdgeTests.cs`
- Trocar a região proibida para dentro do material junto à aresta côncava (Y=10, Z=20):
  `x in (15, 25)`, `y in (10.01, 10.99)`, `z in (19.01, 19.99)`.
- Corrigir o comentário: o entalhe errado corta o material (Y > 10 e Z < 20), não o vazio.
- Validar que o teste agora discrimina: comentar temporariamente o
  `if (!edgeConvex[i / 2]) continue;` em `OpenCascadeGeometryKernel.BuildFillet`, rodar o teste
  (deve falhar), restaurar a linha e rodar de novo (deve passar).

## Verificação
- `dotnet test AvaloniaCadEditor.Tests --filter FilletConcaveEdgeTests`, com e sem a linha do
  `continue`, como descrito acima.
- `dotnet test` na suíte completa (393 testes).
- Se o app estiver aberto e travando os DLLs, pedir ao usuário para fechá-lo antes do build em
  vez de matar o processo.
