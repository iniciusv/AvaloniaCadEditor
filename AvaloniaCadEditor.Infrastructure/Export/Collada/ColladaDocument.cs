using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using AvaloniaCadEditor.Application.Export;
using AvaloniaCadEditor.Domain.Mesh;

namespace AvaloniaCadEditor.Infrastructure.Export.Collada;

/// <summary>
/// Monta o XML COLLADA 1.4.1 de uma <see cref="ExportScene"/>: componentes em <c>library_nodes</c>
/// (o SketchUp os importa como definições de componente), instâncias com <c>instance_node</c> + <c>matrix</c>,
/// grupos e pastas como <c>node</c>. Unidade em milímetro e Z para cima, como no app.
/// </summary>
internal sealed class ColladaDocument
{
    private static readonly XNamespace Ns = "http://www.collada.org/2005/11/COLLADASchema";

    /// <summary>Símbolo do material dentro de cada geometria (cada uma tem um só).</summary>
    private const string MaterialSymbol = "material";

    private readonly ExportScene _scene;
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly XElement _geometries = new(Ns + "library_geometries");
    private readonly Dictionary<string, string> _materialIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _componentIds = new(StringComparer.Ordinal);

    private ColladaDocument(ExportScene scene) => _scene = scene;

    public static XDocument Build(ExportScene scene, DateTime nowUtc) => new ColladaDocument(scene).Build(nowUtc);

    private XDocument Build(DateTime nowUtc)
    {
        var effects = new XElement(Ns + "library_effects");
        var materials = new XElement(Ns + "library_materials");
        foreach (var material in _scene.Materials)
        {
            var id = NewId("material", material.Name);
            _materialIds[material.Id] = id;
            effects.Add(new XElement(Ns + "effect", new XAttribute("id", id + "-effect"),
                new XElement(Ns + "profile_COMMON",
                    new XElement(Ns + "technique", new XAttribute("sid", "common"),
                        new XElement(Ns + "lambert",
                            new XElement(Ns + "diffuse",
                                new XElement(Ns + "color", $"{Channel(material.R)} {Channel(material.G)} {Channel(material.B)} 1")))))));
            materials.Add(new XElement(Ns + "material", new XAttribute("id", id), new XAttribute("name", material.Name),
                new XElement(Ns + "instance_effect", new XAttribute("url", "#" + id + "-effect"))));
        }

        var components = new XElement(Ns + "library_nodes");
        foreach (var component in _scene.Components)
        {
            var id = NewId("componente", component.Name);
            _componentIds[component.Id] = id;
            components.Add(new XElement(Ns + "node", new XAttribute("id", id), new XAttribute("name", component.Name), new XAttribute("type", "NODE"),
                GeometryInstance(component.Name, component.Mesh, component.MaterialId)));
        }

        var visualScene = new XElement(Ns + "visual_scene", new XAttribute("id", "cena"), new XAttribute("name", "cena"));
        _ids.Add("cena");
        foreach (var node in _scene.Nodes) visualScene.Add(Node(node));

        var root = new XElement(Ns + "COLLADA", new XAttribute("version", "1.4.1"),
            new XElement(Ns + "asset",
                new XElement(Ns + "contributor", new XElement(Ns + "authoring_tool", "AvaloniaCadEditor")),
                new XElement(Ns + "created", nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XElement(Ns + "modified", nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XElement(Ns + "unit", new XAttribute("meter", "0.001"), new XAttribute("name", "millimeter")),
                new XElement(Ns + "up_axis", "Z_UP")),
            effects,
            materials,
            _geometries,
            components,
            new XElement(Ns + "library_visual_scenes", visualScene),
            new XElement(Ns + "scene", new XElement(Ns + "instance_visual_scene", new XAttribute("url", "#cena"))));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private XElement Node(ExportNode node)
    {
        // Ordem do esquema: transformação, instance_geometry, instance_node, nós filhos.
        var element = new XElement(Ns + "node", new XAttribute("id", NewId("no", node.Name)), new XAttribute("name", node.Name), new XAttribute("type", "NODE"),
            new XElement(Ns + "matrix", string.Join(" ", node.Matrix.Select(Number))));

        if (node.Mesh != null)
            element.Add(GeometryInstance(node.Name, node.Mesh, node.MaterialId!));
        if (node.ComponentId != null)
            element.Add(new XElement(Ns + "instance_node", new XAttribute("url", "#" + _componentIds[node.ComponentId])));
        foreach (var child in node.Children)
            element.Add(Node(child));

        return element;
    }

    /// <summary>Escreve a geometria em <c>library_geometries</c> e devolve a referência a ela, com o material ligado.</summary>
    private XElement GeometryInstance(string name, TriangleMesh mesh, string materialId)
    {
        var (positions, indices) = VertexWelder.Weld(mesh);
        var id = NewId("geometria", name);

        _geometries.Add(new XElement(Ns + "geometry", new XAttribute("id", id), new XAttribute("name", name),
            new XElement(Ns + "mesh",
                new XElement(Ns + "source", new XAttribute("id", id + "-posicoes"),
                    new XElement(Ns + "float_array", new XAttribute("id", id + "-posicoes-array"), new XAttribute("count", positions.Length), Join(positions)),
                    new XElement(Ns + "technique_common",
                        new XElement(Ns + "accessor", new XAttribute("source", "#" + id + "-posicoes-array"), new XAttribute("count", positions.Length / 3), new XAttribute("stride", 3),
                            new XElement(Ns + "param", new XAttribute("name", "X"), new XAttribute("type", "float")),
                            new XElement(Ns + "param", new XAttribute("name", "Y"), new XAttribute("type", "float")),
                            new XElement(Ns + "param", new XAttribute("name", "Z"), new XAttribute("type", "float"))))),
                new XElement(Ns + "vertices", new XAttribute("id", id + "-vertices"),
                    new XElement(Ns + "input", new XAttribute("semantic", "POSITION"), new XAttribute("source", "#" + id + "-posicoes"))),
                new XElement(Ns + "triangles", new XAttribute("material", MaterialSymbol), new XAttribute("count", indices.Length / 3),
                    new XElement(Ns + "input", new XAttribute("semantic", "VERTEX"), new XAttribute("source", "#" + id + "-vertices"), new XAttribute("offset", 0)),
                    new XElement(Ns + "p", string.Join(" ", indices))))));

        return new XElement(Ns + "instance_geometry", new XAttribute("url", "#" + id),
            new XElement(Ns + "bind_material",
                new XElement(Ns + "technique_common",
                    new XElement(Ns + "instance_material", new XAttribute("symbol", MaterialSymbol), new XAttribute("target", "#" + _materialIds[materialId])))));
    }

    /// <summary>
    /// <c>xs:ID</c> válido e único no documento: só letras ASCII, dígitos, '_' e '-', começando por letra.
    /// O nome original, com acentos, vai no atributo <c>name</c>.
    /// </summary>
    private string NewId(string prefix, string name)
    {
        var sb = new StringBuilder(prefix).Append('_');
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // ç → c, ã → a
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_');
        }

        var baseId = sb.ToString();
        var id = baseId;
        for (int n = 2; !_ids.Add(id); n++) id = $"{baseId}_{n}";
        return id;
    }

    // Sempre InvariantCulture: em pt-BR o ToString() escreveria vírgula, e o destino leria outro número.
    private static string Number(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Channel(byte value) => (value / 255.0).ToString("0.####", CultureInfo.InvariantCulture);

    private static string Join(float[] values)
    {
        var sb = new StringBuilder(values.Length * 8);
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(values[i].ToString("0.######", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
