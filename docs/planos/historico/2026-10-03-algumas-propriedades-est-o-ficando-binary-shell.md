# Fix: Nome de propriedade cortado pelo campo numérico

## Context

No `PropertyInspectorView`, o DataTemplate de `PartParameters` usa um `Grid` com `ColumnDefinitions="100,*"`. O `TextBlock` do nome da propriedade ocupa a coluna de 100px fixa, sem `TextTrimming` configurado. Nomes mais longos que ~12 caracteres (a 11pt) são simplesmente cortados na borda da coluna, dando a impressão de que o campo de entrada "interrompeu" o nome.

## Arquivo a modificar

[AvaloniaCadEditor.UI/Views/PropertyInspectorView.axaml](AvaloniaCadEditor.UI/Views/PropertyInspectorView.axaml) — linhas 83–85

## Mudança

Adicionar `TextTrimming="CharacterEllipsis"` ao `TextBlock` do nome do parâmetro:

**Antes (linha 84–85):**
```xml
<TextBlock Grid.Column="0" Text="{Binding Name}" ToolTip.Tip="{Binding TypeName}"
           Foreground="#CCCCCC" FontSize="11" VerticalAlignment="Center" />
```

**Depois:**
```xml
<TextBlock Grid.Column="0" Text="{Binding Name}" ToolTip.Tip="{Binding TypeName}"
           Foreground="#CCCCCC" FontSize="11" VerticalAlignment="Center"
           TextTrimming="CharacterEllipsis" />
```

O `ToolTip.Tip` já está vinculado a `TypeName`; pode-se também vinculá-lo ao próprio `Name` para que o usuário veja o nome completo ao passar o mouse. Isso é opcional mas melhora a UX para nomes longos.

## Verificação

Abrir o app, selecionar uma peça com parâmetros de nomes longos (ex.: `conexaoMotor`, `larguraBanco`, etc.) no inspetor de propriedades e confirmar que os nomes exibem `...` ao invés de serem cortados abruptamente.
