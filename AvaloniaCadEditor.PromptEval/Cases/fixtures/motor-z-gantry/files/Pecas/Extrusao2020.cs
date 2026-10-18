using System.Collections.Generic;

/// <summary>
/// Perfil de extrusão de alumínio 2020 (20 x 20 mm, seção V-slot/T-slot padrão), com furo central
/// passante ao longo de todo o comprimento.
/// Origem local (0, 0, 0) no canto inferior da seção; X = largura (20 mm), Y = comprimento
/// (direção de extrusão), Z = altura (20 mm) — mesma convenção dos trilhos (TrilhoMgn9/TrilhoMgn15).
/// A seção é fixa em 20 x 20 mm (catálogo); só o comprimento varia por instância.
/// </summary>
public sealed record Extrusao2020(double Comprimento = 200, double DiametroFuroCentral = 6) : Part
{
    public double Largura => 20;
    public double Altura => 20;

    protected override IFeature Build()
    {
        var perfil = new PolygonProfile(new List<Point3D>
        {
            new Point3D(1.707, 0, 1),
            new Point3D(6, 0, 1),
            new Point3D(6, 0, 0),
            new Point3D(0, 0, 0),
            new Point3D(0, 0, 6),
            new Point3D(1, 0, 6),
            new Point3D(1, 0, 1.707),
            new Point3D(6, 0, 6.707),
            new Point3D(6, 0, 13.293),
            new Point3D(1, 0, 18.293),
            new Point3D(1, 0, 14),
            new Point3D(0, 0, 14),
            new Point3D(0, 0, 20),
            new Point3D(6, 0, 20),
            new Point3D(6, 0, 19),
            new Point3D(1.707, 0, 19),
            new Point3D(6.707, 0, 14),
            new Point3D(13.293, 0, 14),
            new Point3D(18.293, 0, 19),
            new Point3D(14, 0, 19),
            new Point3D(14, 0, 20),
            new Point3D(20, 0, 20),
            new Point3D(20, 0, 14),
            new Point3D(19, 0, 14),
            new Point3D(19, 0, 18.293),
            new Point3D(14, 0, 13.293),
            new Point3D(14, 0, 6.707),
            new Point3D(19, 0, 1.707),
            new Point3D(19, 0, 6),
            new Point3D(20, 0, 6),
            new Point3D(20, 0, 0),
            new Point3D(14, 0, 0),
            new Point3D(14, 0, 1),
            new Point3D(18.293, 0, 1),
            new Point3D(13.293, 0, 6),
            new Point3D(6.707, 0, 6),
        });

        var corpo = new ExtrudeFeature(perfil, Comprimento, new Vector3D(0, Comprimento, 0)) { Name = "corpo" };

        var furoCentral = new CylinderFeature(
            new Point3D(Largura / 2, -1, Altura / 2),
            Vector3D.UnitY,
            DiametroFuroCentral / 2,
            Comprimento + 2)
        {
            Name = "furo_central",
            IsTransientTool = true
        };

        return BooleanFeature.CreateDifference("extrusao_2020", corpo, furoCentral);
    }
}
