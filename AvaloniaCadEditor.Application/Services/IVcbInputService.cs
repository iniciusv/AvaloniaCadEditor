using System;
using System.Globalization;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Application.Commands;

namespace AvaloniaCadEditor.Application.Services;

public interface IVcbInputService
{
    string InputBuffer { get; }
    void Clear();
    bool ProcessKeyDown(string keyName, Sketch sketch, ICommandTool? currentTool, Action? onValueEntered);
}
