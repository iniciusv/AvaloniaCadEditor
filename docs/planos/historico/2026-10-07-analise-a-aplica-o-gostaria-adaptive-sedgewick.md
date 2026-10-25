# Nova aba: "Gravação Automática" (auto-record + auto-avançar)

## Context

O OCRWin hoje é um app WinUI 3 com duas abas de OCR: captura estática com anotação manual (human-in-the-loop) e captura dinâmica com costura de rolagem. Toda a inteligência visual já existe e está desacoplada em `OCRWin.Core`: `VisionDetector` (detecção + reconhecimento via RapidOCRSharpOnnx/PP-OCRv4 em DirectML), `OcrRecognizer`, `LayoutAnalyzer`, `ScreenCaptureHelper` (GDI) e `ScrollStitcher` (template matching OpenCV).

O que falta é um **modo autônomo**: assistir uma sequência de vídeos num site, gravar cada um em arquivo próprio, detectar quando o vídeo termina e clicar no botão de avançar — repetindo sem supervisão por horas. Toda a interação é **puramente visual** (captura de tela + OCR + template matching + mouse sintético): nada de extensão de navegador, CDP/DevTools, injeção de JS ou requisições ao site. Isso reaproveita exatamente a pipeline de imagem→texto que já existe.

Para a gravação, a máquina tem duas GPUs AMD e o caminho oficial da AMD é o **AMF** (Advanced Media Framework). Será usado via `ffmpeg.exe` com os encoders `hevc_amf`/`h264_amf`/`av1_amf`, que são wrappers finos sobre `amfrt64.dll` — mesmo silício, mesma qualidade de encode que chamar o SDK direto, sem ter que escrever bindings e um muxer MP4 em C#.

> Nota de escopo: o desenho não toca o site nem contorna proteção (não há DRM), mas gravar e redistribuir conteúdo pode conflitar com os termos de uso da plataforma. Decisão do usuário; o plano segue como pedido.

### Decisões já tomadas

| Tema | Decisão |
|---|---|
| Fim do vídeo | Combinação de **3 sinais**: frame congelado + ausência de som + ícone específico na tela |
| Clique | `SendInput` com mouse real, com verificação pós-clique e parada de emergência |
| Codec | **`hevc_amf`** (QP ~20) como padrão; `av1_amf` exposto quando o adaptador suportar |
| Monitor/GPU | **Suportar os dois**: a sonda ffmpeg descobre adaptador+saída e grava no perfil |
| ffmpeg | **Baixar no 1º uso** (tag + SHA-256 fixos) + botão "Localizar ffmpeg.exe..." |
| Entrega | **Fase 0 primeiro** (go/no-go), depois as fases 1-8 |

### Fatos da máquina (verificados, não assumidos)

- `DISPLAY2` primário 2560×1440 → **iGPU** `AMD Radeon(TM) Graphics` (`PCI\VEN_1002&DEV_1638`, Cezanne: H.264/HEVC, **sem AV1**)
- `DISPLAY1` 1920×1080 em **X negativo** (`X=-1920`) → **RX 9060 XT** (`DEV_7590`, RDNA4: H.264/HEVC/AV1)
- O monitor primário **não** está na placa dedicada → a seleção de adaptador do `ddagrab` é obrigatória, e X negativo torna `MOUSEEVENTF_VIRTUALDESK` carga crítica no `SendInput`
- `ffmpeg.exe` **não existe** na máquina
- `NAudio.Wasapi 2.3.0` + `NAudio.Core 2.3.0` já estão no cache NuGet → restore offline funciona

---

## Fase 0 — go/no-go, zero código (fazer primeiro)

Duas incógnitas podem invalidar o plano A. Ambas se resolvem em ~10 min **com o app que já existe**:

1. **Overlays de hardware.** Chromium/Firefox usam multi-plane overlays para vídeo; GDI `BitBlt` e DXGI Desktop Duplication **omitem planos de overlay** → a região do vídeo volta **preta**. Teste: abrir o site, dar play, apertar **Ctrl+E** e olhar o overlay de seleção. Se estiver preto, tentar `--disable-features=DirectCompositionVideoOverlays` (Chromium) ou `gfx.webrender.dcomp-video-overlay-win=false` (Firefox). Se continuar preto → **plano B** (ver abaixo) antes de escrever código de gravação.
2. **ddagrab + AMF.** Baixar um build BtbN à mão e rodar:
   ```
   ffmpeg -hide_banner -h filter=ddagrab          # salvar: os nomes de opção variam por build
   ffmpeg -hide_banner -h encoder=hevc_amf        # d3d11 está nos pix_fmts?
   # matriz adaptador × saída (obrigatória com 2 GPUs AMD):
   ffmpeg -init_hw_device d3d11va=dx:<A> -filter_complex "ddagrab=output_idx=<O>:framerate=5" -frames:v 2 -f null -
   ```
   Esperado: exatamente dois pares `(adaptador, saída)` reportando 1920×1080 e 2560×1440. Gravar quais.

**Plano B** (só se a Fase 0 falhar): `Windows.Graphics.Capture` passa pelo compositor e **enxerga** planos de overlay. C# bombeia frames WGC → BGRA no stdin do ffmpeg (`-f rawvideo -pix_fmt bgra`), e o áudio vai para um `.wav` separado + mux pós-segmento. É até mais simples (stdin de propósito único), ao custo de perder a ancoragem A/V ao vivo.

---

## Arquitetura

Todos os arquivos novos em `OCRWin.Core`, achatados, `namespace OCRWin` — seguindo a convenção existente (os arquivos de Core declaram `OCRWin`, **não** `OCRWin.Core`, apesar do `RootNamespace`).

### Infraestrutura
- **`AppPaths.cs`** — `DataRoot` (`%LOCALAPPDATA%\OCRWin`, sobrescrevível por `OCRWIN_DATA`), `ProfilesRoot`, `FfmpegDir`, `CacheRoot`, `SessionsRoot` (`%USERPROFILE%\Videos\OCRWin` — vídeos são conteúdo do usuário e multi-GB), `EnsureCreated()`, `Slug()`.
- **`AutoWatchProfile.cs`** — POCOs + store (um arquivo com vários tipos, como `OcrPipeline.cs` já faz). `ScreenRect` (px físicos, coords de virtual desktop), `VisualTarget` (template + região de busca + offset de clique + texto OCR esperado + limiares + multi-escala), `EndDetectionSettings`, `RecordingSettings`, `AutoWatchProfile`, `ProfileStore`. Layout: `profiles\<slug>\profile.json` + PNGs sidecar (`next.png`, `endicon_1.png`) — **não** base64, porque quando uma sessão der errado você precisa *olhar* o que foi ensinado e poder substituir o template à mão. `System.Text.Json` in-box.

### Captura / gravação
- **`FfmpegLocator.cs`** — resolução: override do perfil → `%LOCALAPPDATA%\OCRWin\ffmpeg\bin\ffmpeg.exe` → `AppContext.BaseDirectory\ffmpeg\` → `PATH`. `DownloadAsync` com **tag e SHA-256 fixos** (nunca `latest`: um upgrade silencioso quebra o cache da sonda e as strings de argumento); extrair em diretório próprio e vazio, manter só `bin\ffmpeg.exe`.
- **`FfmpegCapabilities.cs`** — `ProbeAsync` (versão, `-filters`, `-encoders`, `-h filter=ddagrab`, `-h encoder=hevc_amf`, e a **matriz adaptador×saída**), `ChooseTier`, `MatchOutput(monitorBounds)`. Cache em `cache\ffmpeg-caps.json` chaveado por caminho+mtime+tamanho do exe. Mapeia cada `(adapter, output)` sobrevivente para um monitor casando `W×H` com `Screen.AllScreens`.
- **`VideoRecorder.cs`** — `IDisposable`, mesma forma de `ScrollStitcher`. `static string BuildArguments(request, tier)` **puro e testável** (é onde moram os dois bugs que custam um dia cada), `StartSegmentAsync`, `WriteAudio` (não-bloqueante), `StopSegmentAsync`, `RemuxAsync`.

### Áudio
- **`AudioLoopbackMonitor.cs`** — `WasapiLoopbackCapture`; o `WaveFormat` é **lido do dispositivo, nunca assumido** (o mix format desta máquina é IEEE float 32-bit/48k/2ch, então o input do ffmpeg é `-f f32le`, não `s16le`). Fornece PCM para o recorder **e** RMS/pico em ring buffer para o detector — uma fonte, dois consumidores.
  - **Silence keeper obrigatório**: loopback WASAPI não entrega nada enquanto o endpoint está idle, o que abre buracos na linha de tempo e dessincroniza A/V. Rodar um `WasapiOut` tocando `SilenceProvider` durante toda a sessão.
- **`AudioSessionMeter.cs`** — sinal **primário** de silêncio, por processo. `MMDeviceEnumerator().GetDefaultAudioEndpoint(Render, Multimedia).AudioSessionManager.Sessions` → `AudioMeterInformation.MasterPeakValue`. Casar por **`GetSessionIdentifier`** (que embute o caminho completo do exe), **não por PID**: Chromium renderiza áudio num processo utilitário filho, então o PID da janela não é o da sessão. Resolve de saída o "outro app pode fazer barulho", e o estado `Inactive` da sessão é um segundo sinal de graça.

### Detecção
- **`FrameChangeDetector.cs`** — `Cv2.Resize` para 160×90 INTER_AREA → gray → `Absdiff` → `Mean` (MAD) + `Threshold`+`CountNonZero` (fração alterada). Reusa os `Mat` entre chamadas (sessão de 6 h).
- **`VisualTargetMatcher.cs`** — template via `Cv2.MatchTemplate(CCoeffNormed)` + `MinMaxLoc` (mesmo padrão de `ScrollStitcher.ProcessFrame`), multi-escala redimensionando o *template* em {0.90…1.10}. Para `UseMask` trocar para `CCorrNormed` — **`CCoeffNormed` não suporta máscara no OpenCV**. Caminho OCR chama o `VisionDetector.DetectRegionsAsync` existente e faz match difuso (Levenshtein sem acentos).
- **`EndOfVideoCombiner.cs`** — lógica pura, sem I/O, trivialmente testável.

### Entrada
- **`InputSimulator.cs`** — P/Invoke puro: `SendInput`, `GetCursorPos`, `GetSystemMetrics`, `GetAsyncKeyState`.

### Orquestração
- **`AutoWatchSession.cs`** — a máquina de estados, como `async Task RunAsync(CancellationToken)`.
- **`OcrEngineHost.cs`** — uma thread dedicada e longeva para toda inferência OCR (ver "Correções necessárias").

### UI
- **`OCRWin\MainWindow.AutoWatch.cs`** — `public sealed partial class MainWindow`, todos os handlers da aba. Desvio deliberado da convenção "tudo em MainWindow.xaml.cs": aquele arquivo já tem 891 linhas e isto adiciona ~500. `partial` mantém a convenção real (code-behind, sem MVVM, sem DI, sem Frame/Page) e o arquivo legível. Mesma classe, mesmos campos `x:Name`, zero mudança de XAML.

---

## Máquina de estados

```
Idle → Preparing → Recording ⇄ DetectingEnd → Confirming → StoppingSegment
                       ↑                          │(rejeita → Recording)
                       │                          ▼
                       └── VerifyingAdvance ← Advancing (≤3 tentativas)
Finished / Aborted / Error ← de qualquer estado (teardown único)
```

**`Preparing`** — a ordem importa:
1. `FfmpegLocator` → `ProbeAsync` → `ChooseTier`. 2. Espaço livre ≥ `MinFreeBytes`.
3. Resolver o HWND do navegador (`WindowFromPoint` no centro de `VideoRegion` → `GetAncestor(GA_ROOT)`) **antes** de minimizar.
4. `SetThreadExecutionState(ES_CONTINUOUS|ES_DISPLAY_REQUIRED|ES_SYSTEM_REQUIRED)` — **sem isso, o sleep de display ou a tela de bloqueio mata o `ddagrab` no meio de uma sessão de 3 h.**
5. Registrar o hotkey de emergência. 6. `AudioLoopbackMonitor.Start()` + silence keeper; logar o `WaveFormat` resolvido.
7. `MinimizeWindow()` (já existe em `MainWindow.xaml.cs:74`) → delay → `SetForegroundWindow(browser)`. Funciona aqui **precisamente porque** somos o processo em foreground no instante do clique do usuário; mais tarde falha.
8. Criar a pasta da sessão, abrir `session.log`, gravar `profile.json` + os argumentos ffmpeg resolvidos como registro de procedência.

**`Recording`** — tick de 400 ms: congelamento + áudio + template dos alvos (**sem OCR**). Clique oportunista em alvos `Dismiss` (score > 0.90, máx. 5/segmento). **Trava de armação**: exige ≥5 ticks com movimento *ou* som antes de armar o detector — impede que "a página ainda não carregou" seja lido como "o vídeo acabou". Parada dura em `MaxVideoSeconds`.

**`DetectingEnd`** — mesmo tick, acumulando; qualquer tick abaixo de `EnterScore` volta para `Recording`. Após 3 ticks consecutivos (1,2 s) → `Confirming`.

**`Confirming`** — tick cai para 200 ms por 2 s. **Só aqui roda OCR.** Re-checa o supressor `Loading`: se casar, rejeita na hora (é buffering, não fim). Média da janela ≥ `ConfirmScore` → `StoppingSegment`.

**`Advancing`** (1..3) — achar o alvo `NextButton` (agora com OCR permitido); se não achar, salvar PNG de tela cheia em `diag\` (post-mortem é impossível sem isso) e retentar. Guardar `preClickRef` da ROI de verificação e a posição real do cursor. `MoveHumanAsync` → `ClickLeftAsync`.

**`VerifyingAdvance`** — poll de 400 ms por até 8 s. Sucesso exige **ambos**: a ROI mudou (`MeanAbsDiff > 4×` o limiar) **e** o áudio voltou *ou* o ícone de fim não casa mais. Exigir os dois mata os dois modos clássicos de falha: um realce de hover que muda pixels sem navegar, e um blip de áudio de outro app. Caso especial: ROI mudou, áudio não voltou e um alvo `PlayButton` casa → o navegador bloqueou autoplay; clicar nele uma vez (tentativa extra, não contada).

**Teardown único** nos três caminhos de saída: parar ffmpeg com graça, parar NAudio, **restaurar o cursor**, `SetThreadExecutionState(ES_CONTINUOUS)`, desregistrar o hotkey, `RestoreWindow()` (`MainWindow.xaml.cs:86`), flush do log.

### O combinador (resposta à pergunta "como combinar os 3 sinais")

**Híbrido, não pesos puros.** Cada sinal vira confiança em [0,1] em vez de booleano:
- `cFreeze = clamp01((limiar - MAD) / limiar)`
- `cSilence = clamp01((SilenceDb - picoDb) / 20)`, mais `1.0` flat se a sessão de áudio reportar `Inactive`
- `cIcon = clamp01((score - 0.70) / 0.30)`

```
se tempoDesdeInício < MinVideoSeconds ou ticksComMovimento < 5  → Suppressed
se LoadingScore > 0.85                                          → Idle ("buffering")
se RequireFreeze  e cFreeze  < 0.5                              → Idle
se RequireSilence e cSilence < 0.5                              → Idle
score = 0.30·cFreeze + 0.30·cSilence + 0.40·cIcon
```

Os sinais obrigatórios agem como porta **E** (são *necessários*: vídeo que ainda se move ou ainda faz som não terminou) e o score ponderado ordena o resto (o ícone é *suficiente* quando forte). Pesos puros deixariam um frame estático e alto disparar; E puro perderia sites sem ícone de fim. Dois constantes de tempo independentes — a corrida de 3 ticks e a média de 2 s — impedem que uma captura ruim avance a playlist.

Início sugerido: `Enter = 0.60 × 3 ticks`, `Confirm = 0.75 sobre 2 s`, `RequireFreeze+RequireSilence = true`, `RequireIcon = false`. Com um bom ícone ensinado, ligar `RequireIcon` leva os falsos positivos a ~zero.

### Segmentação: um arquivo por vídeo

**Reiniciar o ffmpeg por vídeo.** Um `.mp4` com nome legível é o que se quer de fato; a janela morta de 1-2 s coincide com o clique e o carregamento (conteúdo que não interessa); cada fronteira é um ponto natural de recuperação (falhar no vídeo 17 não custa os 16 anteriores); e permite o OCR de título nomear o arquivo. Um processo único longo não pode ser dito "corte aqui" de qualquer forma.

```
%USERPROFILE%\Videos\OCRWin\<perfil>_<yyyyMMdd_HHmmss>\
    001_titulo-do-video.mp4   002_....mp4
    session.log   (toda transição + toda amostra de sinal, TSV)
    session.json  (snapshot do perfil + resultados + args ffmpeg)
    diag\notfound_002_1.png   (só em falha)
```

Paradas de segurança: `MaxVideos` (50), `MaxSessionMinutes` (360), `MaxVideoSeconds` (3600), recheck de espaço por segmento, hotkey de emergência + poll de Esc + aborto por desvio do cursor.

---

## FFmpeg: argumentos e desligamento

### Tradução de coordenadas (fácil de errar, silencioso quando errado)
- `gdigrab`: `offset_x/y` são **virtual desktop** e **podem ser negativos** (`-1600` é válido para DISPLAY1).
- `ddagrab`: `offset_x/y` são **relativos à origem da saída DXGI** → `offset_x = VideoRegion.X - MonitorBounds.X`. Em DISPLAY1 (`MonitorBounds.X = -1920`), X virtual -1600 vira `offset_x = 320`.

Confundir os dois grava a tela errada **sem erro algum**. A tradução vive em `BuildArguments` e tem teste unitário com a geometria real desta máquina.

### Tier A — `ddagrab` → `hevc_amf` (preferido)
```
ffmpeg -hide_banner -nostats -loglevel level+info
  -init_hw_device d3d11va=dx:<adapter>
  -f f32le -ar 48000 -ac 2 -thread_queue_size 4096 -itsoffset <offset> -i pipe:0
  -filter_complex "ddagrab=output_idx=<out>:draw_mouse=0:framerate=30:offset_x=640:offset_y=200:video_size=1280x720:dup_frames=1,hwdownload,format=bgra,format=nv12[v]"
  -map "[v]" -map 0:a
  -c:v hevc_amf -usage transcoding -quality quality -rc cqp -qp_i 20 -qp_p 22 -g 60 -bf 0
  -c:a aac -b:a 192k -af "aresample=async=1:first_pts=0" -fps_mode cfr -r 30
  -movflags +frag_keyframe+empty_moov+default_base_moof
  -shortest -shortest_buf_duration 2
  -f mp4 -y "<out>"
```
**Índices de input:** fontes de `-filter_complex` **não são inputs**, então o pipe é o input **0** → `-map 0:a`. No Tier B (`gdigrab` como input 0) o pipe é o **1** → `-map 1:a`. `BuildArguments` **calcula** os índices; nunca hardcode.

Se `-h encoder=hevc_amf` listar `d3d11` nos pix_fmts, remover `hwdownload,format=bgra,format=nv12` e alimentar a superfície D3D11 direto (ganho real quando captura e encode estão no mesmo adaptador). Caminho `hwdownload` é o default conhecido-bom (~8-10% de um core em 1280×720) e é o que vale quando o monitor está na iGPU e o encode na dedicada.

Tiers B/C: `gdigrab` → `hevc_amf`, e `gdigrab` → `libx264 -crf 20` como último recurso. `vsrc_amf` (captura AMF nativa, só a partir do FFmpeg ~9.0) fica como Tier 0 **somente sob sonda**, desligado por padrão.

### Desligamento limpo — três mecanismos, usar os três
1. **`-shortest` + fechar o stdin.** `StopSegmentAsync` para de encaminhar PCM e fecha `StandardInput.BaseStream`; o input de áudio atinge EOF e o ffmpeg desmonta o filtergraph e escreve o trailer. *(Escrever `q` no stdin é impossível aqui: o stdin carrega o áudio.)*
2. **MP4 fragmentado.** `+frag_keyframe+empty_moov+default_base_moof` dispensa o átomo `moov` final → **o arquivo toca mesmo se o processo for morto**. Elimina a classe inteira de "matei o ffmpeg e perdi 20 min". Depois, `RemuxAsync` (`-c copy -movflags +faststart`) devolve um MP4 normal e navegável de graça.
3. **`Kill()` após 8 s de graça**, agora inofensivo por causa de (2).

*Rejeitado explicitamente:* `GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT)` — exige `CREATE_NEW_PROCESS_GROUP` (que `Process.Start` não pede, obrigando um `CreateProcess` manual com herança dos 3 pipes) **e** que o pai tenha console, o que um WinUI `WinExe` não tem. Documentar a rejeição para ninguém re-litigar.

**No escritor do pipe:** `WriteAudio` é chamado da thread de captura do NAudio e **precisa ser não-bloqueante com descarte por backpressure** — se travar num stdin cheio, a captura WASAPI engasga e corrompe *a gravação e o detector*. `Channel<byte[]>` limitado (~200 buffers ≈ 10 s) com `BoundedChannelFullMode.DropOldest`, uma task dedicada drenando, contador de descartes no log. Só encaminhar PCM com um segmento aberto.

**Sync A/V:** iniciar a task escritora *antes* do `Process.Start` retornar, `aresample=async=1:first_pts=0`, e uma calibração única de `AudioOffsetSeconds` → `-itsoffset`, guardada no perfil.

---

## SendInput

### Pré-requisito: corrigir o bug de DPI do `OverlayWindow`
`OverlayWindow` mistura espaços de coordenadas: `MoveAndResize` usa **px físicos** (`OverlayWindow.xaml.cs:63`) mas `ScreenGeometry.Rect` recebe esses números num rect de **DIP** (linha 41), e `GetCurrentPoint().Position` devolve DIP direto para `MainWindow.xaml.cs:184`, que soma o `MonitorBounds.X` **físico**. A 100% DPI (o caso atual: 96 DPI nos dois monitores) DIP == físico e nada quebra; a 125%/150% todo retângulo calibrado sai errado pelo fator de escala. **A calibração inteira depende disso**, então corrigir primeiro: capturar `XamlRoot.RasterizationScale`, dividir as dimensões por ele no XAML, e multiplicar de volta no `SelectionCompleted`. Compatível com os dois chamadores atuais (ambos já tratam o rect como pixels de `RawPngBytes`).

### Espaço de coordenadas — os três caminhos concordam
Manifest é `PerMonitorV2` (`app.manifest:16`), logo `CopyFromScreen`, `Screen.AllScreens`, `GetSystemMetrics(SM_*VIRTUALSCREEN)`, `GetCursorPos`, `SendInput` absoluto e `ddagrab`/`gdigrab` são **todos px físicos**. A única superfície em DIP é o XAML, e o fix acima a isola. Guardar todo rect do perfil como px físicos de virtual desktop e nunca mais converter.

### Declarações e matemática
`INPUT`/`MOUSEINPUT`/`KEYBDINPUT`/`HARDWAREINPUT` com `INPUTUNION` em `LayoutKind.Explicit`. Pontos que silenciosamente não funcionam se errados:
- **`MOUSEEVENTF_VIRTUALDESK` (0x4000) é obrigatório.** Sem ele, coordenadas absolutas mapeiam **só no monitor primário** e o DISPLAY1 (em `X=-1920`) fica **inalcançável**.
- **`cbSize` = `Marshal.SizeOf<INPUT>()`** (40 em x64, 28 em x86), nunca literal — o csproj declara `x86;x64;ARM64`. `cbSize` errado faz `SendInput` retornar 0 sem efeito visível. Sempre checar o retorno e `GetLastWin32Error()`.
- `dwExtraInfo = 0x4F435257` ('OCRW') em todo evento sintético, para distinguir nosso input do humano.

```
nx = round((x - vs.X) * 65535 / (vs.Width  - 1))
ny = round((y - vs.Y) * 65535 / (vs.Height - 1))
```
Com `vs = (-1920, 0, 4480, 1440)`: `(1280,700) → (46825, 31878)`; `(-1600,540) → (4682, 24590)`. Resolução de ~0,068 px/unidade na horizontal → erro de arredondamento ≤ ±1 px. **Logar** o resíduo de `GetCursorPos` após o move, mas **não corrigir** com um nudge relativo: deltas relativos passam pela aceleração de ponteiro e podem passar do alvo.

### Movimento e clique
Caminho com ease-in-out (`t = 0.5 - 0.5·cos(π·i/n)`), ~14 passos, arco perpendicular de ±4 px, jitter de ±1 px, `Task.Delay(8..22 ms)` entre passos (~110-310 ms total). Depois ~90 ms de assentamento (muitos players só habilitam o botão de avançar no hover), `LEFTDOWN`, `Task.Delay(45..110)`, `LEFTUP` — **duas chamadas separadas de `SendInput`** com delay real, não um array batched: pressão de 0 ms é um tell clássico e alguns handlers JS fazem debounce.

**Restaurar o cursor depois do `VerifyingAdvance` dar certo**, não logo após o clique: restaurar cedo arranca o ponteiro da barra de controle e pode cancelar a UI dependente de hover.

**Aborto por interferência humana:** antes de cada ação sintética, comparar `GetCursorPos` com onde deixamos o cursor; desvio > 50 px = o humano pegou o mouse → `Aborted`. Barato, sem hooks, e é o modo de falha que de fato acontece.

---

## Loop de detecção

| Estado | Tick | Congelamento | Áudio | Template | OCR |
|---|---|---|---|---|---|
| `Recording` / `DetectingEnd` | 400 ms | ✔ | ✔ | ✔ | ✘ |
| `Confirming` | 200 ms | ✔ | ✔ | ✔ | **✔** |
| `VerifyingAdvance` | 400 ms | ✔ | ✔ | ✔ | ✘ |

400 ms é o ponto certo: o debounce de 3 ticks ainda reage ~2 s após o fim, e o detector todo custa < 2% de um core. **OCR só em `Confirming`** — num vídeo de 20 min, zero ou uma chamada. Template matching (~1-3 ms numa ROI de algumas centenas de px) roda todo tick e é o sinal de maior especificidade, então não é gated.

Nunca capturar tela cheia no loop: `FreezeRegion` + as `SearchRegion` dos alvos, total < 1 MP.

### Correções necessárias no código existente
1. **`RegionGrabber` em `ScreenCaptureHelper.cs`.** `CaptureRegionAsync` aloca `Bitmap`+`Graphics`+`MemoryStream` e **codifica PNG** a cada chamada. A 2,5 Hz × 3 ROIs × 6 h são ~160 000 encodes PNG e ~480 000 objetos GDI. Adicionar uma classe `IDisposable` que reusa `Bitmap`/`Graphics`, faz o downscale **no GDI** (para 160×90, de modo que pixels em resolução plena nunca chegam ao managed) e devolve BGRA via `LockBits`. PNG só para OCR e previews. **Deixar `CaptureRegionAsync` intacto** — as outras duas abas usam.
2. **`OcrEngineHost.cs` — vazamento de GPU.** `VisionDetector.cs:17` e `OcrRecognizer.cs:14` guardam `[ThreadStatic] RapidOCRSharp`, e ambos entram por `Task.Run` → threads do pool. **Cada thread do pool que roda OCR constrói um `ExecutionProviderDirectML` novo** e carrega os dois ONNX na GPU, e nada nunca é disposto. Hoje é invisível (um OCR por clique); com `Confirming` repetido por horas, vaza até o DirectML falhar em alocar. Correção mínima: `OcrEngineHost.RunAsync` numa única thread dedicada e longeva, substituindo o `Task.Run` nos dois arquivos (2 linhas cada). `[ThreadStatic]` passa a cachear exatamente um engine, e o OCR serializa — que é o correto de todo modo, já que inferência DirectML concorrente numa GPU não ajuda. Melhoria estrita também para as abas existentes.
3. **`GlobalHotkeyManager` para N hotkeys.** Hoje é `_hotkeyId = 1` fixo com um evento sem parâmetro. Manter `Register`/`Unregister`/`HotkeyPressed` **com as mesmas assinaturas** reservando o id 1 para a API legada (`MainWindow.xaml.cs:55-57` não muda), e adicionar `RegisterHotkey(mod, key, Action) → int` com um `Dictionary<int, Action>`. Acrescentar `MOD_NOREPEAT = 0x4000` e **usá-lo** no hotkey de emergência (sem ele, segurar a tecla inunda de `WM_HOTKEY`). Guardar o `RegisterClass` com um `static bool` (hoje uma segunda instância falha com `ERROR_CLASS_ALREADY_EXISTS`, inofensivo mas sujo no log). Registrar o hotkey de emergência **na instância existente**, não numa nova.
   Candidatos em ordem (registro pode falhar contra o que o usuário já tem): `Ctrl+Alt+Shift+K`, `Ctrl+Alt+Shift+F9`, `Ctrl+Alt+Shift+Pause`. **Evitar F12** — debuggers reivindicam, deixando o botão de pânico indisponível exatamente durante o desenvolvimento. Mostrar na UI qual combinação registrou de fato.
4. **`_isSelectingForDynamic` → `SelectionPurpose`.** Generalizar o booleano de `MainWindow.xaml.cs:25` num enum (`StaticOcr`, `DynamicScroll`, `AwVideoRegion`, `AwFreezeRegion`, `AwTarget`) e dar um `switch` em `Overlay_SelectionCompleted` (linha ~180). Mudança de 3 linhas que não perturba as duas abas atuais.

---

## Calibração ("ensinar a interface")

Inline na aba, reusando o `OverlayWindow` **verbatim** (depois do fix de DPI). Passos como botões, com uma lista mostrando ✓/pendente:

1. **Novo/Carregar perfil** (`ContentDialog` para o nome).
2. **Área do vídeo** → overlay. Guarda `VideoRegion` em coords de virtual desktop (`MonitorBounds.X + rect.X`, exatamente como a linha 184 já faz), `MonitorBounds`, e roda `MatchOutput` para preencher `DdaAdapterIndex`/`DdaOutputIndex`.
3. **Área de congelamento** (opcional). Default = `VideoRegion` **sem os 12% de baixo**. Isso importa: a barra de progresso e o timestamp do player animam continuamente, então incluí-los faz o `IsStatic` **nunca** disparar e mata o sinal (a) em silêncio.
4. **Ensinar botão "Próximo"** → recorta `next.png` do `RawPngBytes` (a captura pristina pré-overlay daquele monitor, sem a máscara escura), define `SearchRegion` = rect inflado em 60 px e limitado ao monitor, `ClickOffset` = centro, e roda `VisionDetector.DetectRegionsAsync` no recorte para **pré-preencher `ExpectedText`**.
5. **Ensinar ícone(s) de fim** — repetível, `Role = EndIcon`, `Mode = Template` (glifos não têm texto).
6. **Ensinar ícone de carregamento** (opcional) → `Role = Loading`. É um **supressor**: se casar, congelamento+silêncio é buffering, não fim. Este alvo único remove o falso positivo mais comum.
7. **Ensinar "pular anúncio"/"fechar"** (opcional) → `Role = Dismiss`.
8. **Área do título** (opcional) → `Role = Title`, `Mode = Ocr`, OCR uma vez por segmento para nomear o arquivo.
9. **"Testar detecção agora"** — roda uma passada completa e escreve **o número bruto de cada sinal** e o score combinado no log, desenhando uma mira onde *clicaria*. É o controle mais valioso da aba: automação visual é intunável sem ele.
10. **Salvar perfil.**

**Modo de correspondência: os dois, por alvo, com defaults por função.** Template é rápido (~1-3 ms) e preciso mas quebra com zoom do navegador, troca de tema claro/escuro e mudança de renderização de fonte; OCR é robusto a isso mas é inútil para ícones pictográficos e custa 100-400 ms. Logo: `EndIcon`/`Loading` → `Template`; `NextButton`/`Dismiss` → `TemplateOrOcr` (template primeiro, OCR só como fallback e só em `Confirming`); `Title` → `Ocr`. `ComboBox` por alvo para sobrescrever.

---

## Aba XAML

Terceiro `<TabViewItem Header="Gravação Automática" IsClosable="False">` em [MainWindow.xaml](OCRWin/MainWindow.xaml), com raiz `ScrollViewer` > `StackPanel`, no estilo das duas existentes. Seções: **Perfil** (`AwProfileComboBox` + Novo/Salvar/Excluir) · **FFmpeg** (`AwFfmpegStatusTextBlock`, Localizar/Baixar/Testar recursos + `AwDownloadProgressBar`) · **1. Calibração** (os botões de ensinar + `AwTargetsListView` com thumbnails e um editor do alvo selecionado: função, modo, texto esperado, limiar) · **2. Detecção e limites** (`NumberBox` de duração mín/máx, máx. de vídeos, dBFS de silêncio; `ToggleSwitch` de exigir ícone; codec, FPS, pasta de saída) · **3. Execução** (`AwTestDetectionButton`, `AwTestClickButton`, `AwTestRecordButton`, **`AwMonitorOnlyButton`**, `AwStartSessionButton` com `AccentButtonStyle`, `AwStopSessionButton`) · **Sinais ao vivo** (4 `ProgressBar`: congelamento, silêncio, ícone, score combinado, cada uma com o valor numérico ao lado) · **Preview + log** (`AwLiveThumbnail` + `AwOverlayCanvas` para mira/caixas, e `AwLogListView` em Consolas) · **`AwSegmentsListView`** (duplo-clique abre o arquivo).

Handlers em `MainWindow.AutoWatch.cs`, todos `async void` com `try/catch` escrevendo `ex.Message` em `AwStatusTextBlock` — a convenção da casa.

> Desvio deliberado: `AutoWatchSession` é um `async Task RunAsync(CancellationToken)` com `await Task.Delay(tick, ct)`, iniciado da thread de UI (continuações caem no dispatcher, sem marshalling). `DispatcherTimer` é a convenção do repo, mas expressar 10 estados com timeouts, contadores de retry e duas janelas de confirmação aninhadas como callbacks de timer produz código bem pior. O repo **não tem nenhum `CancellationToken`** hoje: introduzir **só aqui** (um loop de 6 h desassistido com parada de emergência genuinamente precisa) e não retrofitar o resto.

---

## NuGet e limpeza

Adicionar **só** em [OCRWin.Core.csproj](OCRWin.Core/OCRWin.Core.csproj):
```xml
<PackageReference Include="NAudio.Wasapi" Version="2.3.0" />
```
Puxa `NAudio.Core 2.3.0` transitivamente; **ambos já estão no cache local** (verificado) → restore offline. Deliberadamente **não** o metapacote `NAudio`, que arrasta MIDI/ASIO/WinForms. `System.Text.Json` é in-box no net8. Nada para o `SendInput`.

*Rejeitados:* `FFMpegCore`/`Xabe.FFmpeg` (wrappers para transcodificação de *arquivo*; não dão controle do grafo filter-source + PCM no stdin nem do shutdown por fechamento de stdin) e `FFmpeg.AutoGen` (libav* in-process + DLLs — estritamente pior que dirigir `ffmpeg.exe`).

Enquanto estiver no csproj: remover os `Sdcb.Paddle*` mortos (~400 MB de payload nativo inútil) e `OCRWin.Core/Class1.cs`. Recomendo também `PublishTrimmed=False` em [OCRWin.csproj](OCRWin/OCRWin.csproj) — NAudio usa COM interop, OpenCvSharp P/Invoke, OnnxRuntime/RapidOCR reflexão: o publish Release provavelmente já está quebrado, e esta feature levaria a culpa.

---

## Riscos principais

| # | Risco | Mitigação |
|---|---|---|
| R1 | **Overlay de hardware (MPO/DirectComposition) → frames pretos** em GDI *e* DXGI | **Portão da Fase 0.** Flags do navegador; senão plano B (`Windows.Graphics.Capture`) |
| R2 | Duas GPUs AMD → adaptador/saída errado grava a tela errada **sem erro** | Matriz de sonda adaptador×saída, persistida no perfil, monitor resolvido exibido na UI |
| R3 | Sleep de display / tela de bloqueio mata o DDA no meio da sessão | `SetThreadExecutionState(…DISPLAY_REQUIRED|SYSTEM_REQUIRED)` |
| R4 | Engine OCR `[ThreadStatic]` duplicado por thread do pool → OOM de GPU | `OcrEngineHost` |
| R5 | Loopback WASAPI não entrega nada com endpoint idle → buracos e drift A/V | Silence keeper (`WasapiOut` + `SilenceProvider`) |
| R6 | Banner de cookie / "ainda está assistindo?" / overlay de anúncio cobre o botão | Alvos `Loading`/`Dismiss`, 3 retries, PNG de diagnóstico, depois `Error` em vez de lixo silencioso |
| R7 | Escritor PCM travando num stdin cheio corrompe gravação **e** detector | `Channel` limitado com `DropOldest`, task dedicada, `-thread_queue_size 4096`, contador de descartes |
| R8 | Navegador bloqueia autoplay no vídeo seguinte → congelado+silencioso na hora | `MinVideoSeconds` + trava de armação + clique de fallback no `PlayButton` |
| R9/R10 | Anúncios, ou vídeo legitimamente estático e silencioso (slide, transição preta) | `MinVideoSeconds`, supressor `Loading`, debounce de 3 ticks + janela de 2 s, `RequireIcon` quando houver ícone |
| R11 | Exaustão de disco — 2560×1440@30 CQP20 ≈ 4-12 GB/h | Precheck e recheck por segmento, total visível na UI |
| R12 | Playback em tela cheia exclusiva muda a geometria | Exigir janela; detectar "estático para sempre e nenhum ícone casou" e abortar com mensagem clara |
| R13 | `SendInput` descartado por UIPI se o navegador rodar elevado e nós não | Detectar em `Preparing` e recusar iniciar, explicando |
| R14 | Modo empacotado (MSIX): `%LOCALAPPDATA%` redirecionado; loopback pode exigir `<DeviceCapability Name="microphone"/>` | Mostrar o caminho resolvido na UI; testar o perfil Packaged explicitamente |

---

## Ordem de construção

- **Fase 0** — go/no-go (acima). Tudo depende destes dois resultados.
- **Fase 1** — `AppPaths`, `AutoWatchProfile` + `ProfileStore`, a aba XAML, `SelectionPurpose`, **o fix de DPI do `OverlayWindow`**, fluxo de calibração completo. *Entrega:* criar perfil, ensinar 3 alvos, salvar, fechar o app, recarregar e ver os recortes e rects.
- **Fase 2** — `InputSimulator` + "Testar clique", isolado. Testar contra Paint nos **dois** monitores, especialmente o de X negativo. *Entrega:* clica no alvo ensinado e restaura o cursor.
- **Fase 3** — `FfmpegLocator`, `FfmpegCapabilities`, `VideoRecorder` sem áudio; "Gravar 10 s". *Entrega:* três arquivos de 10 s (Tier A/B/C) comparáveis em qualidade, CPU e tamanho.
- **Fase 4** — `AudioLoopbackMonitor` + silence keeper, canal PCM, layout de dois inputs, `-shortest` + fechar stdin, MP4 fragmentado, `RemuxAsync`, calibrar `AudioOffsetSeconds`. *Entrega:* um MP4 de 60 s sincronizado, mais um deliberadamente `Kill`ado que ainda toca.
- **Fase 5** — `FrameChangeDetector`, `VisualTargetMatcher`, `AudioSessionMeter`, `EndOfVideoCombiner`, `OcrEngineHost`, e **`AwMonitorOnlyButton`**: roda o loop completo de detecção streamando todos os sinais, **sem gravar nem clicar**. Assistir 3-4 vídeos reais acabarem e **ajustar todos os limiares aqui, onde errar não custa nada**. É a fase onde a feature é ganha ou perdida.
- **Fase 6** — refactor do `GlobalHotkeyManager`, hotkey de emergência com lista de candidatos, poll de Esc, aborto por desvio de cursor, `SetThreadExecutionState`. **Provar os três caminhos de aborto antes de deixar o orquestrador clicar.**
- **Fase 7** — `AutoWatchSession`. Primeira rodada com `MaxVideos = 2`, depois 5, depois desassistido.
- **Fase 8** — retries, `session.log`/`session.json`, OCR de título para nomes, caminhos `Dismiss`/`Loading`/`PlayButton`, pasta `diag`, checks de espaço, `AwSegmentsListView`.

---

## Verificação

**Testes unitários (sem hardware)** — valem porque são exatamente os dois bugs que custam um dia cada:
- `ToAbsolute` com `vs = (-1920, 0, 4480, 1440)`: `(1280,700) → (46825, 31878)`, `(-1920,0) → (0,0)`, `(2559,1439) → (65535,65535)`.
- `BuildArguments`: `offset_x = 640` no Tier A no primário; `offset_x = 320` para X virtual -1600 no DISPLAY1; `-map 0:a` no Tier A e `-map 1:a` no Tier B.
- `Marshal.SizeOf<INPUT>()` == 40 em x64.

**`InputSimulator`** — Paint maximizado no monitor **esquerdo** (X negativo): "Testar clique" num ponto conhecido deve marcar ±2 px. Depois **comentar `MOUSEEVENTF_VIRTUALDESK`** e confirmar que o clique cai no primário — é a prova de que a flag é carga crítica. Confirmar que o cursor volta à posição salva.

**Áudio** — logar `Format` na inicialização (esperado nesta máquina: IeeeFloat/48000/2/32). Música → pico em -6..-20 dBFS; pausa → abaixo de -60 dentro da janela. **Prova do R5:** com o silence keeper desligado, parar todo áudio 30 s e observar `DataAvailable` parar de disparar; religar e ver continuar. **Prova da medição por sessão:** `PeakFor("firefox.exe")` com o navegador tocando > 0, e com áudio tocando em **outro** app permanecendo ~0. **Sync A/V:** gravar 30 s de uma página que pisca branco, medir o offset num player frame-accurate, ajustar `AudioOffsetSeconds`, regravar, confirmar < 40 ms.

**Shutdown do recorder** — parada normal: arquivo toca, navegável após `RemuxAsync`, duração = relógio ±1 s. **Prova do R7/A1:** `End task` no ffmpeg no meio de um segmento → **o MP4 fragmentado ainda toca** até o corte. `StopSegmentAsync` com graça de 0 s (força o Kill) → mesmo resultado.

**Detecção (o teste que importa)** — `AwMonitorOnlyButton` sobre 4 vídeos reais; exportar `session.log` e checar, por vídeo: o score cruzou `ConfirmScore` dentro de 3 s do fim real? Houve cruzamento *antes* do fim? **Ajustar `FreezeMadThreshold`, `SilenceDb` e os pesos pelos valores brutos logados, não por chute.** Negativos: transição preta no meio do vídeo (congelado+silencioso, sem ícone) → score deve ficar abaixo de `ConfirmScore`; pausar manualmente → *deve* disparar, confirmar se é o desejado. Buffering: estrangular a rede para forçar o spinner → com o alvo `Loading` ensinado, o combinador deve reportar `"buffering"` e não avançar. Custo: cronometrar 1000 ticks, afirmar média < 15 ms e **zero OCR fora de `Confirming`** (contador).

**Hotkeys e aborto** — Ctrl+E continua funcionando (regressão em `MainWindow.xaml.cs:44-60`). O hotkey de emergência dispara de dentro do navegador com nossa janela minimizada. Segurar a tecla → exatamente um evento (`MOD_NOREPEAT`). Esc durante a sessão → `Aborted`; pegar o mouse durante `Advancing` → `Aborted`. **Os três caminhos devem deixar:** ffmpeg parado, arquivo tocável, cursor restaurado, janela restaurada, `ES_CONTINUOUS` chamado, hotkey desregistrado.

**Ponta a ponta** — `MaxVideos = 2` assistido; depois 5 desassistido com a tela em paz por uma hora — é o que pega R3 (sleep), R4 (vazamento de GPU), R11 (disco) e o churn de handles GDI. Acompanhar memória de GPU e contagem de handles ao longo da rodada. Round-trip de perfil: salvar, matar o app, reiniciar, carregar, rodar → comportamento idêntico. Rodar o perfil **Packaged** uma vez e confirmar onde o JSON caiu (R14) e se o loopback ainda funciona.

---

## Arquivos críticos

- [OCRWin/MainWindow.xaml](OCRWin/MainWindow.xaml) — terceiro `TabViewItem`
- [OCRWin/MainWindow.xaml.cs](OCRWin/MainWindow.xaml.cs) — `_isSelectingForDynamic` → `SelectionPurpose` (linhas 25, 180-195); registro do hotkey de emergência (44-60). Handlers novos vão no partial `MainWindow.AutoWatch.cs`
- [OCRWin/OverlayWindow.xaml.cs](OCRWin/OverlayWindow.xaml.cs) — fix DIP/físico nas linhas 41, 63, 96; **toda a calibração depende disso**
- [OCRWin/GlobalHotkeyManager.cs](OCRWin/GlobalHotkeyManager.cs) — mapa id→handler, `MOD_NOREPEAT`, `Register`/`Unregister` retrocompatíveis
- [OCRWin.Core/ScreenCaptureHelper.cs](OCRWin.Core/ScreenCaptureHelper.cs) — adicionar `RegionGrabber`; **não** mexer em `CaptureRegionAsync`
- [OCRWin.Core/VisionDetector.cs](OCRWin.Core/VisionDetector.cs) — vazamento `[ThreadStatic]` na linha 17; é também o ponto de entrada de OCR que o `VisualTargetMatcher` chama
- [OCRWin.Core/OcrRecognizer.cs](OCRWin.Core/OcrRecognizer.cs) — mesmo vazamento na linha 14
- [OCRWin.Core/ScrollStitcher.cs](OCRWin.Core/ScrollStitcher.cs) — padrão de template matching e ciclo de vida de `Mat` a copiar
- [OCRWin.Core/OCRWin.Core.csproj](OCRWin.Core/OCRWin.Core.csproj) — `NAudio.Wasapi 2.3.0`; remover `Sdcb.Paddle*`
