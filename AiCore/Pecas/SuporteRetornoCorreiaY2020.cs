/// <summary>
/// Cantoneira em L impressa (ABS) presa na ponta da extrusão 2020 sob o trilho Y, onde a correia Y volta:
/// a aba_rasgo oferece a mesma conexão da parede do <see cref="BlocoFicacaoBeltVaoCorreia"/>, um rasgo
/// vertical, para um <see cref="SuporteRolamentosCorreia"/> com os rolamentos de retorno.
/// <para>
/// Origem local (0, 0, 0) na aresta de baixo da face externa da extrusão, na ponta dela: x = 0 na face
/// externa, y = 0 na face da ponta (a extrusão fica em y &lt;= 0), z = 0 na base da extrusão. Os eixos são
/// paralelos aos da cena. "Fora" é a distância da face externa para longe da extrusão: −X sem espelho
/// (lado A), +X com EspelhadoNoEixoX (lado B, a imagem pelo plano x = 0).
/// </para>
/// As duas abas são verticais, com Espessura, e a aresta do L também:
/// - aba_extrusao: encosta na face externa da extrusão e vai de RecuoAbaExtrusao atrás da ponta até a
///   aba_rasgo. Fica abaixo do topo da extrusão, porque o carrinho Y passa por cima dela. Presa por 2 M3
///   no canal lateral (porca T): com Espessura = 6 e EngateTNut = 4, um M3×10.
/// - aba_rasgo: de face de apoio em y = YParede (voltada para −Y, para a haste do C), da face externa da
///   extrusão até MargemParede além do rasgo. O rasgo vertical fica em "fora" = ForaEixo, centrado em
///   ZRasgo, com RasgoMeiaAltura de curso para cada lado; a aba sobe MargemRasgo acima dele. O M3 entra
///   pela frente (+Y), passa pelo rasgo e rosqueia na porca da haste do C.
/// O contorno externo leva um chanfro de RaioChanfro, cortado antes dos furos e do rasgo.
/// </summary>
public sealed record SuporteRetornoCorreiaY2020(
    double ForaEixo = 18.5,           // da face externa da extrusão ao plano do rasgo (e do eixo dos rolamentos)
    double YParede = 18.5,            // da ponta da extrusão à face de apoio da aba_rasgo
    double ZRasgo = 45.5,             // meio do rasgo, da base da extrusão
    double Espessura = 6.0,
    double AlturaExtrusao = 20.0,
    double FolgaTopoExtrusao = 0.5,   // da aba_extrusao abaixo do topo da extrusão
    double RecuoAbaExtrusao = 40.0,   // da aba_extrusao para trás da ponta da extrusão
    double DistanciaPrimeiroFuro = 10.0, // da ponta da extrusão ao primeiro M3 do canal
    double PassoFurosExtrusao = 20.0,
    double MargemParede = 10.0,       // da aba_rasgo além do rasgo, para fora
    double MargemRasgo = 4.0,         // da aba_rasgo acima do rasgo
    double DiametroFuro = 3.4,
    double RasgoMeiaAltura = 8.0,
    double RaioChanfro = 1.0,
    bool EspelhadoNoEixoX = false) : Part
{
    /// <summary>Topo da aba_rasgo.</summary>
    public double ZTopo => ZRasgo + RasgoMeiaAltura + DiametroFuro / 2.0 + MargemRasgo;

    protected override IFeature Build()
    {
        // "Fora" vira X com o sinal do lado: −X no lado A, +X no lado B.
        var sx = EspelhadoNoEixoX ? -1.0 : 1.0;
        double X(double fora) => -sx * fora;
        // Função local: só CALCULA o canto de menor X de uma faixa de "fora" (o sólido é declarado abaixo).
        double CantoX(double fora0, double fora1) => System.Math.Min(X(fora0), X(fora1));

        var raioFuro = DiametroFuro / 2.0;
        var foraFimParede = ForaEixo + MargemParede;
        if (ForaEixo - raioFuro <= Espessura)
        {
            throw new System.ArgumentOutOfRangeException(nameof(ForaEixo), ForaEixo,
                $"SuporteRetornoCorreiaY2020: o rasgo precisa ficar fora da aba_extrusao ({Espessura:0.###} mm).");
        }

        var yFrente = YParede + Espessura;
        var aba_extrusao = new BoxFeature(new Point3D(CantoX(0, Espessura), -RecuoAbaExtrusao, 0), Espessura, RecuoAbaExtrusao + yFrente, AlturaExtrusao - FolgaTopoExtrusao) { Name = "aba_extrusao" };
        var aba_rasgo = new BoxFeature(new Point3D(CantoX(0, foraFimParede), YParede, 0), foraFimParede, Espessura, ZTopo) { Name = "aba_rasgo" };
        var cantoneira = BooleanFeature.CreateUnion("cantoneira", aba_extrusao, aba_rasgo);

        // Chanfro só no L bruto: as bocas dos furos e do rasgo ficam vivas. A aresta côncava do L é pulada.
        IFeature corpo = cantoneira;
        if (RaioChanfro > 0.001)
        {
            var cantoneira_chanfrada = new FilletFeature("cantoneira_chanfrada", cantoneira, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = cantoneira_chanfrada;
        }

        // M3 no canal lateral da extrusão (meia altura), entrando por fora e atravessando a aba.
        var zCanal = AlturaExtrusao / 2.0;
        var direcaoParaExtrusao = new Vector3D(sx, 0, 0);
        var furo_extrusao_1 = new CylinderFeature(new Point3D(X(Espessura + 1.0), -DistanciaPrimeiroFuro, zCanal), direcaoParaExtrusao, raioFuro, Espessura + 2.0) { Name = "furo_extrusao_1", IsTransientTool = true };
        var furo_extrusao_2 = new CylinderFeature(new Point3D(X(Espessura + 1.0), -DistanciaPrimeiroFuro - PassoFurosExtrusao, zCanal), direcaoParaExtrusao, raioFuro, Espessura + 2.0) { Name = "furo_extrusao_2", IsTransientTool = true };

        // Rasgo vertical (stadium) na aba_rasgo, atravessando-a em Y: a caixa dá o trecho reto e os dois
        // cilindros arredondam as pontas.
        var xRasgo = X(ForaEixo);
        var rasgo = new BoxFeature(new Point3D(xRasgo - raioFuro, YParede - 1.0, ZRasgo - RasgoMeiaAltura), DiametroFuro, Espessura + 2.0, 2.0 * RasgoMeiaAltura) { Name = "rasgo", IsTransientTool = true };
        var cilindro_rasgo_superior = new CylinderFeature(new Point3D(xRasgo, YParede - 1.0, ZRasgo + RasgoMeiaAltura), Vector3D.UnitY, raioFuro, Espessura + 2.0) { Name = "cilindro_rasgo_superior", IsTransientTool = true };
        var cilindro_rasgo_inferior = new CylinderFeature(new Point3D(xRasgo, YParede - 1.0, ZRasgo - RasgoMeiaAltura), Vector3D.UnitY, raioFuro, Espessura + 2.0) { Name = "cilindro_rasgo_inferior", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_retorno_correia_y_2020", corpo,
            furo_extrusao_1, furo_extrusao_2,
            rasgo, cilindro_rasgo_superior, cilindro_rasgo_inferior);
    }
}
