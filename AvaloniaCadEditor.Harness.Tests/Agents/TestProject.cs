using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Harness.Tests.Agents;

/// <summary>
/// Projeto mínimo para os testes do harness: a cena com uma instância e a peça em Pecas/ (as
/// regras do CadScript não deixam declarar record no script), os dois no contexto.
/// </summary>
internal static class TestProject
{
    public const string Scene = "var placa_1 = Add(new Placa(), at: Placement.At(10, 0, 0), name: \"placa_1\");\n";

    public const string PlacaFile =
        "public sealed record Placa(double Largura = 50) : Part\n" +
        "{\n" +
        "    protected override IFeature Build() => new BoxFeature(Point3D.Origin, Largura, 30, 5) { Name = \"placa\" };\n" +
        "}\n";

    public static readonly CadScriptContext Context = Build();

    private static CadScriptContext Build()
    {
        var all = new[] { new ScriptDocument("Cena.cs", Scene), new ScriptDocument("Pecas/Placa.cs", PlacaFile) };
        return new CadScriptContext(
            Scene,
            Kind: CadFileKind.SceneEntry,
            FileName: "Cena.cs",
            ProjectDocuments: all,
            Scope: new MultiFileScope(
                all,
                new Dictionary<string, CadFileKind>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cena.cs"] = CadFileKind.SceneEntry,
                    ["Pecas/Placa.cs"] = CadFileKind.PartFile
                },
                Array.Empty<string>()));
    }
}
