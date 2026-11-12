# Biblioteca de peças: uma aba na coluna direita com as peças de uso recorrente, fora de qualquer cena

**Status:** Em andamento: etapa 1 feita (ver "Etapa 1: como ficou") · **Data:** 2026-11-11 · **Branch:** `claude/confident-maxwell-yk1mdx`

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

## Etapas

- [x] **1. Catálogo, aba, inserir e miniaturas.** `Application/Library`, porta, store em disco,
      configuração da pasta, aba com busca, origens, lista com miniatura, detalhe com parâmetros e
      "Inserir na cena" com cópia do arquivo e das dependências. Testes: catálogo sobre `AiCore/Pecas`,
      fecho de dependências, conflito de nome, cena importada passa no `SceneCheckService`, rasterizador
      sobre uma caixa.
- [ ] **2. Enviar para a biblioteca, divergência e citar no chat.** Ação na árvore e no detalhe, marca
      `alterada`, "Atualizar da biblioteca" (desfazível com Ctrl+Z), motivo `Library` no contexto da IA e
      importação automática quando a resposta usa a peça. Acervo inicial: as peças de catálogo da `AiCore`.
- [ ] **3. Vazios.** Marca peça × vazio, "Furar a peça aberta" com clique na face, modelos M3/M4/M5
      para `FuroRebaixado`.
- [ ] **4. A IA busca sozinha.** Índice no contexto e ferramentas de buscar e importar no Harness.

## Perguntas em aberto

1. A linha "combo de peças + Inserir" da aba Código (só aparece quando a cena tem peças;
   `SceneScriptViewModel.InsertPartCommand`) sai quando a aba Biblioteca existir, ou as duas convivem?
   Proposta: sai.
2. Como marcar um vazio: propriedade em `Part` ou convenção de pasta?
3. O índice da biblioteca vai em toda mensagem à IA, ou só pela ferramenta de busca?

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
