/// <summary>
/// Suporte impresso para fixar um motor NEMA 17 (ver <see cref="MotorNema17"/>) em cima de uma
/// extrusão de alumínio 2020 (ver <see cref="Extrusao2020"/>).
/// Placa centralizada em X e Y: a face frontal do motor (lado do boss/eixo) encosta na face
/// de baixo da placa, com o eixo passando por um furo de alívio central; os 4 furos de fixação
/// do motor (padrão 31 x 31 mm) atravessam a placa para os parafusos chegarem aos furos
/// rosqueados (cegos) do motor. Por baixo, 2 furos passantes alinhados ao furo central da
/// extrusão (ao longo do seu comprimento) permitem aparafusar a placa diretamente nela.
/// A origem (0, 0, 0) local fica na face de baixo da placa (encosto na extrusão), centralizada
/// em X e Y.
/// </summary>
public sealed record SuporteMotorNema17(
    double Largura = 45,
    double Comprimento = 45,
    double Espessura = 6,
    double DiametroFuroEixo = 26,
    double PassoFurosMotor = 31,
    double DiametroFuroMotor = 3.4,
    double PassoFurosExtrusao = 20,
    double DiametroFuroExtrusao = 5.5) : Part
{
    protected override IFeature Build()
    {
        var baseCorner = new Point3D(-Largura / 2, -Comprimento / 2, 0);
        var corpo = new BoxFeature(baseCorner, Largura, Comprimento, Espessura) { Name = "corpo" };

        var cortes = new List<IFeature>
        {
            new CylinderFeature(new Point3D(0, 0, 0), Vector3D.UnitZ, DiametroFuroEixo / 2, Espessura)
            {
                Name = "furo_alivio_eixo",
                IsTransientTool = true
            }
        };

        var i = 0;
        foreach (var sinalX in new[] { -1.0, 1.0 })
        {
            foreach (var sinalY in new[] { -1.0, 1.0 })
            {
                var furo = new CylinderFeature(
                    new Point3D(sinalX * PassoFurosMotor / 2, sinalY * PassoFurosMotor / 2, 0),
                    Vector3D.UnitZ,
                    DiametroFuroMotor / 2,
                    Espessura)
                {
                    Name = $"furo_motor_{i}",
                    IsTransientTool = true
                };
                cortes.Add(furo);
                i++;
            }
        }

        foreach (var sinalY in new[] { -1.0, 1.0 })
        {
            var furo = new CylinderFeature(
                new Point3D(0, sinalY * PassoFurosExtrusao / 2, 0),
                Vector3D.UnitZ,
                DiametroFuroExtrusao / 2,
                Espessura)
            {
                Name = $"furo_extrusao_{(sinalY < 0 ? "a" : "b")}",
                IsTransientTool = true
            };
            cortes.Add(furo);
        }

        return BooleanFeature.CreateDifference("suporte_motor_nema17", corpo, cortes.ToArray());
    }
}
