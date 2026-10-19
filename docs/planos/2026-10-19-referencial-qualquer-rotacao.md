# Referencial da peça isolada (H) para qualquer rotação: cotas em peças "deitadas"

- **Status:** Proposto
- **Data:** 2026-10-19
- **Branch:** a definir (sugestão: `feature/referencial-qualquer-rotacao`)

## Objetivo

Isolar com H uma peça girada em X ou Y (`carrinho_hgh15_z_a/b`, `trilho_hgr15_z_*`, extrusões
verticais "deitadas", motores e polias do Y na fixture `motor-z-gantry`) e ver as cotas como em
qualquer outra peça. As vistas Topo, Frente, Lateral etc. passam a ser **as da peça**, com o desenho
na orientação convencional da tela (Z da peça para cima na Frente, X para a direita).

## Situação atual

O caminho que hoje some com as cotas, sem nenhum aviso:

1. `PartFrameResolver.Resolve` (`AvaloniaCadEditor.Application/Isolation/PartFrameResolver.cs`) só
   aceita um placement que leve o Z local para ±Z do mundo. Fora disso devolve `null`.
2. `CadViewModel.ApplyIsolation` grava esse `null` em `DisplayFrame`.
3. `DraftingViewModel.Recompute` só monta a folha com `Frame != null`: sem folha, não há cotas nem
   legenda, e o isolamento segue normal.

A restrição vem da câmera. `CameraState` é orbital com Z para cima (azimute + elevação, sem rolagem), e
`SetStandardView(view, azimuthOffset)` só acompanha giros em Z. O `PartFrame` expõe o giro como
`AzimuthOffset = FrameToWorld.RotationZRadians`, que só faz sentido nesse caso.

O que **já é genérico** e não precisa mudar:

| Parte | Por quê |
|---|---|
| Desenho (`Drafting`, `DraftingInput.ForIsolated`, `ViewBasis`) | Trabalha todo no referencial da peça: leva a árvore com `rootToWorld.Then(frame.WorldToFrame)` e as vistas são uma tabela fixa nesse referencial |
| Grade e eixos (`GridRenderPass`, `AxisTriadOverlay`) | Usam `DisplayFrame.XY` como `WorkPlane` qualquer, já desenham em plano vertical |
| Pick, projeção, pan | Usam a matriz de vista ou `camera.Up`. Se `Up` passar a incluir a rolagem, continuam certos |
| Harness (`SceneInspectionService`) | Projeta a malha já no referencial com `ProjectionBasis.For(view)` sem offset. Com referencial novo, a peça só deixa de ser `FrameIsWorld` |

### Por que só o resolver não basta

Exemplo do `carrinho_hgh15_z_b` (`Rotation(X, 90)` e depois giro de −90° em Z): o X da peça vai para
−Y do mundo, o Y para +Z e o Z para −X.

| Vista da peça | Olha de (mundo) | "Cima" da tela (mundo) | Câmera Z-up consegue? |
|---|---|---|---|
| Topo | −X | +Z | sim, rolagem 0 |
| Frente | −Z (de baixo) | −X | sim: olhar vertical, o azimute escolhe o "cima" |
| Direita | −Y | −X | **não**: olhar horizontal com "cima" horizontal exige rolagem de 90° |

Sem rolagem, a Direita apareceria girada 90° na tela, o `MatchesCamera` (que hoje só compara a
direção) diria que bate e as cotas sairiam de lado. Daí a rolagem na câmera.

## Desenho

### 1. Referencial (Application, função pura)

`PartFrameResolver.Resolve` passa a aceitar qualquer placement de `PartInstance`/`TransformedFeature`.
O referencial é uma **rotação própria** (det +1), escolhida assim:

| Placement | Eixos do referencial | Muda em relação a hoje? |
|---|---|---|
| Rotação (det +1), qualquer eixo | os da peça: `L·X`, `L·Y`, `L·Z` | Não para giro só em Z. **Sim** para giro de 180° em X/Y (ex.: `porca_fuso_z`), que hoje é tratado como espelho e fica com Z para cima |
| Espelho que mantém o Z em ±Z | regra de hoje: giro em Z que leva o X da peça | Não (trava: `MirroredInstanceDrawingTests`) |
| Outro espelho | X e Y da peça, `Z = X × Y` | Novo (hoje é `null`) |

Os eixos passam por um "snap" de ruído (|c| < 1e-9 vira 0, ||c| − 1| < 1e-9 vira ±1), como o
`ProjectionBasis.Clean`, para giros de 90° não deixarem 1e-17 nas cotas. A origem continua no canto
mínimo da caixa medida nesses eixos, e a função continua sem estado.

Domain ganha `Placement.FromAxes(Vector3D x, Vector3D y, Vector3D z, Vector3D translation)`
(ortonormal, validado) para montar a rotação, já que o construtor é privado.

`IsRotationAboutZOnly` e `RotationZRadians` ficam como estão: o `RoslynPartMerger` e o próprio ramo
dos espelhos ainda os usam.

### 2. Câmera com rolagem (Domain)

`CameraState` ganha `Roll` (radianos, em torno da direção de visão), com 0 como padrão:

- `Up` passa a ser o "cima" de hoje girado de `Roll` em torno de `Position − Target`. `GetViewMatrix`,
  `Pan`, `ScreenPointToRay`, o tríade de eixos e o `ProjectionBasis` já leem `Up`, então herdam a
  rolagem sem mudar.
- `LookFrom(Vector3 eye, Vector3 up)`: calcula azimute e elevação a partir de `eye`. Com olhar vertical
  (elevação ±90°), escolhe o azimute para o `up` sair com rolagem 0. Senão, `Roll` é o ângulo com sinal
  entre o "cima" orbital e o `up` pedido.
- `SetStandardView(StandardView view, PartFrame? frame = null)` substitui a sobrecarga com
  `azimuthOffset`. Sem frame, comportamento idêntico ao de hoje (rolagem 0). Com frame, pega olho e cima
  da vista no referencial e os leva ao mundo com `frame.FrameToWorld.ApplyVector`, depois chama
  `LookFrom`. A Iso entra no mesmo caminho.
- `Orbit` zera `Roll` antes de aplicar o delta. Girar a câmera já esconde as cotas (`MatchesCamera`), e
  assim o arrasto volta ao modelo orbital Z-up conhecido. O custo é um "salto" de rolagem no primeiro
  arrasto (ver perguntas em aberto).

`PartFrame.AzimuthOffset` sai. Os chamadores são `MainWindow.StandardView_Click`, os testes
`StandardViewTests` e `DraftingViewModelTests`, e o `ProjectionBasis.For(view, azimuthOffset)` (que no
Harness é sempre chamado sem offset, então o parâmetro some).

### 3. UI

- `MainWindow.StandardView_Click`: `ViewportControl.SetStandardView(view, vm?.DisplayFrame)`, e
  `SilkViewportControl.SetStandardView` com a mesma assinatura nova.
- `DraftingViewModel.MatchesCamera` também confere o "cima": `camera.Up · (FrameToWorld · Basis.V) > 0.999`,
  além da direção. Sem isso, um referencial que bate na direção mas não na rolagem mostraria as cotas de
  lado.
- Aviso na barra de status quando há uma peça única isolada e **ainda assim** não há referencial
  (feature solta sem placement): "Cotas indisponíveis: <motivo>". Hoje esse caso também é silencioso.

### 4. O que não muda

- Nada escreve código com o referencial: ele continua só visual, e as cotas manuais continuam por Id de
  âncora, por peça e vista.
- O `FactExtractor` e o `RelationAnalyzer` não mudam: já recebem a árvore levada ao referencial.
- A regra de dependência não muda: o Domain ganha `Placement.FromAxes` e `CameraState.Roll`/`LookFrom`,
  e o Application usa o primeiro.

## Etapas

- [ ] **Domain:** `Placement.FromAxes` (com teste de ortonormalidade e de `Inverse`).
- [ ] **Domain:** `CameraState.Roll`, `Up` com rolagem, `LookFrom`, `SetStandardView(view, PartFrame?)`
      e `Orbit` zerando a rolagem. Remover `PartFrame.AzimuthOffset`.
- [ ] **Application:** `PartFrameResolver` com as três regras da tabela e o snap de eixos; atualizar o
      `<summary>`.
- [ ] **UI:** `MainWindow`, `SilkViewportControl.SetStandardView`, `DraftingViewModel.MatchesCamera`
      e o aviso na barra de status.
- [ ] **Harness:** tirar o `azimuthOffset` do `ProjectionBasis.For` e atualizar o comentário de
      `FrameIsWorld` em `SceneInspectionService` ("peça girada fora de Z" deixa de ser motivo).
- [ ] **Testes** (abaixo).
- [ ] **Docs:** linha "Mudar o referencial da peça isolada (H)" da §4 do `ARCHITECTURE.md` (citar
      `CameraState` e a rolagem), e Status deste plano aqui e no `docs/README.md`.
- [ ] Rodar `dotnet test` dos dois projetos de teste e `check AiCore --geometry`.

## Testes

| Arquivo | Caso |
|---|---|
| `Tests/Isolation/PartFrameTests.cs` | `Resolve_RotacaoForaDeZ_SemReferencial` vira `Resolve_RotacaoEmX_ReferencialNosEixosDaPeca`: caixa no octante positivo com extensões 40 × 16 × 17. Casos novos: giro do carrinho Z (X 90° e depois Z −90°), giro qualquer (37° em X), espelho que não mantém Z, giro de 180° em X com Z para baixo |
| `Tests/Isolation/StandardViewTests.cs` | Para o referencial do carrinho Z e **cada** vista padrão: olho = `R·W`, direita da tela = `R·U`, cima = `R·V` (`ViewBasis` como gabarito). Regressão: com frame só em Z, `Azimuth`/`Elevation` iguais aos de hoje e `Roll` = 0 |
| `Tests/Isolation/CameraRollTests.cs` (novo) | `Orbit` zera a rolagem; `Pan` com rolagem anda nos eixos da tela; `ScreenPointToRay` no centro da tela continua apontando para o alvo |
| `Tests/Drafting/RotatedInstanceDrawingTests.cs` (novo) | **Invariante principal:** o `CarrinhoHGH15` real (`AiCore/Pecas`) posicionado como na cena dá, em cada vista, as mesmas cotas (chave e valor) que a mesma peça com `Placement.Identity` |
| `Tests/Drafting/DraftingViewModelTests.cs` | Peça deitada isolada: `Sheet` não nulo e `IsShowing` verdadeiro depois do `SetStandardView` com o frame. Falso com a mesma direção e rolagem errada |
| `Harness.Tests/Inspection/SceneInspectionServiceTests.cs` | Peça girada em X deixa de ser `FrameIsWorld` e a vista em texto sai no referencial dela |

## Perguntas em aberto

1. **Salto da rolagem no primeiro arrasto.** Zerar `Roll` no `Orbit` gira a imagem de uma vez (até
   90°) quando o usuário começa a orbitar a partir de uma vista rolada. Alternativas: animar a volta em
   ~150 ms, ou manter a rolagem e orbitar em torno do "cima" atual (um modelo de câmera trackball, bem
   maior). Recomendação: zerar na v1 e só animar se incomodar.
2. **Giro de 180° em X/Y sem espelho** (`porca_fuso_z`). Pela regra nova o referencial segue a peça, e
   o "Topo" dela passa a olhar de baixo no mundo. É o coerente com "as vistas seguem a peça", mas muda o
   que se vê hoje nessa peça. Recomendação: aceitar, porque as cotas manuais dela são só da sessão.
3. **Vistas da cena sem peça isolada** continuam como hoje (rolagem 0). Não há motivo para mexer.

## Riscos

- **Overlays que assumem Z para cima.** A busca por `.Up`, `.Azimuth` e `.Elevation` só achou
  `AxisTriadOverlay`, `ProjectionBasis`, os testes e o `ViewportInputHandler` (`Orbit`). Antes de
  fechar, rodar de novo e conferir os overlays de texto (`DraftingLabelsOverlay`, guias de referência)
  numa vista rolada.
- **Ruído numérico nas cotas** com giros de 90°: o snap dos eixos no resolver resolve, e o teste de
  invariância (peça girada = peça na identidade) trava isso.
- **Saída do Harness muda** para peças giradas: o texto das vistas passa a vir no referencial delas.
  Os casos do PromptEval que usam a fixture `motor-z-gantry` podem pontuar diferente, então vale rodar
  uma vez antes e depois.
- **Teste de GUI:** a verificação final na janela (isolar `carrinho_hgh15_z_b`, Cotas, cada vista) é
  manual e fica com o usuário.
