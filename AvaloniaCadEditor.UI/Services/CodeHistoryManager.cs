using System.Collections.Generic;

namespace AvaloniaCadEditor.UI.Services;

public class CodeHistoryManager
{
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public void SaveState(string currentState)
    {
        if (_undoStack.Count == 0 || _undoStack.Peek() != currentState)
        {
            _undoStack.Push(currentState);
            _redoStack.Clear();
        }
    }

    /// <summary>
    /// Descarta o último estado guardado se ele for <paramref name="state"/>: quem desfez a mudança por outro
    /// caminho (o desfazer de vários arquivos) não deixa um passo repetido no histórico do arquivo.
    /// </summary>
    public void DiscardLastIf(string state)
    {
        if (_undoStack.Count > 0 && _undoStack.Peek() == state) _undoStack.Pop();
    }

    public bool TryUndo(string currentState, out string previousState)
    {
        if (_undoStack.Count > 0)
        {
            _redoStack.Push(currentState);
            previousState = _undoStack.Pop();
            return true;
        }
        previousState = string.Empty;
        return false;
    }

    public bool TryRedo(string currentState, out string nextState)
    {
        if (_redoStack.Count > 0)
        {
            _undoStack.Push(currentState);
            nextState = _redoStack.Pop();
            return true;
        }
        nextState = string.Empty;
        return false;
    }
}
