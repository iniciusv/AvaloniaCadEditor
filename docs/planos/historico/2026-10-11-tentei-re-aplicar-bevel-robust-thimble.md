# Bevel incompleto no topo do BlocoFicacaoBeltVaoCorreia

## Contexto
Depois da última edição da peça ([AiCore/Pecas/BlocoFicacaoBeltVaoCorreia.cs](AiCore/Pecas/BlocoFicacaoBeltVaoCorreia.cs)), o chanfro de 1 mm na face Z = 17 passou a sair só em parte: falta em trechos das arestas do `boolOp_7b90` (prisma + cylinder_821a) e em parte de uma aresta de cilindro.

A mudança da `FaceReference` não importa: o ponto antigo e o novo estão no mesmo plano Z = 17, e o filtro em [OcctFilletBuilder.cs:94-132](AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs#L94-L132) só olha normal + plano. O que mudou foi a **geometria**:

- `RaioFuroVertical` passou de 3,0 para 3,25, mas o `cylinder_54d2` (rebaixo subtraído do prisma) continuou com `radius: 3` fixo no código. Antes os dois raios eram iguais e a parede do furo era contínua. Agora a parede tem r 3,25 do lado do corpo (X > −5,615) e r 3,0 do lado do prisma (X < −5,615). Isso deixa um degrau de 0,25 mm em X = −5,615 (Y 21,75→22) e uma beirada de 0,25 mm dentro do alojamento da cabeça do M3.
- O vão agora tem 6,5 mm (2 × 3,25) e é tangente ao furo r 3,25 nas duas paredes (Y 15,5 e 22,0). O prisma também passou a encostar em Y = 22.

O chanfro em booleanas passa pelo caminho por **malha**: um cortador por segmento de aresta, aplicado em sequência sobre a forma que vai sendo cortada. O wrapper do OCCT (OcctNet.Wrapper 0.1.1) não tem chanfro B-Rep nativo, então esse caminho continua sendo o único. Com cortadores de 1 mm sobre detalhes de 0,25 mm e pontos tangentes, a booleana pode falhar. Quando falha, `TryCutKeepingSolid` descarta o corte e aquela aresta fica sem chanfro. Se um corte "passa" mas deixa a forma inconsistente, os cortes seguintes também falham, e por isso só parte das arestas sai chanfrada.

## Abordagem

### 1. Reproduzir e localizar (antes de mudar algo)
Adicionar em [FilletDegenerateCutTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/FilletDegenerateCutTests.cs) um teste que monta a geometria **atual** da peça: vão em Y 15,5…22, furo r 3,25, prisma com v = 3,0 e cylinder_54d2 r 3. Ele aplica o chanfro com `PointOnFace` em Z = 17 e conta triângulos inclinados (reusar `InclinedTriangleCount`) perto de cada aresta do topo: Y = 17,45 do prisma, arco do cylinder_821a, degrau em Y = 18,75, arco r 3 / r 3,25 e contorno externo. O teste deve falhar hoje nos trechos que o usuário viu.

Para confirmar qual etapa descarta cada aresta, instrumentar temporariamente o laço de [OcctFilletBuilder.cs:97-195](AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs#L97-L195), com um contador por motivo: côncava, n1≈n2 (borda ou emenda), fora do plano, exceção no cortador, corte rejeitado em `TryCutKeepingSolid`. A instrumentação sai antes do commit.

### 2. Correção na peça (causa provável)
Em `BlocoFicacaoBeltVaoCorreia.Build()`, amarrar o `cylinder_54d2` ao furo, em vez de deixar valores fixos:
- `globalBaseCenter: new Point3D(FuroVerticalX, FuroVerticalY, FuroVerticalZ)`
- `radius: RaioFuroVertical`, `height: AlturaFuroVertical`

Assim a parede do furo volta a ser um cilindro único, sem degrau nem beirada de 0,25 mm, como era com r 3,0. Também é o certo para o projeto: o alojamento da cabeça do M3 fica com o raio do parâmetro dos dois lados. Atualizar o comentário e o `<summary>`, que falam em "rebaixo de 3 mm".

Respeitar as regras da memória: uma declaração nomeada por sólido, sem loops, e a ordem atual das booleanas.

### 3. Se ainda faltar chanfro depois do passo 2
Usar o contador do passo 1 para decidir:
- **Corte rejeitado ou exceção em cascata**: em `TryCutKeepingSolid`, validar o resultado além da caixa envolvente (a forma ainda é um único sólido com volume próximo do anterior). Se não for, descartar o corte e seguir com a forma anterior, para que um corte ruim não contamine os seguintes.
- **Arestas como borda (n1 = n2) por T-junction ou emenda não fundida**: tratar no `EdgeDetector`/builder pareando segmentos colineares, ou ajustar a geometria da peça (o prisma encosta em Y = 22 e X = −20,615, coplanar com o corpo).

## Arquivos
- [AiCore/Pecas/BlocoFicacaoBeltVaoCorreia.cs](AiCore/Pecas/BlocoFicacaoBeltVaoCorreia.cs): `cylinder_54d2` paramétrico.
- [AvaloniaCadEditor.Tests/BehaviorTests/FilletDegenerateCutTests.cs](AvaloniaCadEditor.Tests/BehaviorTests/FilletDegenerateCutTests.cs): teste de regressão com a geometria nova.
- [OcctFilletBuilder.cs](AvaloniaCadEditor.Infrastructure/OpenCascade/OcctFilletBuilder.cs): só se o passo 3 for necessário.

## Verificação
- `dotnet test --filter FilletDegenerateCutTests --artifacts-path <scratchpad>` (com o app aberto as DLLs ficam travadas; ver memória). O teste novo falha antes do passo 2 e passa depois. Os três testes existentes continuam passando.
- No app: recarregar a cena e conferir que todo o contorno do topo (prisma, arcos e borda externa) ficou chanfrado e que o fundo do vão (Z = 7) continua sem chanfro.
