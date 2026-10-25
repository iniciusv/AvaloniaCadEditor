# Botões de vista padrão (Topo / Frente / Lateral / Iso) + Perspectiva/Ortográfica

## Contexto
Hoje a câmera só muda por orbit/pan/zoom com o mouse. Queremos botões minimalistas centralizados no topo
para pular para vistas padrão (Topo, Frente, Lateral, Isométrica) e alternar entre projeção perspectiva e
ortográfica. Os controles existentes (OSnap + ⚙, Hidden Edges, Guides) passam para o lado direito da barra.

Convenção do app: Z para cima; `CameraState.Position` = Target + D·(cosE·sinA, cosE·cosA, sinE).

## 1. `AvaloniaCadEditor.Domain/Entities/CameraState.cs`
- Novo enum `StandardView { Top, Front, Right, Isometric }` (mesmo arquivo ou `Domain/Entities/StandardView.cs`).
- `public bool IsOrthographic { get; set; }`.
- **`Up` passa a ser calculado** a partir de azimute/elevação: `(-sinE·sinA, -sinE·cosA, cosE)`.
  Necessário porque a vista de Topo (E = 90°) deixa `CreateLookAt` degenerado com Up fixo em Z. Em E=0 o
  resultado continua sendo +Z, e `Pan` já usa `Up` via cross product, então segue funcionando.
- `GetProjectionMatrix()`: se `IsOrthographic`, retorna
  `Matrix4x4.CreateOrthographic(h * AspectRatio, h, -FarPlane, FarPlane)` com `h = 2·Distance·tan(Fov/2)`
  (mesmo enquadramento da perspectiva no plano do Target, então zoom pela roda continua funcionando via
  `Distance`; near negativo evita cortar geometria atrás da câmera ao dar zoom).
  `ScreenPointToRay` e `ScreenProjection.WorldToScreen` já usam a matriz inversa/W, então funcionam sem mudança.
- `SetStandardView(StandardView v)` define Azimuth/Elevation (sem passar pelo clamp do `Orbit`):
  - Front: A = π, E = 0 (câmera em −Y olhando +Y)
  - Right: A = π/2, E = 0 (câmera em +X)
  - Top: A = π, E = π/2 (X para a direita, Y para cima na tela)
  - Isometric: A = 3π/4, E = atan(1/√2) ≈ 35,26° (câmera em +X −Y +Z, iso verdadeira, azimute 45°)
  Target e Distance são mantidos.

## 2. `AvaloniaCadEditor.UI/Controls/SilkViewportControl.cs`
- `public void SetStandardView(StandardView v)` → `_camera.SetStandardView(v); RequestNextFrameRendering();`
- `public void SetOrthographic(bool on)` → idem.
(Estado da câmera fica na view, como hoje; não passa pelo ViewModel.)

## 3. `AvaloniaCadEditor.UI/Views/MainWindow.axaml` — toolbar (linhas 18-77)
Trocar o `StackPanel` horizontal por `Grid ColumnDefinitions="*,Auto,*"`:
- Coluna 1 (centro): `StackPanel` horizontal com botões pequenos e sem fundo (`Background="Transparent"`,
  `Padding="8,2"`, `FontSize="12"`, hover sutil) — **Top · Front · Right · Iso**, um separador vertical fino,
  e um `ToggleButton` **Persp/Ortho** (`IsChecked` = ortográfica).
- Coluna 2 (direita, `HorizontalAlignment="Right"`): o conteúdo atual inalterado (OSnap+flyout, Hidden Edges,
  Guides+Analyze+status).
- Coluna 0 fica vazia (só para manter o grupo central realmente centralizado).
- Botões usam `Click="ViewTop_Click"` etc. e `Tag` com o nome da vista, ou um único handler lendo `Tag`.

## 4. `AvaloniaCadEditor.UI/Views/MainWindow.axaml.cs`
- `StandardView_Click(object sender, RoutedEventArgs e)`: lê `((Button)sender).Tag` → `Enum.Parse<StandardView>` →
  `ViewportControl.SetStandardView(...)`.
- `Projection_Click`: `ViewportControl.SetOrthographic(toggle.IsChecked == true)`.

## 5. Textos — `Resources/Strings.resx` + `Resources/Strings.cs`
Adicionar `ViewTop`, `ViewFront`, `ViewRight`, `ViewIso`, `ProjectionOrtho` (+ tooltips) seguindo o padrão
existente (`public static string X => ResourceManager.GetString("X") ?? "..."`). Labels em inglês, como o
resto do resx atual.

## Verificação
1. `dotnet build` da solução.
2. Rodar o app, criar/importar uma peça:
   - Top/Front/Right/Iso mudam a vista corretamente; Top não fica preto/degenerado e mostra X→direita, Y→cima.
   - Orbit/pan/zoom continuam funcionando depois de cada vista (inclusive saindo do Topo).
   - Toggle Ortho: sem distorção de perspectiva, zoom pela roda funciona, seleção por clique (raycast),
     OSnap, desenho de linha e arrasto de altura do Extrude continuam acertando o ponto sob o cursor.
   - Rótulos das Reference Guides continuam alinhados em ambas as projeções.
3. Redimensionar a janela: grupo de vistas continua centralizado, controles à direita.
