using System;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Primitiva CSG nativa de um tronco de cone, guardada em coordenadas globais (como
/// <see cref="CylinderFeature"/>).
/// <para>
/// <see cref="TopRadius"/> = 0 dá um cone completo: o topo vira um ápice e a face de topo não
/// existe. <see cref="TopRadius"/> igual a <see cref="BottomRadius"/> dá um cilindro. O caso
/// prático mais comum é o furo escareado — base maior na superfície, topo menor no fundo.
/// </para>
/// <para>
/// Ao contrário do cilindro (que o kernel baixa para a extrusão de um polígono de 32 lados), o
/// sólido gerado aqui é um cone EXATO: a aproximação existe só na malha de exibição, não no modelo.
/// </para>
/// </summary>
public class ConeFeature : FeatureBase
{
    /// <summary>Tolerância abaixo da qual um raio é tratado como um ponto (ápice).</summary>
    public const double ApexTolerance = 1e-9;

    // Modelo matemático em coordenadas globais
    /// <summary>Centro da face de baixo.</summary>
    public Point3D GlobalBaseCenter { get; set; }

    /// <summary>Eixo, da base para o topo. Normalizado no construtor.</summary>
    public Vector3D GlobalDirection { get; set; }

    /// <summary>Raio da face de baixo, em <see cref="GlobalBaseCenter"/>.</summary>
    public double BottomRadius { get; set; }

    /// <summary>Raio da face de cima; 0 = cone completo (ápice, sem face de topo).</summary>
    public double TopRadius { get; set; }

    /// <summary>Altura medida ao longo de <see cref="GlobalDirection"/>. Pode ser negativa.</summary>
    public double Height { get; set; }

    /// <summary>
    /// Derivada de <see cref="TopRadius"/>: true quando o topo é um ponto, e portanto não há face
    /// de topo para selecionar. Não precisa entrar no <see cref="FeatureFingerprint"/> por si só
    /// (<see cref="TopRadius"/> já entra).
    /// </summary>
    public bool IsFullCone => TopRadius <= ApexTolerance;

    /// <summary>
    /// Cria um tronco de cone diretamente em coordenadas globais. Como as demais primitivas
    /// (<see cref="CylinderFeature"/>, <see cref="BoxFeature"/>, <see cref="PrismFeature"/>), não
    /// valida os parâmetros: valores degenerados são barrados no kernel, que devolve malha vazia.
    /// </summary>
    public ConeFeature(Point3D globalBaseCenter, Vector3D globalDirection, double bottomRadius, double topRadius, double height, string? name = null)
    {
        GlobalBaseCenter = globalBaseCenter;
        GlobalDirection = globalDirection.Normalize();
        BottomRadius = bottomRadius;
        TopRadius = topRadius;
        Height = height;
        Name = name ?? $"Cone (R{bottomRadius}, r{topRadius}, H{height})";
    }

    /// <summary>
    /// Centro da face. Num cone completo, <see cref="FaceType.Top"/> devolve o ápice — continua
    /// sendo um ponto útil para posicionar outra feature, mesmo sem face plana ali.
    /// </summary>
    public Point3D GetFaceCenter(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Bottom => GlobalBaseCenter,
            FaceType.Top => new Point3D(
                GlobalBaseCenter.X + GlobalDirection.X * Height,
                GlobalBaseCenter.Y + GlobalDirection.Y * Height,
                GlobalBaseCenter.Z + GlobalDirection.Z * Height),
            _ => GlobalBaseCenter
        };
    }

    public Vector3D GetFaceNormal(FaceType faceType)
    {
        return faceType switch
        {
            FaceType.Top => GlobalDirection,
            FaceType.Bottom => new Vector3D(-GlobalDirection.X, -GlobalDirection.Y, -GlobalDirection.Z),
            _ => GlobalDirection
        };
    }

    /// <summary>
    /// Qual tampa tem esta normal, ou null para a lateral (curva) e para o topo de um cone completo
    /// (que não existe). Duas diferenças em relação ao cilindro:
    /// <list type="bullet">
    /// <item>sem face de topo quando <see cref="IsFullCone"/>;</item>
    /// <item>a lateral de um cone é INCLINADA. Num cone raso (base larga, pouca altura) a normal da
    /// lateral chega perto do eixo e passaria pela tolerância das tampas — por isso a normal só vira
    /// tampa se estiver mais perto do eixo do que da própria lateral.</item>
    /// </list>
    /// </summary>
    public FaceType? GetFaceTypeFromNormal(Vector3D normal)
    {
        const double tol = 0.99;
        double axial = normal.DotProduct(GlobalDirection);

        double dr = BottomRadius - TopRadius;
        double slant = Math.Sqrt(Height * Height + dr * dr);
        if (slant > ApexTolerance)
        {
            double lateralAxial = dr / slant;
            double toCap = Math.Min(Math.Abs(axial - 1.0), Math.Abs(axial + 1.0));
            if (Math.Abs(axial - lateralAxial) < toCap) return null;
        }

        if (axial > tol) return IsFullCone ? null : FaceType.Top;
        if (axial < -tol) return FaceType.Bottom;
        return null;
    }

    /// <summary>
    /// Centro da base e centro do topo (o ápice, num cone completo) — os mesmos dois índices de
    /// <see cref="CylinderFeature.GetVertices"/>, para que uma referência a vértice se comporte igual.
    /// </summary>
    public override IReadOnlyList<Point3D> GetVertices()
    {
        var bottomCenter = GetFaceCenter(FaceType.Bottom);
        var topCenter = GetFaceCenter(FaceType.Top);
        return new List<Point3D> { bottomCenter, topCenter }.AsReadOnly();
    }
}
