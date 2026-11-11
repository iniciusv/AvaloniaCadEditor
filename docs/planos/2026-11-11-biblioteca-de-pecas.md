# Biblioteca de peças: uma aba na coluna direita com as peças de uso recorrente, fora de qualquer cena

**Status:** Proposto: tela desenhada, decisões em aberto com o usuário · **Data:** 2026-11-11 · **Branch:** `claude/confident-maxwell-yk1mdx`

Protótipo clicável da tela: <https://claude.ai/artifact/DouhKq1HUnZSrFS82Wt3Rf>

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
2. **Origens**, como filtro: *Nesta cena* (`Pecas/` do projeto aberto), *Minha biblioteca* (pasta
   pessoal, fora de qualquer projeto) e uma entrada por *biblioteca de projeto* (pastas extras que o
   usuário aponta).
3. **Lista** agrupada por categoria, cada item com ícone, nome, uma linha de descrição e uma marca:
   `vazio` (corta uma peça), `na cena` (já copiada) ou `alterada` (a cópia da cena difere da biblioteca).
4. **Detalhe** do item escolhido: descrição, modelos prontos, parâmetros editáveis, o código que será
   gravado e as ações.

Ações, conforme o item e o nível de edição:

| Item | Na cena | Com uma peça aberta (duplo clique) |
|---|---|---|
| Peça | **Inserir na cena** | Inserir na cena · Cortar da peça aberta |
| Vazio | desligado, com a dica "abra a peça que vai receber o furo" | **Furar a peça aberta**, depois clique na face (ponto → `Face`, normal → `Direcao`) |
| Peça da cena que não está na biblioteca | Enviar para a biblioteca | idem |
| Peça `alterada` | Atualizar da biblioteca · Enviar para a biblioteca | idem |

Na árvore do modelo, o botão direito numa peça ganha "Enviar para a biblioteca".

## Decisões propostas

1. **Inserir copia o arquivo para a cena.** Na primeira vez, o `.cs` da peça e os das peças de que ela
   depende (fecho transitivo dos tipos `Part` usados) vão para `Pecas/`. A cena continua completa na
   própria pasta: o `check`, o git, o `AGENTS.md` e uma IA externa não precisam conhecer a biblioteca,
   e a regra "o disco é a fonte da verdade" (ARCHITECTURE §1) não muda. Referenciar a biblioteca direto
   deixaria tudo sempre atualizado, mas uma mudança lá quebraria cenas antigas sem aviso.
2. **O catálogo é o próprio código, sem formato novo.** Categoria = subpasta. Descrição = `<summary>`.
   Parâmetros = construtor do record (o `PartTypeInfo` que já existe). Modelos prontos = propriedades
   estáticas que devolvem o próprio tipo. Tudo lido de forma sintática, sem compilar a biblioteca.
3. **Peça × vazio.** A lista precisa saber qual item corta. Proposta: uma propriedade virtual em
   `Part`, no molde de `DrawingLabel`, ou a convenção de pasta (`Furos/`). Ver perguntas em aberto.
4. **Divergência por conteúdo.** A cópia guarda de onde veio num comentário de cabeçalho
   (`// biblioteca: Perfis/Extrusao2020`); "alterada" é a comparação do texto dos dois arquivos.
   Conflito de nome com uma peça diferente já existente na cena: recusa com mensagem, não sobrescreve.
5. **A aba substitui o combo "Inserir peça" da aba Código.**

## Arquitetura

Respeita a regra de dependência (ARCHITECTURE §2): regra em `Application`, disco em `Infrastructure`.

| Onde | O quê |
|---|---|
| `Application/Library/` (novo) | `PartLibraryCatalog` (lê pastas → itens, reaproveitando `PartSourceEditor.FindPartTypes`), `PartLibraryImporter` (fecho de dependências, cópia, detecção de conflito e de divergência), `PartLibraryItem`. Puro: recebe e devolve textos. |
| `Application/Interfaces/IPartLibraryStore.cs` (novo) | porta para listar, ler e gravar arquivos das pastas de biblioteca |
| `Infrastructure/Library/` (novo) | implementação em disco + `library_settings.json` (pasta pessoal e pastas extras) |
| `UI/ViewModels/LibraryViewModel.cs`, `UI/Views/LibraryView.axaml` (novos) | a aba; inserir delega a `SceneScriptViewModel` (`InsertPartAsync` ganha os argumentos escolhidos) |
| `UI/Views/MainWindow.axaml`, `UI/Resources/Strings.cs`, `UI/App.axaml.cs` | `TabItem` novo, textos, composição |
| `CadViewModel` | os índices das abas mudam (`CodeTabIndex`) |

Furar com a peça aberta reaproveita o que existe: `PartBooleanOperation` já copia outra peça como
sub-peça (`new Tipo(...).Instance("x", at:)`) e o `IPartMerger` já converte mundo → local.

Ao implementar, o ARCHITECTURE.md ganha uma linha na §4 ("Mudar a biblioteca de peças").

## Etapas

- [ ] **1. Catálogo e aba (só leitura + inserir peças).** `Application/Library`, porta, store em disco,
      aba com busca, origens, lista, detalhe com parâmetros e "Inserir na cena" com cópia do arquivo e
      das dependências. Remove o combo da aba Código. Testes: catálogo sobre `AiCore/Pecas`, fecho de
      dependências, conflito de nome, cena importada passa no `SceneCheckService`.
- [ ] **2. Enviar para a biblioteca e divergência.** Ação na árvore e no detalhe, marca `alterada`,
      "Atualizar da biblioteca" (desfazível com Ctrl+Z). Acervo inicial: as peças de catálogo da `AiCore`.
- [ ] **3. Vazios.** Marca peça × vazio, "Furar a peça aberta" com clique na face, modelos M3/M4/M5
      para `FuroRebaixado`.
- [ ] **4. Miniaturas.** Imagem renderizada por item, em cache pelo conteúdo do arquivo.
- [ ] **5. IA.** O contexto do chat recebe o catálogo (nome + descrição) e o Harness ganha uma
      ferramenta para importar uma peça da biblioteca, em vez de reescrevê-la.

## Perguntas em aberto

1. Copiar para a cena (proposto) ou referenciar a biblioteca?
2. Pasta pessoal: escolhida pelo usuário, com padrão em Documentos e versionável com git (proposto),
   ou escondida em AppData?
3. A aba substitui o combo "Inserir peça" da aba Código (proposto) ou convivem?
4. Como marcar um vazio: propriedade em `Part` ou convenção de pasta?
5. Miniaturas já na etapa 1, ou ícone por categoria primeiro (proposto)?
6. A IA enxerga a biblioteca já, ou na etapa 5 (proposto)?

## Riscos

- **Dependências escondidas.** Peça que usa um tipo auxiliar que não é `Part` (enum, record de medidas)
  declarado em outro arquivo: o fecho precisa seguir todo tipo declarado na biblioteca, não só `Part`.
- **Nomes iguais, peças diferentes.** Duas bibliotecas com `Suporte.cs`: o tipo é global na cena, então
  a segunda importação tem de recusar.
- **Cópias que envelhecem.** É o preço da decisão 1; a marca `alterada` e a atualização explícita são a
  resposta, e não uma atualização automática.
- **`CadViewModel` e `SceneScriptViewModel` já são grandes** (ARCHITECTURE §6.3): a regra nasce em
  `Application/Library` e o `LibraryViewModel` é próprio, só a chamada de inserir passa por eles.
