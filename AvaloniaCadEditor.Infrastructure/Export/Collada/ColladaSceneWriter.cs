using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using AvaloniaCadEditor.Application.Export;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Infrastructure.Export.Collada;

/// <summary>Grava a cena em COLLADA 1.4.1 (<c>.dae</c>), o formato que o SketchUp importa com grupos e componentes.</summary>
public sealed class ColladaSceneWriter : ISceneFileWriter
{
    public async Task WriteAsync(ExportScene scene, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var document = ColladaDocument.Build(scene, DateTime.UtcNow);

        var settings = new XmlWriterSettings { Async = true, Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        await using var stream = File.Create(path);
        await using var writer = XmlWriter.Create(stream, settings);
        await document.SaveAsync(writer, default);
    }
}
