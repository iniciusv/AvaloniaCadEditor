# Biblioteca de peças: uma aba na coluna direita com as peças de uso recorrente, fora de qualquer cena

**Status:** Feito: as quatro etapas (ver as seções "Etapa N: como ficou") · **Data:** 2026-11-11 · **Branch:** `claude/confident-maxwell-yk1mdx`

Protótipo clicável da tela: [2026-11-11-biblioteca-de-pecas-prototipo.html](2026-11-11-biblioteca-de-pecas-prototipo.html)
(abra no navegador; cópia publicada em <https://claude.ai/artifact/DouhKq1HUnZSrFS82Wt3Rf>)

## Objetivo

Dar acesso fácil, em qualquer projeto, às peças que se repetem: perfis de alumínio, trilhos e
carrinhos, rolamentos, motores, furos para parafusos e as fixações nesses perfis. Hoje uma peça só
existe dentro da pasta `Pecas/` da cena em que foi escrita; para usá-la em outra cena, copia-se o
arquivo à mão.

## Situação atual

- Peça é um `record` que herda de `Part`, num `.cs` em `Pecas/` da cena. A cena de exemplo (`AiCore/`)
  tem 41, e cerca de 20 são de catálogo (`Extrusao2020`, `TrilhoMgn9`, `CarrinhoMgn15H`, `Rolamento`,
  `MotorNema17`, `FuroRebaixado`, `FuroPorca`...).
- Inserir uma peça é um combo na aba Código (`SceneScriptViewModel.InsertPartAsync`, que usa
  `PartSourceEditor.FindPartTypes` e `BuildAddStatement`). Só lista as peças da cena aberta, sem
  descrição, sem busca e sem parâmetros.
- As peças já trazem o que um catálogo precisa: descrição no `<summary>`, parâmetros com valor padrão
  no construtor, faixas por `[Range]`, e modelos prontos em propriedades estáticas (`Rolamento.F623ZZ`).
- Os "vazios" (`FuroRebaixado`, `FuroPorca`) são peças usadas como operando dentro do `Build()` de
  outra: `new FuroRebaixado(...).Instance("furo_1")`. Recebem ponto e direção, não `Placement`.
- A coluna direita é um `TabControl` com três abas (Modelo, Código, Ferramentas) em `MainWindow.axaml`.

## A tela

Aba nova **Biblioteca**, entre Modelo e Código. De cima para baixo:

1. **Busca**, que olha nome, categoria e descrição.
2. **Origens**, como filtro: *Nesta cena* (`Pecas/` do projeto aberto) e *Minha biblioteca* (a pasta
   única, fora de qualquer projeto). Peças de um projeto específico são uma subpasta dela
   (`Projetos/Impressora 3D/`), não outra pasta.
3. **Lista** agrupada por categoria, cada item com miniatura, nome, uma linha de descrição e uma marca:
   `vazio` (corta uma peça), `na cena` (já copiada) ou `alterada` (a cópia da cena difere da biblioteca).
4. **Detalhe** do item escolhido: descrição, modelos prontos, parâmetros editáveis, o código que será
   gravado e as ações, entre elas **Citar no chat**.

Ações, conforme o item e o nível de edição:

| Item | Na cena | Com uma peça aberta (duplo clique) |
|---|---|---|
| Peça | **Inserir na cena** | Inserir na cena · Cortar da peça aberta |
| Vazio | desligado, com a dica "abra a peça que vai receber o furo" | **Furar a peça aberta**, depois clique na face (ponto → `Face`, normal → `Direcao`) |
| Peça da cena que não está na biblioteca | Enviar para a biblioteca | idem |
| Peça `alterada` | Atualizar da biblioteca · Enviar para a biblioteca | idem |

Na árvore do modelo, o botão direito numa peça ganha "Enviar para a biblioteca".

## Decisões (com o usuário, 2026-11-11)

1. **Inserir copia o arquivo para a cena.** Na primeira vez, o `.cs` da peça e os das peças de que ela
   depende (fecho transitivo dos tipos usados) vão para `Pecas/`. A cena não guarda caminho para a
   biblioteca: pastas podem ser movidas sem quebrar nada, e o `check`, o git, o `AGENTS.md` e uma IA
   externa não precisam conhecê-la. A regra "o disco é a fonte da verdade" (ARCHITECTURE §1) não muda.
2. **A biblioteca é uma pasta única**, com local padrão (`Documentos/AvaloniaCad/Biblioteca`), que o
   usuário pode trocar nas configurações e versionar com git. Não há lista de pastas extras: o que é de
   um projeto vira subpasta. O app não roda git; só não grava nada lá que atrapalhe (o cache de
   miniaturas fica fora da pasta).
3. **Miniaturas na primeira versão.** Ver "Miniaturas" abaixo.
4. **A IA enxerga a biblioteca, a começar pela peça citada.** Ver "A IA e a biblioteca" abaixo.

Propostas do plano, ainda não discutidas:

5. **O catálogo é o próprio código, sem formato novo.** Categoria = subpasta. Descrição = `<summary>`.
   Parâmetros = construtor do record (o `PartTypeInfo` que já existe). Modelos prontos = propriedades
   estáticas que devolvem o próprio tipo. Tudo lido de forma sintática.
6. **Peça × vazio.** A lista precisa saber qual item corta. Proposta: uma propriedade virtual em
   `Part`, no molde de `DrawingLabel`, ou a convenção de pasta (`Furos/`).
7. **Divergência por conteúdo.** A cópia guarda de onde veio num comentário de cabeçalho
   (`// biblioteca: Perfis/Extrusao2020`, caminho relativo à biblioteca, nunca absoluto); "alterada" é a
   comparação do texto dos dois arquivos. Conflito de nome com uma peça diferente já existente na cena:
   recusa com mensagem, não sobrescreve.

## Miniaturas

- **De onde vem a malha:** para cada item, uma cena sintética de uma linha (`Add(new Tipo());`, com os
  padrões do construtor) mais o arquivo da peça e suas dependências, avaliada pelo `ISceneEvaluator` e
  construída pelo kernel em segundo plano, na fila do `SceneMeshManager` (o kernel não é thread-safe).
- **Como vira imagem:** o app não tem renderização fora da tela, e o viewport OpenGL pertence ao
  controle. Proposta: um rasterizador pequeno em CPU (vista isométrica, z-buffer, sombreado plano,
  ~128 px), puro, em `Application/Library/`. Não depende de contexto GL e é testável.
- **Cache:** PNG em disco, fora da biblioteca, com chave = hash do conteúdo do arquivo e das
  dependências. Mudou o arquivo, refaz. Peça que não constrói (ou vazio com parâmetro obrigatório sem
  padrão) fica com o ícone da categoria.
- **Na tela:** a lista aparece na hora com o ícone, e cada miniatura entra quando fica pronta.

## A IA e a biblioteca

O chat já monta o contexto por motivo rastreável (`AiContextReason`: entrada, arquivo ativo, seleção,
dependência, fixado) e mostra cada arquivo como uma etiqueta removível.

- **Citar (etapa 2).** "Citar no chat" num item acrescenta o arquivo da peça ao contexto com um motivo
  novo, `Library`, como etiqueta. É só leitura: o `AiEditScopePolicy` não deixa a IA reescrever a
  biblioteca. Se a resposta usar o tipo e ele ainda não estiver na cena, o app importa o arquivo (mesmo
  `PartLibraryImporter` do botão Inserir) antes de validar a resposta.
- **Enxergar o catálogo (etapa 5).** O contexto leva o índice (nome + uma linha de descrição, sem o
  código) e o Harness ganha duas ferramentas finas: buscar na biblioteca e importar uma peça.
- **Em aberto:** o índice inteiro custa contexto em toda mensagem; talvez só com a busca por ferramenta.

## Arquitetura

Respeita a regra de dependência (ARCHITECTURE §2): regra em `Application`, disco em `Infrastructure`.

| Onde | O quê |
|---|---|
| `Application/Library/` (novo) | `PartLibraryCatalog` (lê pastas → itens, reaproveitando `PartSourceEditor.FindPartTypes`), `PartLibraryImporter` (fecho de dependências, cópia, detecção de conflito e de divergência), `PartLibraryItem`. Puro: recebe e devolve textos. |
| `Application/Library/ThumbnailRasterizer` (novo) | malha → imagem, em CPU |
| `Application/Ai/Context/AiContextBuilder.cs` | motivo `Library` para a peça citada |
| `Application/Interfaces/IPartLibraryStore.cs` (novo) | porta para listar, ler e gravar arquivos das pastas de biblioteca |
| `Infrastructure/Library/` (novo) | implementação em disco, `library_settings.json` (o caminho da pasta) e o cache de miniaturas |
| `UI/ViewModels/LibraryViewModel.cs`, `UI/Views/LibraryView.axaml` (novos) | a aba; inserir delega a `SceneScriptViewModel` (`InsertPartAsync` ganha os argumentos escolhidos) |
| `UI/Views/MainWindow.axaml`, `UI/Resources/Strings.cs`, `UI/App.axaml.cs` | `TabItem` novo, textos, composição |
| `CadViewModel` | os índices das abas mudam (`CodeTabIndex`) |

Furar com a peça aberta reaproveita o que existe: `PartBooleanOperation` já copia outra peça como
sub-peça (`new Tipo(...).Instance("x", at:)`) e o `IPartMerger` já converte mundo → local.

Ao implementar, o ARCHITECTURE.md ganha uma linha na §4 ("Mudar a biblioteca de peças").

## Etapa 1: como ficou

Arquivos: `AvaloniaCadEditor.Application/Library/` (`PartLibraryCatalog`, `PartLibraryImporter` com o
`PartDependencyIndex`, `PartLibraryInsertion`, `PartThumbnailService`, `ThumbnailRasterizer`), as portas
`IPartLibraryStore` e `IThumbnailCache`, `AvaloniaCadEditor.Infrastructure/Library/DiskPartLibraryStore.cs`
(também o `DiskThumbnailCache`), `LibraryViewModel` e `LibraryView`. Testes em `AvaloniaCadEditor.Tests/Library/`.

O que mudou em relação ao plano:

- **Conflito com a cena.** Um tipo que a cena já declara fica com a versão da cena. Se o texto for diferente
  do da biblioteca, a inserção segue e o status avisa, em vez de recusar: sem o cabeçalho de origem (decisão 7,
  etapa 2), não dá para separar "cópia alterada" de "outra peça com o mesmo nome". A recusa fica para o que
  quebraria a compilação: um arquivo cujos tipos a cena já tem só em parte, ou um caminho de destino ocupado.
- **Miniaturas com kernel próprio**, não pela fila do `SceneMeshManager`: o cache dele é por execução da cena e
  o lock faria a UI esperar. É o mesmo arranjo do Harness. O `PureMathGeometryKernel` não constrói instância de
  peça, por isso o teste da miniatura usa o OCCT.
- **Ctrl+Z da inserção** esvazia os arquivos copiados, não os apaga (o desfazer de vários arquivos só troca
  texto). Um arquivo vazio no destino pode ser ocupado de novo pela próxima inserção.
- **A biblioteca começa vazia.** O acervo inicial vem com "Enviar para a biblioteca" (etapa 2). Até lá, a aba
  mostra o caminho da pasta e o que fazer.
- O combo "Inserir" da aba Código continua (pergunta em aberto 1).
- A aba Biblioteca entrou com índice 1: `CadViewModel.CodeTabIndex` passou de 1 para 2.

## Etapa 2: como ficou

Arquivos: `AvaloniaCadEditor.Application/Library/PartLibrarySync.cs` (`PartLibrarySync` e `PartLibraryCitation`),
`IPartLibraryStore.Write`, o motivo `AiContextReason.Library` e `AiContextRequest.LibraryDocuments`,
`AiChatViewModel.CiteLibraryPart`, `SceneScriptViewModel.UpdatePartFromLibraryAsync`, o menu da árvore
(`ModelTreeViewModel.SendPartToLibraryRequested`). Testes em `AvaloniaCadEditor.Tests/Library/PartLibrarySyncTests.cs`.

O que mudou em relação ao plano:

- **Sem cabeçalho de origem (decisão 7).** A cópia da cena e a da biblioteca se ligam pelo nome do tipo, que é
  global na cena. Assim os arquivos ficam idênticos, e "alterada" é simplesmente texto diferente (ignorando a
  quebra de linha). O caminho na biblioteca vem de quem declara o tipo lá.
- **Enviar** grava a peça no arquivo que já a declara na biblioteca (substitui) ou, se for nova, em
  `Projetos/<nome da cena>/`, com as dependências que a biblioteca não tem. Uma dependência que lá está diferente
  **não** é substituída (aviso): só a peça escolhida muda. Recusa peça que dependa de tipo do arquivo de entrada,
  e arquivo da biblioteca que declare tipos a mais (substituí-lo os apagaria). Não há confirmação: o botão diz
  "substitui", e a pasta é versionável.
- **Atualizar da biblioteca** troca o texto no caminho da cena (`ApplySourceEditAsync`: o arquivo vira o ativo e
  o Ctrl+Z desfaz) e copia as dependências que faltarem.
- **Citar no chat.** Peça da cena é só fixada no contexto. Peça da biblioteca entra no contexto, e na validação,
  no caminho onde a cópia ficaria (`Pecas/X.cs`), com o motivo `Library` (chip com 📚). Ao aplicar a resposta, se
  ela usa o tipo e nem a cena nem a resposta o declaram, os arquivos são copiados antes. Se a IA reescrever o
  arquivo citado, quem muda é a cópia da cena, nunca a biblioteca. Tirar o chip descita a peça. "New chat" e
  troca de cena limpam as citações.
- **Acervo inicial:** não foi gravado automaticamente na pasta do usuário. Basta "Enviar para a biblioteca" nas
  peças da `AiCore` (botão direito na árvore ou na aba); elas vão para `Projetos/AiCore/` e podem ser
  reorganizadas em subpastas à mão.

## Etapa 3: como ficou

Arquivos: `Part.IsCutter` (Domain), `PartLibraryCatalog.IsCutter`, `AvaloniaCadEditor.Application/Library/PartLibraryDrill.cs`,
`SceneScriptViewModel.DrillPartFromLibraryAsync`, `CadViewModel.BeginFacePick`/`TryCompleteFacePick`, o botão na aba.
Testes em `AvaloniaCadEditor.Tests/Library/PartLibraryDrillTests.cs`.

- **Marca (pergunta 2): propriedade em `Part`.** `public virtual bool IsCutter => false;`, no molde de `DrawingLabel`. O
  vazio declara `public override bool IsCutter => true;` e a biblioteca a lê pela sintaxe, então vale também para as
  peças da cena, que não têm subpasta. `FuroRebaixado` e `FuroPorca` (na `AiCore`) já a declaram.
- **A lista** mostra a marca `vazio` ao lado das outras. O detalhe troca "Inserir na cena" por **Furar a peça aberta**,
  ligado só com uma peça aberta (duplo clique), e o código mostra a linha que vai para o `Build()`, com `‹clique›`
  no lugar do ponto e da direção.
- **O clique.** Furar põe a viewport em escolha de face (o realce e o TAB são os do Workplane). Só vale uma face da peça
  aberta; Esc, ou sair da peça, desiste. O ponto vai para o primeiro parâmetro `Point3D` do vazio (`Face`, `Entrada`) e
  a normal invertida, para dentro do material, para o primeiro `Vector3D` (`Direcao`), os dois já nas coordenadas do
  `Build()` (ponto em centésimos de mm). Os outros parâmetros são os do detalhe; o modelo pronto nunca entra pelo
  nome, porque não traria o ponto.
- **A gravação** é a do Extrude na peça aberta: um trecho de cena com
  `AddFeature(furo.Transformed(posição da peça))` passa pelo `PartSolidInserter` como ferramenta da diferença. Como os
  parâmetros já são locais, a conversão mundo → local é a identidade e a linha entra como está, sem `at:`, conferida
  pelo `PartMergeCheck`. Vazio da biblioteca que a cena não tem é copiado para `Pecas/` antes, e um Ctrl+Z desfaz a
  cópia e o furo juntos. Na recusa nada muda: o vazio não faz sentido solto na cena.
- **Modelos M3/M4/M5** no `FuroRebaixado` (`DiametroFuro`, `DiametroRebaixo`, `ProfundidadeRebaixo` com folga de
  impressão). Os padrões do record não mudaram, para as peças que já o usam não mexerem.
- **Fora desta etapa:** a `FuroPorca` pede a `Entrada` 1 mm fora da face, por convenção; o clique a põe sobre a face, o
  que ainda corta (o furo começa `FolgaAntes` antes do alojamento). As regras do dialeto (`CadScriptRules`) não
  ensinam o `IsCutter` à IA: mudar o perfil `atual` exige regravar o golden.

## Etapa 4: como ficou

Arquivos: `CadScriptContext.Library`, `CadToolContext.SceneDocuments`/`Library`, as ferramentas
`AvaloniaCadEditor.Harness/Tools/Query/SearchLibraryTool.cs` e `AvaloniaCadEditor.Harness/Tools/Edit/ImportLibraryPartTool.cs`,
um parágrafo em `HarnessPrompt.Tools`. Testes em `AvaloniaCadEditor.Harness.Tests/Agents/LibraryToolsTests.cs`.

- **Sem índice no prompt (pergunta 3).** A biblioteca inteira vai no `CadScriptContext.Library`, que não é enviado ao
  modelo como texto; só as ferramentas a leem. O custo em toda mensagem é o das duas definições de ferramenta.
- **`search_library`** (leitura): sem `query`, as categorias com os nomes; com `query`, as peças em que todas as
  palavras aparecem no nome, na categoria ou na descrição (até 15), com a assinatura, os modelos prontos e as marcas
  "vazio" e "já está na cena". O Inspetor também a recebe, por ser de leitura.
- **`import_library_part`** (edição): o mesmo plano do botão Inserir (`PartLibraryImporter`), gravado na cópia de
  trabalho do turno: as cópias saem como mudanças da resposta, a UI as aplica com o resto e o Ctrl+Z desfaz tudo.
  Devolve a assinatura e o código da peça. Só em cena de pasta (projeto); a biblioteca nunca muda.
- O chat de sempre (sem harness) continua só com a peça citada (etapa 2).

## Etapas

- [x] **1. Catálogo, aba, inserir e miniaturas.** `Application/Library`, porta, store em disco,
      configuração da pasta, aba com busca, origens, lista com miniatura, detalhe com parâmetros e
      "Inserir na cena" com cópia do arquivo e das dependências. Testes: catálogo sobre `AiCore/Pecas`,
      fecho de dependências, conflito de nome, cena importada passa no `SceneCheckService`, rasterizador
      sobre uma caixa.
- [x] **2. Enviar para a biblioteca, divergência e citar no chat.** Ação na árvore e no detalhe, marca
      `alterada`, "Atualizar da biblioteca" (desfazível com Ctrl+Z), motivo `Library` no contexto da IA e
      importação automática quando a resposta usa a peça. Acervo inicial: as peças de catálogo da `AiCore`.
- [x] **3. Vazios.** Marca peça × vazio, "Furar a peça aberta" com clique na face, modelos M3/M4/M5
      para `FuroRebaixado`.
- [x] **4. A IA busca sozinha.** Ferramentas de buscar e importar no Harness (o índice ficou fora do contexto).

## Perguntas em aberto

1. A linha "combo de peças + Inserir" da aba Código (só aparece quando a cena tem peças;
   `SceneScriptViewModel.InsertPartCommand`) sai quando a aba Biblioteca existir, ou as duas convivem?
   Proposta: sai.
2. ~~Como marcar um vazio: propriedade em `Part` ou convenção de pasta?~~ Propriedade (`Part.IsCutter`), decidido
   com o usuário em 2026-11-12.
3. ~~O índice da biblioteca vai em toda mensagem à IA, ou só pela ferramenta de busca?~~ Só pela ferramenta do Harness,
   decidido com o usuário em 2026-11-12.

## Riscos

- **Dependências escondidas.** Peça que usa um tipo auxiliar que não é `Part` (enum, record de medidas)
  declarado em outro arquivo: o fecho precisa seguir todo tipo declarado na biblioteca, não só `Part`.
- **Nomes iguais, peças diferentes.** Duas subpastas com `Suporte.cs`: o tipo é global na cena, então
  a segunda importação tem de recusar. O catálogo avisa do nome repetido já na lista.
- **Custo das miniaturas.** Cada uma é uma compilação e uma construção de sólido. Com cache por
  conteúdo isso acontece uma vez por peça; a primeira abertura de uma biblioteca grande demora, em
  segundo plano.
- **Cópias que envelhecem.** É o preço da decisão 1; a marca `alterada` e a atualização explícita são a
  resposta, e não uma atualização automática.
- **`CadViewModel` e `SceneScriptViewModel` já são grandes** (ARCHITECTURE §6.3): a regra nasce em
  `Application/Library` e o `LibraryViewModel` é próprio, só a chamada de inserir passa por eles.
