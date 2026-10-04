using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Domain.Features;

/// <summary>
/// Chave estrutural da geometria de uma feature: tipo + parâmetros geométricos + filhos, sem
/// <see cref="IFeature.Id"/> nem nome. Duas features com a mesma chave geram exatamente o mesmo sólido,
/// então o kernel pode reaproveitar a malha entre execuções da cena (cada execução cria Ids novos).
/// <para>
/// Falha segura: tipos que não sabemos descrever por completo (fillet, extrusão de perfil, malha
/// importada, tipos novos) retornam <c>null</c>, e quem chamou simplesmente não usa o cache.
/// Ao adicionar uma propriedade geométrica a uma feature suportada, inclua-a aqui — o teste
/// <c>FeatureFingerprintTests</c> falha se uma propriedade pública ficar de fora.
/// </para>
/// <para>
/// Calculada a cada consulta (nunca guardada no objeto): features podem ser alteradas no lugar
/// (ex.: pelo Inspector) e a chave tem que acompanhar.
/// </para>
/// </summary>
public static class FeatureFingerprint
{
    /// <summary>Chave da geometria de <paramref name="feature"/>, ou null se não for cacheável.</summary>
    public static string? Compute(IFeature feature)
    {
        var sb = new StringBuilder(256);
        return Append(sb, feature) ? sb.ToString() : null;
    }

    private static bool Append(StringBuilder sb, IFeature feature)
    {
        if (feature == null) return false;

        sb.Append(feature.GetType().FullName).Append('{');
        sb.Append(feature.IsEnabled ? 'E' : 'D').Append(';');

        bool ok = feature switch
        {
            BoxFeature box => AppendBox(sb, box),
            CylinderFeature cylinder => AppendCylinder(sb, cylinder),
            ConeFeature cone => AppendCone(sb, cone),
            PrismFeature prism => AppendPrism(sb, prism),
            BooleanFeature boolean => AppendBoolean(sb, boolean),
            FilletFeature fillet => AppendFillet(sb, fillet),
            ExtrudeFeature extrude => AppendExtrude(sb, extrude),
            PartInstance instance => AppendPlacement(sb, instance.Placement) && Append(sb, instance.Definition),
            TransformedFeature transformed => AppendPlacement(sb, transformed.Placement) && Append(sb, transformed.Source),
            _ => false
        };

        sb.Append('}');
        return ok;
    }

    private static bool AppendBox(StringBuilder sb, BoxFeature box)
    {
        AppendPoint(sb, box.BaseCorner);
        AppendDouble(sb, box.Width);
        AppendDouble(sb, box.Length);
        AppendDouble(sb, box.Height);
        AppendVector(sb, box.DirectionX);
        AppendVector(sb, box.DirectionY);
        AppendVector(sb, box.DirectionZ);
        return true;
    }

    private static bool AppendCylinder(StringBuilder sb, CylinderFeature cylinder)
    {
        AppendPoint(sb, cylinder.GlobalBaseCenter);
        AppendVector(sb, cylinder.GlobalDirection);
        AppendDouble(sb, cylinder.Radius);
        AppendDouble(sb, cylinder.Height);
        return true;
    }

    private static bool AppendCone(StringBuilder sb, ConeFeature cone)
    {
        AppendPoint(sb, cone.GlobalBaseCenter);
        AppendVector(sb, cone.GlobalDirection);
        AppendDouble(sb, cone.BottomRadius);
        AppendDouble(sb, cone.TopRadius);
        AppendDouble(sb, cone.Height);
        return true;
    }

    private static bool AppendPrism(StringBuilder sb, PrismFeature prism)
    {
        sb.Append('[');
        foreach (var vertex in prism.Vertices) AppendPoint(sb, vertex);
        sb.Append(']');
        AppendDouble(sb, prism.Height);
        return AppendPlane(sb, prism.Plane);
    }

    private static bool AppendBoolean(StringBuilder sb, BooleanFeature boolean)
    {
        sb.Append(boolean.Operation).Append(';');
        return AppendList(sb, boolean.BaseFeatures) && AppendList(sb, boolean.ToolFeatures);
    }

    /// <summary>
    /// Arestas e faces entram com a geometria da feature a que pertencem (o kernel lê a aresta
    /// dessa feature, que nem sempre é a própria <see cref="FilletFeature.BaseFeature"/>).
    /// </summary>
    private static bool AppendFillet(StringBuilder sb, FilletFeature fillet)
    {
        AppendDouble(sb, fillet.Radius);
        sb.Append(fillet.Mode).Append(';').Append(fillet.ApplyToAllEdges ? 'A' : 'S').Append(';');
        if (!Append(sb, fillet.BaseFeature) || fillet.Edges == null || fillet.Faces == null) return false;

        sb.Append("E[");
        foreach (var edge in fillet.Edges)
        {
            if (edge == null) return false;
            sb.Append(edge.EdgeId).Append(';');
            if (!Append(sb, edge.ParentFeature)) return false;
        }
        sb.Append("]F[");
        foreach (var face in fillet.Faces)
        {
            if (face == null) return false;
            sb.Append(face.FaceId).Append(';');
            AppendVector(sb, face.NormalHint);
            if (!Append(sb, face.ParentFeature)) return false;
        }
        sb.Append(']');
        return true;
    }

    private static readonly HashSet<Type> KnownProfileTypes = new()
    {
        typeof(PolygonProfile), typeof(RectangleProfile), typeof(CircleProfile)
    };

    private static bool AppendExtrude(StringBuilder sb, ExtrudeFeature extrude)
    {
        AppendDouble(sb, extrude.Height);
        AppendVector(sb, extrude.Direction);
        return AppendProfile(sb, extrude.Profile);
    }

    /// <summary>
    /// Só perfis cujos parâmetros conhecemos por completo; os demais ficam fora do cache. Tipo exato:
    /// uma subclasse pode ter parâmetros a mais que não entrariam na chave.
    /// </summary>
    private static bool AppendProfile(StringBuilder sb, IProfile? profile)
    {
        if (profile == null || !KnownProfileTypes.Contains(profile.GetType())) return false;
        sb.Append(profile.GetType().FullName).Append('{');

        bool ok;
        switch (profile)
        {
            case PolygonProfile polygon:
                if (polygon.Vertices == null) return false;
                sb.Append('[');
                foreach (var vertex in polygon.Vertices) AppendPoint(sb, vertex);
                sb.Append(']');
                ok = AppendPlane(sb, polygon.Plane);
                break;
            case RectangleProfile rectangle:
                AppendPoint(sb, rectangle.Center);
                AppendDouble(sb, rectangle.Width);
                AppendDouble(sb, rectangle.Height);
                AppendDouble(sb, rectangle.RotationAngle);
                ok = AppendPlane(sb, rectangle.Plane);
                break;
            case CircleProfile circle:
                AppendPoint(sb, circle.Center);
                AppendDouble(sb, circle.Radius);
                ok = AppendPlane(sb, circle.Plane);
                break;
            default:
                return false;
        }

        sb.Append('}');
        return ok;
    }

    private static bool AppendList(StringBuilder sb, IReadOnlyList<IFeature>? features)
    {
        if (features == null) return false;
        sb.Append('[');
        foreach (var child in features)
        {
            if (!Append(sb, child)) return false;
        }
        sb.Append(']');
        return true;
    }

    private static bool AppendPlane(StringBuilder sb, WorkPlane? plane)
    {
        if (plane == null) return false;
        AppendPoint(sb, plane.Origin);
        AppendVector(sb, plane.Normal);
        AppendVector(sb, plane.XAxis);
        AppendVector(sb, plane.YAxis);
        return true;
    }

    /// <summary>A transformação afim inteira: imagem da origem e dos três eixos.</summary>
    private static bool AppendPlacement(StringBuilder sb, Placement? placement)
    {
        if (placement == null) return false;
        AppendPoint(sb, placement.Apply(Point3D.Origin));
        AppendVector(sb, placement.ApplyVector(new Vector3D(1, 0, 0)));
        AppendVector(sb, placement.ApplyVector(new Vector3D(0, 1, 0)));
        AppendVector(sb, placement.ApplyVector(new Vector3D(0, 0, 1)));
        return true;
    }

    private static void AppendPoint(StringBuilder sb, Point3D p)
    {
        AppendDouble(sb, p.X);
        AppendDouble(sb, p.Y);
        AppendDouble(sb, p.Z);
    }

    private static void AppendVector(StringBuilder sb, Vector3D v)
    {
        AppendDouble(sb, v.X);
        AppendDouble(sb, v.Y);
        AppendDouble(sb, v.Z);
    }

    // Bits exatos: só reaproveita o que é idêntico; valores "quase iguais" viram outra chave (cache miss, nunca erro).
    private static void AppendDouble(StringBuilder sb, double value) =>
        sb.Append(BitConverter.DoubleToInt64Bits(value).ToString("X", CultureInfo.InvariantCulture)).Append(',');
}
