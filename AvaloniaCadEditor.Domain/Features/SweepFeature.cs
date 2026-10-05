using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Varredura de uma seção ao longo de um caminho de retas e arcos: tubo dobrado, barra curvada,
/// eletrocalha, anel.
/// <para>
/// O caminho (<see cref="Path"/>) manda na posição e na orientação; a seção
/// (<see cref="SweepSection"/>) dá só a forma, e é transportada de estação em estação. O
/// transporte é paralelo — ao atravessar um arco, o referencial gira junto com o sólido que o
/// <c>Revolve</c> produz — então não há torção acumulada e a face final de um trecho nasce em cima
/// da face inicial do próximo.
/// </para>
/// <para>
/// O kernel constrói cada reta com <c>Extrude</c> e cada arco com <c>Revolve</c> (ambos exatos: num
/// arco, o plano da seção contém o eixo de revolução por construção) e funde os trechos. Uma seção
/// circular, porém, é um polígono de ProfileSegments lados, igual ao que
/// <see cref="CylinderFeature"/> já faz.
/// </para>
/// <para>
/// Para um tubo OCO, subtraia uma segunda varredura de mesmo caminho e seção menor. Para emendar
/// outra coisa na ponta, <see cref="EndCenter"/> e <see cref="EndTangent"/> dizem onde ela começa.
/// </para>
/// </summary>
public class SweepFeature : FeatureBase
{
    /// <summary>Abaixo disto um comprimento é tratado como zero (sólido degenerado).</summary>
    public const double DegenerateTolerance = 1e-9;

    /// <summary>A forma varrida.</summary>
    public SweepSection Section { get; set; }

    /// <summary>Os trechos percorridos.</summary>
    public SweepPath Path { get; set; }

    /// <summary>
    /// Onde a varredura começa e como a seção está girada ali: a origem é o primeiro ponto do
    /// caminho, a normal é o rumo inicial, e os eixos X/Y são para onde vão o u e o v da seção.
    /// </summary>
    public WorkPlane StartPlane { get; set; }

    /// <summary>Ponto do caminho onde a varredura começa.</summary>
    public Point3D StartCenter => StartPlane.Origin;

    /// <summary>Rumo do caminho no início.</summary>
    public Vector3D StartTangent => StartPlane.Normal;

    /// <summary>Ponto do caminho onde a varredura termina — onde encaixar o próximo trecho.</summary>
    public Point3D EndCenter => GetStations()[^1].Origin;

    /// <summary>Rumo do caminho no fim — a orientação do próximo trecho.</summary>
    public Vector3D EndTangent => GetStations()[^1].Normal;

    /// <summary>
    /// Varredura por um caminho qualquer. <paramref name="upReference"/> fixa a rotação da seção em
    /// torno do caminho (o v da seção aponta para lá); sem ela, o referencial sai da convenção
    /// padrão de <see cref="WorkPlane"/>. Para uma seção circular tanto faz.
    /// </summary>
    public SweepFeature(SweepSection section, SweepPath path, Vector3D? upReference = null, string? name = null)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        Path = path ?? throw new ArgumentNullException(nameof(path));

        var start = path.Start;
        var tangent = path.StartTangent;

        if (upReference is { } up)
        {
            // WorkPlane(origem, normal, xAxis) deriva YAxis = normal × xAxis; com xAxis = up × normal,
            // o YAxis sai sendo 'up' projetado perpendicular ao rumo, que é o que se quer dizer com
            // "esse lado da seção aponta para cima".
            var x = up.CrossProduct(tangent);
            StartPlane = x.Length < DegenerateTolerance
                ? new WorkPlane(tangent, start)
                : new WorkPlane(start, tangent, x);
        }
        else
        {
            StartPlane = new WorkPlane(tangent, start);
        }

        Name = name ?? $"Sweep ({path.Segments.Count} trecho(s))";
    }

    /// <summary>
    /// Varredura de UM arco a partir de um perfil já posicionado: o plano do perfil diz onde o
    /// caminho começa e para onde aponta, e os vértices dão a forma.
    /// </summary>
    public SweepFeature(IProfile section, Vector3D bendDirection, double bendRadius, double angleDegrees, string? name = null)
    {
        if (section == null) throw new ArgumentNullException(nameof(section));

        Section = SweepSection.FromProfile(section);
        Path = SweepPath.Arc(section.Plane.Origin, section.Normal, bendDirection, bendRadius, angleDegrees);
        StartPlane = section.Plane;
        Name = name ?? $"Sweep (R{bendRadius}, {angleDegrees}°)";
    }

    /// <summary>
    /// Varredura de UM arco com seção CIRCULAR (tubo maciço, barra dobrada). Monta o perfil no
    /// plano que começa em <paramref name="start"/> e aponta para <paramref name="startTangent"/>,
    /// do mesmo jeito que o kernel monta o de um cilindro.
    /// </summary>
    public SweepFeature(
        Point3D start,
        Vector3D startTangent,
        double sectionRadius,
        Vector3D bendDirection,
        double bendRadius,
        double angleDegrees,
        string? name = null)
        : this(
            new CircleProfile(Point3D.Origin, sectionRadius, plane: new WorkPlane(startTangent, start)),
            bendDirection,
            bendRadius,
            angleDegrees,
            name)
    {
    }

    /// <summary>
    /// O referencial da seção no começo de cada trecho, mais o do fim do último — são
    /// <c>Segments.Count + 1</c> estações. O transporte mora em <see cref="SweepPath.Stations"/>,
    /// para que a geometria e a conferência de fechamento de um caminho fechado usem exatamente a
    /// mesma conta.
    /// </summary>
    public IReadOnlyList<WorkPlane> GetStations() => Path.Stations(StartPlane);

    /// <summary>
    /// Os pontos do caminho, estação por estação. Para um arco só são dois — começo e fim, os
    /// mesmos dois índices de <see cref="CylinderFeature.GetVertices"/>.
    /// </summary>
    public override IReadOnlyList<Point3D> GetVertices()
    {
        var stations = GetStations();
        var points = new List<Point3D>(stations.Count);
        foreach (var station in stations) points.Add(station.Origin);
        return points.AsReadOnly();
    }
}
