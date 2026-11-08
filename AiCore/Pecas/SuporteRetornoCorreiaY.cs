/// <summary>
/// Suporte impresso (ABS) na ponta da extrusão 2020 sob o trilho Y, onde a correia Y volta: uma base
/// encostada na face externa da extrusão e uma parede em pé na ponta, com um rasgo vertical para o
/// <see cref="SuporteRolamentosCorreia"/> dos rolamentos de retorno (a mesma conexão da parede do
/// <see cref="BlocoFicacaoBeltVaoCorreia"/>).
/// <para>
/// Origem local (0, 0, 0) na aresta de baixo da face externa da extrusão, na ponta dela: x = 0 na face
/// externa, y = 0 na face da ponta (a extrusão fica em y &lt;= 0), z = 0 na base da extrusão. Os eixos são
/// paralelos aos da cena. "Fora" é a distância da face externa para longe da extrusão: −X sem espelho
/// (lado A), +X com EspelhadoNoEixoX (lado B, a imagem pelo plano x = 0).
/// </para>
/// - base: bloco de LarguraBase (fora) × ComprimentoBase (da ponta para trás) × AlturaBase, ao lado da
///   extrusão, com a mesma altura dela. Presa por 2 M3 no canal lateral (porca T), que entram pela face de
///   fora num rebaixo. O rebaixo é tão raso quanto o ComprimentoParafuso permite: o parafuso atravessa a
///   base sob a cabeça e entra EngateExtrusao na extrusão (parede do perfil + porca T).
/// - parede: em cima da base, rente à ponta da extrusão, com EspessuraParede em Y e a largura da base. A face
///   de apoio (y = −EspessuraParede) fica voltada para −Y, para a haste do C. O rasgo vertical fica em
///   "fora" = ForaEixo, centrado em ZRasgo, com RasgoMeiaAltura de curso para cada lado; o M3 entra pela
///   ponta (+Y), passa pelo rasgo e rosqueia na porca da haste do C.
/// O contorno externo leva um chanfro de RaioChanfro, cortado antes dos furos e do rasgo.
/// </summary>
public sealed record SuporteRetornoCorreiaY(
    double ForaEixo = 18.5,           // da face externa da extrusão ao plano do rasgo (e do eixo dos rolamentos)
    double ZRasgo = 45.5,             // meio do rasgo, da base da extrusão
    double LarguraBase = 30.0,        // "fora", da face externa da extrusão
    double ComprimentoBase = 25.0,    // Y, da ponta da extrusão para trás
    double AlturaBase = 20.0,         // a altura da extrusão
    double EspessuraParede = 6.0,
    double AlturaParede = 37.0,       // acima da base
    double DiametroFuro = 3.4,
    double DiametroRebaixo = 6.5,     // cabeça cilíndrica do M3 (5,5) com folga
    double ComprimentoParafuso = 20.0, // M3 do canal (sem a cabeça)
    double EngateExtrusao = 4.0,      // do parafuso além da face da extrusão, até passar a porca T
    double RasgoMeiaAltura = 6.0,
    double MargemRasgo = 3.0,         // mínimo de parede acima do rasgo
    double RaioChanfro = 1.0,
    bool EspelhadoNoEixoX = false) : Part
{
    /// <summary>Topo da parede.</summary>
    public double ZTopo => AlturaBase + AlturaParede;

    protected override IFeature Build()
    {
        // "Fora" vira X com o sinal do lado: −X no lado A, +X no lado B.
        var sx = EspelhadoNoEixoX ? -1.0 : 1.0;
        double X(double fora) => -sx * fora;
        // Função local: só CALCULA o canto de menor X de uma faixa de "fora" (os sólidos são declarados abaixo).
        double CantoX(double fora0, double fora1) => System.Math.Min(X(fora0), X(fora1));

        var raioFuro = DiametroFuro / 2.0;
        if (ForaEixo - raioFuro <= 0 || ForaEixo + raioFuro >= LarguraBase)
        {
            throw new System.ArgumentOutOfRangeException(nameof(ForaEixo), ForaEixo,
                $"SuporteRetornoCorreiaY: o rasgo precisa caber na largura da parede ({LarguraBase:0.###} mm).");
        }

        if (ZRasgo + RasgoMeiaAltura + raioFuro + MargemRasgo > ZTopo || ZRasgo - RasgoMeiaAltura - raioFuro < AlturaBase)
        {
            throw new System.ArgumentOutOfRangeException(nameof(ZRasgo), ZRasgo,
                $"SuporteRetornoCorreiaY: o rasgo precisa caber na parede (z de {AlturaBase:0.###} a {ZTopo:0.###} mm).");
        }

        var base_extrusao = new BoxFeature(new Point3D(CantoX(0, LarguraBase), -ComprimentoBase, 0), LarguraBase, ComprimentoBase, AlturaBase) { Name = "base_extrusao" };
        var parede = new BoxFeature(new Point3D(CantoX(0, LarguraBase), -EspessuraParede, AlturaBase), LarguraBase, EspessuraParede, AlturaParede) { Name = "parede" };
        var corpo_bruto = BooleanFeature.CreateUnion("corpo_bruto", base_extrusao, parede);

        // Chanfro só no corpo bruto: as bocas dos furos e do rasgo ficam vivas. A aresta côncava é pulada.
        IFeature corpo = corpo_bruto;
        if (RaioChanfro > 0.001)
        {
            var corpo_chanfrado = new FilletFeature("corpo_chanfrado", corpo_bruto, RaioChanfro)
            {
                Mode = FilletMode.Bevel,
                ApplyToAllEdges = true
            };
            corpo = corpo_chanfrado;
        }

        // M3 no canal lateral da extrusão (meia altura): entram pela face de fora da base e a atravessam,
        // um em cada metade do comprimento dela.
        var zCanal = AlturaBase / 2.0;
        var direcaoParaExtrusao = new Vector3D(sx, 0, 0);
        // Material sob a cabeça = o que sobra do parafuso depois do engate; o resto da base vira rebaixo.
        var profundidadeRebaixo = System.Math.Max(0.0, LarguraBase - (ComprimentoParafuso - EngateExtrusao));
        var furo_extrusao_1 = new FuroRebaixado(new Point3D(X(LarguraBase), -ComprimentoBase / 4.0, zCanal), direcaoParaExtrusao, LarguraBase, DiametroFuro, DiametroRebaixo, profundidadeRebaixo).Instance("furo_extrusao_1");
        var furo_extrusao_2 = new FuroRebaixado(new Point3D(X(LarguraBase), -ComprimentoBase * 3.0 / 4.0, zCanal), direcaoParaExtrusao, LarguraBase, DiametroFuro, DiametroRebaixo, profundidadeRebaixo).Instance("furo_extrusao_2");

        // Rasgo vertical (stadium) na parede, atravessando-a em Y: a caixa dá o trecho reto e os dois
        // cilindros arredondam as pontas.
        var xRasgo = X(ForaEixo);
        var yAntesParede = -EspessuraParede - 1.0;
        var rasgo = new BoxFeature(new Point3D(xRasgo - raioFuro, yAntesParede, ZRasgo - RasgoMeiaAltura), DiametroFuro, EspessuraParede + 2.0, 2.0 * RasgoMeiaAltura) { Name = "rasgo", IsTransientTool = true };
        var cilindro_rasgo_superior = new CylinderFeature(new Point3D(xRasgo, yAntesParede, ZRasgo + RasgoMeiaAltura), Vector3D.UnitY, raioFuro, EspessuraParede + 2.0) { Name = "cilindro_rasgo_superior", IsTransientTool = true };
        var cilindro_rasgo_inferior = new CylinderFeature(new Point3D(xRasgo, yAntesParede, ZRasgo - RasgoMeiaAltura), Vector3D.UnitY, raioFuro, EspessuraParede + 2.0) { Name = "cilindro_rasgo_inferior", IsTransientTool = true };

        return BooleanFeature.CreateDifference("suporte_retorno_correia_y", corpo,
            furo_extrusao_1, furo_extrusao_2,
            rasgo, cilindro_rasgo_superior, cilindro_rasgo_inferior);
    }
}
