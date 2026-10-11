namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// Orientação de estilo do perfil <see cref="SimplePromptProfile"/>: modelos mais limpos, com poucos
/// parâmetros e poucos sólidos. Os exemplos seguem o mesmo estilo.
/// </summary>
public static class SimplePromptRules
{
    public const string Style =
        """
        ESTILO (modelos simples e limpos):
        - Uma peça tem no MÁXIMO 3 parâmetros: só as dimensões principais que alguém realmente mudaria.
          As demais medidas são const dentro do Build() ou derivadas dos parâmetros.
        - Não crie parâmetros "por via das dúvidas", nem bool de opções, [Range], color ou fillet, a menos que
          o pedido peça.
        - Use o mínimo de sólidos: um furo passante é um cilindro com a espessura da peça; prefira BoxFeature,
          CylinderFeature, PrismFeature, SweepFeature e BooleanFeature.
        - Argumentos posicionais curtos: new BoxFeature(Point3D.Origin, 40, 20, 5), Vector3D.UnitZ.
        """;

    public const string ScriptExample =
        """
        var chapa_1 = new BoxFeature(Point3D.Origin, 80, 50, 6) { Name = "chapa_1" };
        AddFeature(chapa_1);
        var furo_1 = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_1", IsTransientTool = true };
        AddFeature(furo_1);
        var furo_2 = new CylinderFeature(new Point3D(70, 40, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_2", IsTransientTool = true };
        AddFeature(furo_2);
        var chapa_furada = BooleanFeature.CreateDifference("chapa_furada", chapa_1, furo_1, furo_2);
        AddFeature(chapa_furada);
        """;

    public const string PartExample =
        """
        public sealed record Suporte(double Largura = 60, double Altura = 40) : Part
        {
            protected override IFeature Build()
            {
                const double espessura = 5;
                const double profundidade = 30;
                const double raioFuro = 3;

                var aba_base = new BoxFeature(Point3D.Origin, Largura, profundidade, espessura) { Name = "aba_base" };
                var aba_vertical = new BoxFeature(Point3D.Origin, Largura, espessura, Altura) { Name = "aba_vertical" };
                var corpo = BooleanFeature.CreateUnion("corpo", aba_base, aba_vertical);
                var furo_1 = new CylinderFeature(new Point3D(Largura / 4, profundidade / 2 + espessura, 0), Vector3D.UnitZ, raioFuro, espessura) { Name = "furo_1", IsTransientTool = true };
                var furo_2 = new CylinderFeature(new Point3D(3 * Largura / 4, profundidade / 2 + espessura, 0), Vector3D.UnitZ, raioFuro, espessura) { Name = "furo_2", IsTransientTool = true };
                return BooleanFeature.CreateDifference("suporte", corpo, furo_1, furo_2);
            }
        }
        """;

    public const string SceneUsingPartExample =
        """
        var suporte_1 = Add(new Suporte());
        var suporte_2 = Add(new Suporte(Largura: 80), at: Placement.At(100, 0, 0));
        """;
}
