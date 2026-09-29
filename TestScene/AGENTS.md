# Cena "TestScene" (AvaloniaCad)

Esta pasta é uma cena do AvaloniaCad: um projeto C# comum que descreve um modelo 3D paramétrico.
O app AvaloniaCad observa esta pasta e redesenha o modelo sempre que um arquivo muda.

## Estrutura
- `Cena.cs`: a cena (top-level statements). Registra cada feature com `AddFeature(variavel)`.
- `Pecas/`: classes e funções reutilizáveis usadas pela cena. Arquivos aqui NÃO podem ter top-level statements.
- `.cad/api.md`: referência completa da API disponível (gerada pelo app; não edite).
- `.cad/`: arquivos gerados pelo app. Não edite.

## Regras
- Unidades em milímetros; o eixo Z aponta para cima.
- Use apenas os tipos de `.cad/api.md` e da biblioteca padrão do .NET. Não use System.IO, rede, threads ou reflexão.
- Toda feature visível deve ser registrada com `AddFeature(...)`. Dê a cada feature `Name` igual ao nome da variável.
- Ferramentas de operações booleanas (ex.: o cilindro que fura) usam `IsTransientTool = true` e também são registradas.

## Peças paramétricas
- Peças reutilizáveis são `record`s em `Pecas/` que herdam de `Part`; os parâmetros são o construtor primário.
- `Build()` devolve a geometria em coordenadas locais (origem da peça em 0,0,0) e não chama `AddFeature`.
- Na cena: `var p = Add(new MinhaPeca(...), at: Placement.At(x, y, z));`. Variantes: expressão `with` do C# sobre `p.Part` (ex.: mudar só a espessura).
- Pontos da peça no mundo: `p.World(peca => peca.AlgumPonto)`.
- Exemplo completo em `.cad/api.md` (seção PEÇAS PARAMÉTRICAS).

## Verificação (obrigatória depois de qualquer edição)
1. `dotnet build` — garante que o código compila (tipos e referências entre arquivos).
2. `"C:\Users\vinic\source\repos\AvaloniaCadEditor\AvaloniaCadEditor.UI\bin\Debug\net10.0-windows\AvaloniaCadEditor.UI.exe" check . --geometry` — executa a cena e valida a geometria no kernel.

Os dois precisam terminar com código de saída 0. Os erros vêm no formato `arquivo(linha,coluna): mensagem`;
corrija e repita até passar.
