using System;
using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Ai.Context;

/// <summary>
/// Ajustes manuais do usuário sobre o contexto calculado: arquivos fixados (entram mesmo fora da
/// seleção) e excluídos (não entram). Fixar e excluir o mesmo arquivo são mutuamente exclusivos.
/// </summary>
public sealed class AiContextAdjustments
{
    private readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Pinned => _pinned;
    public IReadOnlySet<string> Excluded => _excluded;

    public bool IsPinned(string filePath) => _pinned.Contains(filePath);

    public void Pin(string filePath)
    {
        _excluded.Remove(filePath);
        _pinned.Add(filePath);
    }

    public void TogglePin(string filePath)
    {
        if (!_pinned.Remove(filePath)) Pin(filePath);
    }

    public void Exclude(string filePath)
    {
        _pinned.Remove(filePath);
        _excluded.Add(filePath);
    }

    public void Reset()
    {
        _pinned.Clear();
        _excluded.Clear();
    }
}
