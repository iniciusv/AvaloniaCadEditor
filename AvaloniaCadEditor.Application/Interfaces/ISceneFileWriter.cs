using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Export;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>Grava uma <see cref="ExportScene"/> num formato de troca (ex.: COLLADA para o SketchUp).</summary>
public interface ISceneFileWriter
{
    Task WriteAsync(ExportScene scene, string path);
}
