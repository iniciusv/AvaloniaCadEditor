using System;
using System.Globalization;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Application.Commands;

namespace AvaloniaCadEditor.Application.Services;

public class VcbInputService : IVcbInputService
{
    private string _inputBuffer = "";

    public string InputBuffer => _inputBuffer;

    public void Clear()
    {
        _inputBuffer = "";
    }

    public bool ProcessKeyDown(string keyName, Sketch sketch, ICommandTool? currentTool, Action? onValueEntered)
    {
        string upperKey = keyName.ToUpperInvariant();

        if (IsNumericKey(upperKey, out char numChar))
        {
            _inputBuffer += numChar;
            return true;
        }
        else if (upperKey == "BACK" && _inputBuffer.Length > 0)
        {
            _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
            return true;
        }
        else if ((upperKey == "RETURN" || upperKey == "ENTER") && _inputBuffer.Length > 0)
        {
            if (double.TryParse(_inputBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                currentTool?.OnValueEntered(sketch, value);
                onValueEntered?.Invoke();
            }
            Clear();
            return true;
        }

        return false;
    }

    private bool IsNumericKey(string keyName, out char result)
    {
        result = '\0';
        if (keyName.Length == 2 && keyName.StartsWith("D") && char.IsDigit(keyName[1]))
        {
            result = keyName[1];
            return true;
        }
        if (keyName.StartsWith("NUMPAD") && keyName.Length == 7 && char.IsDigit(keyName[6]))
        {
            result = keyName[6];
            return true;
        }
        if (keyName == "OEMPERIOD" || keyName == "DECIMAL" || keyName == "OEMCOMMA")
        {
            result = '.';
            return true;
        }
        return false;
    }
}
