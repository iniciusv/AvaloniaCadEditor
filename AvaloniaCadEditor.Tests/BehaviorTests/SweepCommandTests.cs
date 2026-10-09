using System.Linq;
using System.Threading.Tasks;
using AvaloniaCadEditor.Application.Commands;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Infrastructure.Engines;
using AvaloniaCadEditor.Infrastructure.OpenCascade;
using AvaloniaCadEditor.Infrastructure.Scripting;
using AvaloniaCadEditor.Tests.Extensions;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.BehaviorTests;

/// <summary>
/// A ferramenta Sweep varre uma seção por um caminho, e as duas coisas são geometria JÁ DESENHADA:
/// uma região do esboço é a seção, arestas encadeadas são o caminho. Ela não constrói geometria —
/// emite o CadScript equivalente — então os testes conferem o que ela emite (e o que NÃO emite).
/// </summary>
public class SweepCommandTests
{
    private static Sketch NovoSketch() => new();

    private static LineEntity Linha(double x1, double y1, double x2, double y2, WorkPlane? plane = null) =>
        new(new Point3D(x1, y1, 0), new Point3D(x2, y2, 0)) { Plane = plane ?? WorkPlane.XY };

    /// <summary>A cadeia em L de sempre: (0,0) -> (100,0) -> (100,80).</summary>
    private static LineEntity[] CadeiaEmL() => new[] { Linha(0, 0, 100, 0), Linha(100, 0, 100, 80) };

    private static CircleProfile SecaoCircular(double raio) =>
        new(new Point3D(300, 300, 0), raio, plane: WorkPlane.XY);

    /// <summary>Um retângulo 20x10 como polígono livre, desenhado longe da origem.</summary>
    private static PolygonProfile SecaoPoligonal(double cx, double cy) =>
        new(new[]
        {
            new Point3D(cx - 10, cy - 5, 0), new Point3D(cx + 10, cy - 5, 0),
            new Point3D(cx + 10, cy + 5, 0), new Point3D(cx - 10, cy + 5, 0),
        }, plane: WorkPlane.XY);

    /// <summary>Prepara a ferramenta com a escolha já feita; o Enter é que conclui.</summary>
    private static (SweepCommand Cmd, System.Func<string?> Codigo) Preparar(IProfile? secao, params Entity[] caminho)
    {
        var cmd = new SweepCommand(secao, caminho, new MathGeometryEngine());
        string? gerado = null;
        cmd.CodeGenerated += (_, c) => gerado = c;
        return (cmd, () => gerado);
    }

    /// <summary>Prepara a ferramenta SEM escolha, com as entidades no esboço para serem clicadas.</summary>
    private static (SweepCommand Cmd, System.Func<string?> Codigo, Sketch Sketch) Acionada(params Entity[] entidades)
    {
        var sketch = NovoSketch();
        foreach (var e in entidades) sketch.AddEntity(e);

        var cmd = new SweepCommand(geometryEngine: new MathGeometryEngine());
        string? gerado = null;
        cmd.CodeGenerated += (_, c) => gerado = c;
        return (cmd, () => gerado, sketch);
    }

    // --- O caminho vem de arestas, nunca de cliques no vazio ---

    [Fact]
    public void CliqueNoVazio_NaoDesenhaCaminho()
    {
        // A ferramenta não desenha: clicar onde não há nada não acrescenta trecho nenhum, e o Enter
        // diz o que falta em vez de criar um sólido torto.
        var (cmd, codigo, sketch) = Acionada();

        cmd.OnMouseDown(sketch, new Point3D(0, 0, 0));
        cmd.OnMouseDown(sketch, new Point3D(100, 0, 0));
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().BeNull();
        cmd.Error.Should().NotBeNull().And.Contain("arestas do caminho");
    }

    [Fact]
    public void SemCaminhoEscolhido_OValorDigitadoEhORaioDaSecao()
    {
        var (cmd, _, sketch) = Acionada();

        cmd.OnValueEntered(sketch, 7);

        cmd.SectionRadius.Should().Be(7);
        cmd.GetMeasurementText().Should().Contain("7");
    }

    // --- Seção e caminho ---

    [Fact]
    public void RegiaoCircularMaisCadeiaDeLinhas_VaraNoEnter()
    {
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoCircular(6), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().NotBeNull();
        codigo().Should().Contain("SweepSection.Circle(6)")
                .And.Contain("new Point3D(0, 0, 0)")
                .And.Contain("new Point3D(100, 0, 0)")
                .And.Contain("new Point3D(100, 80, 0)")
                .And.Contain("cornerRadius: 12")                    // 2 x 6
                .And.Contain("upReference: new Vector3D(0, 0, 1)")
                .And.Contain("AddFeature(");
    }

    [Fact]
    public void NomeDoSolidoBateComAVariavel()
    {
        // A regra de sólidos rastreáveis: o Name literal tem que ser igual ao nome da variável.
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoCircular(5), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        string varName = codigo()!.Split("var ")[2].Split(' ')[0];
        codigo().Should().Contain($"var {varName} = new SweepFeature(")
                .And.Contain($"{{ Name = \"{varName}\" }}")
                .And.Contain($"AddFeature({varName});");
    }

    [Fact]
    public void SemRegiao_ASecaoEhOCirculoDoRaioDigitado()
    {
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(null, CadeiaEmL());

        cmd.OnValueEntered(sketch, 4);
        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("SweepSection.Circle(4)").And.Contain("cornerRadius: 8");
    }

    [Fact]
    public void SecaoPoligonal_EhRecentradaNaOrigem()
    {
        // O contorno do perfil sai em coordenadas do plano DELE: uma região desenhada em (100, 50)
        // daria uma seção a 100, 50 do eixo, e o tubo sairia deslocado do caminho.
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoPoligonal(100, 50), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().NotBeNull();
        string argSecao = codigo()!.Split("SweepSection.Polygon(")[1].Split("}")[0];
        argSecao.Should().Contain("(-10, -5)").And.Contain("(10, -5)")
                .And.Contain("(10, 5)").And.Contain("(-10, 5)");
        argSecao.Should().NotContain("90").And.NotContain("45").And.NotContain("110");
    }

    [Fact]
    public void SecaoPoligonal_SaiComOTipoDoArrayExplicito()
    {
        // `new[] { (-10, -5), ... }` inferiria (int, int)[], que NÃO converte para
        // IEnumerable<(double u, double v)>: o script geraria e só falharia ao executar a cena.
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoPoligonal(0, 0), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("SweepSection.Polygon(new (double, double)[] {");
    }

    [Fact]
    public async Task SecaoPoligonal_OCodigoGeradoCompila()
    {
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoPoligonal(0, 0), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        var result = await new RoslynSceneEvaluator().EvaluateAsync(codigo()!);
        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "o código gerado tem que compilar");
        result.Features.Should().ContainSingle();
    }

    [Fact]
    public void RetanguloSemRotacao_SaiComoRectangle()
    {
        var sketch = NovoSketch();
        var secao = new RectangleProfile(new Point3D(300, 300, 0), width: 20, height: 10);
        var (cmd, codigo) = Preparar(secao, CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("SweepSection.Rectangle(20, 10)");
    }

    [Fact]
    public void RetanguloGirado_SaiComoPolygon_ParaNaoPerderOAngulo()
    {
        // SweepSection.Rectangle(w, h) descarta o ângulo em silêncio.
        var sketch = NovoSketch();
        var secao = new RectangleProfile(new Point3D(300, 300, 0), width: 20, height: 10,
            rotationAngle: System.Math.PI / 6);
        var (cmd, codigo) = Preparar(secao, CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("SweepSection.Polygon(").And.NotContain("SweepSection.Rectangle(");
    }

    // --- Seção e caminho em planos diferentes: é o caso normal, não erro ---

    [Fact]
    public void SecaoEmXY_ComCaminhoEmXZ_Funciona()
    {
        // O perfil num plano e o caminho em outro é o jeito normal de modelar. Da seção só se usa a
        // FORMA; quem manda na posição e na orientação é o caminho.
        var sketch = NovoSketch();
        var secao = new RectangleProfile(new Point3D(300, 300, 0), width: 20, height: 10, plane: WorkPlane.XY);
        var caminho = new[]
        {
            Linha(0, 0, 100, 0, WorkPlane.XZ),
            Linha(100, 0, 100, 80, WorkPlane.XZ),
        };
        var (cmd, codigo) = Preparar(secao, caminho);

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        cmd.Error.Should().BeNull("planos diferentes não são impedimento");
        codigo().Should().NotBeNull();

        // XZ: o x local é o X global e o y local é o Z global; a normal é -Y.
        codigo().Should().Contain("new Point3D(100, 0, 0)")
                .And.Contain("new Point3D(100, 0, 80)")
                .And.Contain("upReference: new Vector3D(0, -1, 0)");
    }

    [Fact]
    public void CadeiaQueMudaDePlano_EhUmCaminho3D()
    {
        // Encadear em coordenadas de mundo é o que permite isso: em coordenadas locais as duas
        // arestas pareceriam longe uma da outra e a cadeia seria recusada.
        var sketch = NovoSketch();
        var caminho = new[]
        {
            Linha(0, 0, 100, 0, WorkPlane.XY),      // mundo: (0,0,0) -> (100,0,0)
            Linha(100, 0, 100, 80, WorkPlane.XZ),   // mundo: (100,0,0) -> (100,0,80)
        };
        var (cmd, codigo) = Preparar(SecaoCircular(5), caminho);

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        cmd.Error.Should().BeNull();
        codigo().Should().Contain("new Point3D(0, 0, 0)")
                .And.Contain("new Point3D(100, 0, 0)")
                .And.Contain("new Point3D(100, 0, 80)");
    }

    // --- Raio de canto ---

    [Fact]
    public void RaioDeCantoPadrao_EhODobroDaMeiaLarguraEmU()
    {
        // É u que manda, não o circunraio: a guarda do kernel compara o raio de curvatura com o
        // alcance da seção na direção da dobra, que num caminho no plano é justamente o eixo u.
        var sketch = NovoSketch();
        var chata = new RectangleProfile(new Point3D(300, 300, 0), width: 40, height: 6);
        var (cmd, codigo) = Preparar(chata, CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("cornerRadius: 40");               // 2 x 20
    }

    [Fact]
    public void RaioDerivadoQueNaoCabe_EncurtaAteOQueCabe_EmVezDeRecusar()
    {
        // Seção estreita num caminho curto: o raio derivado (2 x 3 = 6) não cabe entre arestas de
        // 4, mas 4·0,98 cabe e ainda é maior que a meia-largura 3. Encurtar é o certo; recusar
        // deixaria o usuário adivinhando um número.
        var sketch = NovoSketch();
        var estreita = new RectangleProfile(new Point3D(300, 300, 0), width: 6, height: 2);
        var (cmd, codigo) = Preparar(estreita, Linha(0, 0, 4, 0), Linha(4, 0, 4, 4));

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        cmd.Error.Should().BeNull("o raio tinha que se ajustar ao caminho");
        codigo().Should().NotBeNull().And.Contain("cornerRadius: 3.92");
    }

    [Fact]
    public void SecaoLargaDemaisParaOCaminho_NaoGeraCodigo_EExplica()
    {
        // Arestas de 12 com seção de 40 de largura: nem encurtando o raio ao máximo que cabe (~11,8)
        // a seção caberia na dobra, porque ela sozinha pede mais de 20. Aí é recusa mesmo - e a
        // mensagem tem que dizer as DUAS medidas, senão não há o que fazer com ela.
        var sketch = NovoSketch();
        var larga = new RectangleProfile(new Point3D(300, 300, 0), width: 40, height: 6);
        var (cmd, codigo) = Preparar(larga, Linha(0, 0, 12, 0), Linha(12, 0, 12, 12));

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().BeNull("emitir código que vira malha vazia em silêncio não serve");
        cmd.Error.Should().NotBeNull()
           .And.Contain("40 de largura")
           .And.Contain("só aceita raio de canto");
        cmd.GetStatusText().Should().Contain("só aceita raio de canto");
    }

    [Fact]
    public void RaioDeCantoDigitadoDepoisDaRecusa_RefazEGera()
    {
        // A recusa não é beco sem saída: a escolha fica de pé e um valor digitado refaz a conta.
        var sketch = NovoSketch();
        var larga = new RectangleProfile(new Point3D(300, 300, 0), width: 40, height: 6);
        var (cmd, codigo) = Preparar(larga, Linha(0, 0, 12, 0), Linha(12, 0, 12, 12));

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);
        codigo().Should().BeNull();

        cmd.OnValueEntered(sketch, 3);

        codigo().Should().NotBeNull();
        codigo().Should().Contain("cornerRadius: 3").And.Contain("SweepSection.Rectangle(40, 6)");
    }

    // --- Círculo como caminho, cadeia fechada ---

    [Fact]
    public void CirculoComoCaminho_GeraUmaVoltaInteira()
    {
        var sketch = NovoSketch();
        var caminho = new CircleEntity(new Point3D(50, 50, 0), 40) { Plane = WorkPlane.XY };
        var (cmd, codigo) = Preparar(SecaoCircular(5), caminho);

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().NotBeNull();
        codigo().Should().Contain("SweepPath.FullCircle(")
                .And.Contain("center: new Point3D(50, 50, 0)")
                .And.Contain("axis: new Vector3D(0, 0, 1)")
                .And.Contain("radius: 40")
                .And.NotContain("cornerRadius");
    }

    [Fact]
    public void CadeiaFechadaDeLinhas_VaraSemTampas()
    {
        var sketch = NovoSketch();
        var quadro = new Entity[]
        {
            Linha(0, 0, 120, 0), Linha(120, 0, 120, 120),
            Linha(120, 120, 0, 120), Linha(0, 120, 0, 0),
        };
        var (cmd, codigo) = Preparar(SecaoCircular(5), quadro);

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("closed: true");
        codigo()!.Split("new Point3D(").Length.Should().Be(5, "quatro pontos, sem repetir o primeiro");
    }

    // --- Recusas ---

    [Fact]
    public void LinhasQueSeRamificam_NaoGeramCodigo()
    {
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoCircular(5),
            Linha(0, 0, 50, 0), Linha(50, 0, 100, 0), Linha(50, 0, 50, 50));

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().BeNull();
        cmd.Error.Should().NotBeNull().And.Contain("ramific");
    }

    [Fact]
    public void VariasRegioesComoSecao_SaoRecusadas()
    {
        // Varrer N regiões são N sólidos, e a regra de sólidos rastreáveis quer uma declaração para
        // cada: a ferramenta pede uma região só.
        var sketch = NovoSketch();
        var composto = new CompoundProfile(new IProfile[]
        {
            new RectangleProfile(new Point3D(0, 0, 0), 10, 10),
            new RectangleProfile(new Point3D(50, 0, 0), 10, 10),
        });
        var (cmd, codigo) = Preparar(composto, CadeiaEmL());

        cmd.TryStartFromSelection(sketch);

        codigo().Should().BeNull();
        cmd.Error.Should().NotBeNull().And.Contain("uma região só");
    }

    [Fact]
    public void FaceDeSolidoComoSecao_ERecusada()
    {
        var sketch = NovoSketch();
        var caixa = new BoxFeature(Point3D.Origin, 10, 10, 10, "caixa");
        var (cmd, codigo) = Preparar(new ParametricBoxFace(caixa, FaceType.Top), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);

        codigo().Should().BeNull();
        cmd.Error.Should().NotBeNull().And.Contain("região do esboço");
    }

    [Fact]
    public void Escape_DescartaAEscolha()
    {
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoCircular(5), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Escape);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().BeNull();
    }

    [Fact]
    public void SemPreview_PorqueNaoHaNadaSendoDesenhado()
    {
        var (cmd, _, sketch) = Acionada(Linha(0, 0, 100, 0));

        cmd.TryPickAt(sketch, new Point3D(50, 0, 0));

        cmd.GetPreviewEntities().Should().BeEmpty();
    }

    // --- Escolhendo com a ferramenta já acionada ---

    [Fact]
    public void CliqueDentroDeUmaRegiao_EscolheASecao()
    {
        var (cmd, _, sketch) = Acionada();
        var regiao = new RectangleProfile(new Point3D(200, 200, 0), width: 20, height: 10);
        sketch.SetProfiles(new IProfile[] { regiao });

        bool consumiu = cmd.TryPickAt(sketch, new Point3D(200, 200, 0));

        consumiu.Should().BeTrue();
        cmd.Section.Should().Be(regiao);
        cmd.GetStatusText().Should().Contain("região escolhida");
    }

    [Fact]
    public void CliqueNumaAresta_EntraNoCaminho_EEnterConclui()
    {
        var a = Linha(0, 0, 100, 0);
        var b = Linha(100, 0, 100, 80);
        var (cmd, codigo, sketch) = Acionada(a, b);
        var regiao = new RectangleProfile(new Point3D(200, 200, 0), width: 20, height: 10);
        sketch.SetProfiles(new IProfile[] { regiao });

        cmd.TryPickAt(sketch, new Point3D(200, 200, 0)).Should().BeTrue();   // seção
        cmd.TryPickAt(sketch, new Point3D(50, 0, 0)).Should().BeTrue();      // aresta a
        cmd.TryPickAt(sketch, new Point3D(100, 40, 0)).Should().BeTrue();    // aresta b

        cmd.HasPickedPath.Should().BeTrue();
        a.IsSelected.Should().BeTrue("a aresta escolhida fica destacada como feedback");

        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().NotBeNull();
        codigo().Should().Contain("SweepSection.Rectangle(20, 10)")
                .And.Contain("new Point3D(0, 0, 0)")
                .And.Contain("new Point3D(100, 80, 0)")
                .And.Contain("upReference: new Vector3D(0, 0, 1)");
    }

    [Fact]
    public void RaioDigitadoComArestasEscolhidas_EhORaioDoCantoEConclui()
    {
        // Antes isto era um no-op silencioso: com região escolhida (então o número não é raio de
        // seção) e arestas escolhidas mas nada ainda preparado, o valor digitado caía no vazio.
        var a = Linha(0, 0, 100, 0);
        var b = Linha(100, 0, 100, 80);
        var (cmd, codigo, sketch) = Acionada(a, b);
        var regiao = new RectangleProfile(new Point3D(200, 200, 0), width: 20, height: 10);
        sketch.SetProfiles(new IProfile[] { regiao });

        cmd.TryPickAt(sketch, new Point3D(200, 200, 0));
        cmd.TryPickAt(sketch, new Point3D(50, 0, 0));
        cmd.TryPickAt(sketch, new Point3D(100, 40, 0));

        cmd.OnValueEntered(sketch, 15);

        codigo().Should().NotBeNull("o número digitado com caminho em mãos é o raio do canto, e conclui");
        codigo().Should().Contain("cornerRadius: 15");
    }

    [Fact]
    public void CliqueDeNovoNaMesmaAresta_TiraDoCaminho()
    {
        var a = Linha(0, 0, 100, 0);
        var (cmd, _, sketch) = Acionada(a);

        cmd.TryPickAt(sketch, new Point3D(50, 0, 0));
        cmd.TryPickAt(sketch, new Point3D(50, 0, 0));

        cmd.HasPickedPath.Should().BeFalse();
        a.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void ArestasMarcadasPeloRetangulo_SaoColhidasNoEnter()
    {
        // O arraste marca IsSelected sem passar pela ferramenta; o Enter tem que enxergar isso,
        // senão a seleção por retângulo não serviria de nada aqui.
        var a = Linha(0, 0, 100, 0);
        var b = Linha(100, 0, 100, 80);
        var (cmd, codigo, sketch) = Acionada(a, b);

        a.IsSelected = true;
        b.IsSelected = true;
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().NotBeNull();
        codigo().Should().Contain("new Point3D(0, 0, 0)").And.Contain("new Point3D(100, 80, 0)");
    }

    [Fact]
    public void CliqueNoTracoDeUmCirculo_EhCaminhoDeVoltaInteira()
    {
        // O hit-test mede a distância à CIRCUNFERÊNCIA, então o traço é caminho; o miolo, seção.
        var circulo = new CircleEntity(new Point3D(50, 50, 0), 40) { Plane = WorkPlane.XY };
        var (cmd, codigo, sketch) = Acionada(circulo);

        cmd.TryPickAt(sketch, new Point3D(90, 50, 0)).Should().BeTrue();
        cmd.OnKeyDown(sketch, CadKey.Enter);

        codigo().Should().Contain("SweepPath.FullCircle(").And.Contain("radius: 40");
    }

    // --- Ponta a ponta ---

    [Fact]
    public async Task CodigoGerado_CompilaEViraSolidoComOVolumeCerto()
    {
        // O elo que fecha tudo: os outros testes conferem o TEXTO emitido, este confere que o texto
        // vira geometria. Sem ele, um erro de grafia no código gerado passaria despercebido.
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoCircular(5), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnValueEntered(sketch, 30);                     // raio de canto, e conclui

        var result = await new RoslynSceneEvaluator().EvaluateAsync(codigo()!);
        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "o código gerado tem que compilar");
        result.Features.Should().ContainSingle();

        // Pappus: canto de 90° com raio 30 recua 30 de cada reta, então 100 e 80 viram 70 e 50.
        double comprimento = 70 + 30 * (System.Math.PI / 2) + 50;
        double areaSecao = MeshAreaCalculator.InscribedPolygonArea(5, 32);
        double volume = MeshVolumeCalculator.ComputeVolume(
            new OpenCascadeGeometryKernel().BuildFeatureMesh(result.Features[0]));

        volume.Should().BeApproximately(areaSecao * comprimento, areaSecao * comprimento * 0.01);
    }

    [Fact]
    public async Task SecaoPoligonal_ViraSolidoDePe_ComOVolumeCerto()
    {
        // O volume sozinho não distingue uma seção DE PÉ de uma deitada: é a extensão em Z que
        // prende a convenção (u -> perpendicular ao caminho no plano, v -> fora do plano).
        var sketch = NovoSketch();
        var (cmd, codigo) = Preparar(SecaoPoligonal(0, 0), CadeiaEmL());

        cmd.TryStartFromSelection(sketch);
        cmd.OnKeyDown(sketch, CadKey.Enter);

        var result = await new RoslynSceneEvaluator().EvaluateAsync(codigo()!);
        result.Success.Should().BeTrue(because: result.ErrorMessage ?? "o código gerado tem que compilar");

        var mesh = new OpenCascadeGeometryKernel().BuildFeatureMesh(result.Features[0]);

        // A seção tem 20 de largura em u, logo o raio de canto derivado é 20; um canto de 90° com
        // raio 20 recua 20 de cada reta, então 100 e 80 viram 80 e 60.
        double comprimento = 80 + 20 * (System.Math.PI / 2) + 60;
        double esperado = 20 * 10 * comprimento;
        MeshVolumeCalculator.ComputeVolume(mesh).Should().BeApproximately(esperado, esperado * 0.01);

        // v (a altura 10 do perfil) vai para a normal do plano do caminho: de -5 a +5 em Z.
        var zs = Enumerable.Range(0, mesh.Positions.Length / 3).Select(i => (double)mesh.Positions[i * 3 + 2]).ToList();
        (zs.Max() - zs.Min()).Should().BeApproximately(10, 0.01, "o perfil fica de pé sobre o caminho");
    }
}
