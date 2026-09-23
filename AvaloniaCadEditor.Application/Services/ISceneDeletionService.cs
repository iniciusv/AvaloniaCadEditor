using System;
using System.Linq;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Services;

public interface ISceneDeletionService
{
    void DeleteSelected(Sketch sketch, Guid? modelTreeSelectedId);
}
