/// <summary>
/// Tubo de seção retangular com parede de espessura constante e as quatro quinas longitudinais
/// arredondadas. A quina externa tem raio RaioQuina; a interna, RaioQuina − Espessura (com os
/// padrões, 2 − 2 = 0: quina interna viva).
/// Origem local (0, 0, 0) no canto inferior da seção; X = largura, Y = comprimento (eixo do tubo),
/// Z = altura — mesma convenção da Extrusao2020. As pontas são abertas.
/// DiametroFuroPassante maior que 0 abre um furo vertical (em Z) pelas duas paredes, no centro da seção e no
/// meio do comprimento, ou seja, no ponto local (Largura / 2, Comprimento / 2); 0 = sem furo.
/// DiametroFurosPar maior que 0 abre dois furos verticais (em Z) pelas duas paredes, a DistanciaFurosPar um do
/// outro ao longo do tubo e centrados no meio do comprimento, com o centro a RecuoFurosPar da face x = Largura;
/// 0 = sem os furos.
/// </summary>
public sealed record TuboRetangular(
    double Comprimento = 200,
    double Largura = 50,
    double Altura = 30,
    double Espessura = 2,
    double RaioQuina = 2,
    double DiametroFuroPassante = 0,
    double DiametroFurosPar = 0,
    double DistanciaFurosPar = 0,
    double RecuoFurosPar = 0) : Part
{
    protected override IFeature Build()
    {
        var raioInterno = RaioQuina - Espessura;

        // Arredonda o envelope ANTES de ocar: o fillet só vê as 4 arestas longitudinais do bloco.
        var envelope = new BoxFeature(Point3D.Origin, Largura, Comprimento, Altura) { Name = "envelope" };
        var envelope_arredondado = new FilletFeature("envelope_arredondado", envelope, RaioQuina);
        envelope_arredondado.AddEdge(new EdgeReference(envelope, EdgeType.TopLeft));
        envelope_arredondado.AddEdge(new EdgeReference(envelope, EdgeType.TopRight));
        envelope_arredondado.AddEdge(new EdgeReference(envelope, EdgeType.BottomLeft));
        envelope_arredondado.AddEdge(new EdgeReference(envelope, EdgeType.BottomRight));

        // O vão passa 1 mm além das duas pontas, para abri-las sem faces coincidentes.
        var vao = new BoxFeature(new Point3D(Espessura, -1, Espessura), Largura - 2 * Espessura, Comprimento + 2, Altura - 2 * Espessura) { Name = "vao", IsTransientTool = true };
        IFeature vaoFinal = vao;
        if (raioInterno > 0.001)
        {
            var vao_arredondado = new FilletFeature("vao_arredondado", vao, raioInterno) { IsTransientTool = true };
            vao_arredondado.AddEdge(new EdgeReference(vao, EdgeType.TopLeft));
            vao_arredondado.AddEdge(new EdgeReference(vao, EdgeType.TopRight));
            vao_arredondado.AddEdge(new EdgeReference(vao, EdgeType.BottomLeft));
            vao_arredondado.AddEdge(new EdgeReference(vao, EdgeType.BottomRight));
            vaoFinal = vao_arredondado;
        }

        var tubo = BooleanFeature.CreateDifference("tubo_retangular", envelope_arredondado, vaoFinal);
        if (DiametroFuroPassante > 0.001)
        {
            // Passa 1 mm além das duas faces, para abri-las sem faces coincidentes.
            var furo_passante = new CylinderFeature(new Point3D(Largura / 2, Comprimento / 2, -1), Vector3D.UnitZ, DiametroFuroPassante / 2, Altura + 2) { Name = "furo_passante", IsTransientTool = true };
            tubo = BooleanFeature.CreateDifference("tubo_furo_passante", tubo, furo_passante);
        }

        if (DiametroFurosPar > 0.001)
        {
            var xFurosPar = Largura - RecuoFurosPar;
            var furo_par_a = new CylinderFeature(new Point3D(xFurosPar, Comprimento / 2 - DistanciaFurosPar / 2, -1), Vector3D.UnitZ, DiametroFurosPar / 2, Altura + 2) { Name = "furo_par_a", IsTransientTool = true };
            var furo_par_b = new CylinderFeature(new Point3D(xFurosPar, Comprimento / 2 + DistanciaFurosPar / 2, -1), Vector3D.UnitZ, DiametroFurosPar / 2, Altura + 2) { Name = "furo_par_b", IsTransientTool = true };
            tubo = BooleanFeature.CreateDifference("tubo_furos_par", tubo, furo_par_a, furo_par_b);
        }

        return tubo;
    }
}
