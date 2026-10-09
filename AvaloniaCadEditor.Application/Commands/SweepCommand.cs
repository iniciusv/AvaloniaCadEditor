using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaCadEditor.Application.Services;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Interfaces;
using AvaloniaCadEditor.Domain.Profiles;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// Ferramenta Sweep: varre uma seção ao longo de um caminho, e as duas coisas são GEOMETRIA JÁ
/// DESENHADA — não se desenha caminho aqui.
/// <list type="bullet">
/// <item><b>Seção</b>: clique dentro de uma REGIÃO do esboço. Sem região escolhida, a seção é um
/// círculo do raio digitado.</item>
/// <item><b>Caminho</b>: clique (ou arraste o retângulo) nas ARESTAS já desenhadas. Um círculo
/// escolhido pelo traço vale como caminho de volta inteira (anel).</item>
/// <item><b>Enter</b> conclui, sempre. Escolher não cria nada sozinho.</item>
/// </list>
/// <para>
/// Pode escolher antes de acionar (pré-seleção) ou depois; os dois caminhos desembocam no mesmo
/// <see cref="StageFromEntities"/>, então dão o mesmo resultado.
/// </para>
/// <para>
/// Seção e caminho NÃO precisam estar no mesmo plano — perfil num plano e caminho em outro é o jeito
/// normal de modelar. Da região só se aproveita a FORMA: a posição dela é descartada (o contorno é
/// recentrado no caminho, senão o tubo sairia deslocado) e a rotação em torno do caminho é fixada
/// por convenção — <c>upReference</c> = normal do plano do CAMINHO, o que deixa o perfil "de pé"
/// sobre ele: a largura vira largura perpendicular ao caminho, a altura vira altura fora do plano
/// do caminho.
/// </para>
/// <para>
/// Como as demais ferramentas, não constrói geometria: emite o CadScript equivalente por
/// <see cref="CodeGenerated"/>, e quem executa é o editor de script. O caminho é VALIDADO antes de
/// emitir (emitir código que vira malha vazia em silêncio não serve); o motivo da recusa sai em
/// <see cref="GetStatusText"/>.
/// </para>
/// <para>
/// Limite desta versão: sem preview do sólido — ele aparece ao concluir, porque o preview 3D hoje
/// está amarrado ao tipo <see cref="ExtrudeCommand"/> em uma dúzia de lugares. O feedback da
/// escolha é o destaque das arestas e da região.
/// </para>
/// </summary>
public class SweepCommand : ICommandTool
{
    public event EventHandler<string>? CodeGenerated;
    public string Name => "Sweep";

    /// <summary>
    /// <see cref="State.Picking"/> é escolhendo seção e caminho. <see cref="State.Blocked"/> é a
    /// escolha que não deu em nada: ela é mantida, um valor digitado refaz a conta, e escolher
    /// outra coisa recomeça.
    /// </summary>
    private enum State { Picking, Blocked }
    private State _state = State.Picking;

    /// <summary>Raio da seção, em mm. Vale enquanto nenhuma região foi escolhida.</summary>
    public double SectionRadius { get; private set; } = 5.0;

    /// <summary>
    /// Raio dos cantos. Zero significa "derive da seção" — um canto precisa de raio maior que a
    /// seção, senão a parede interna cruza o eixo do arco e o sólido dobra para dentro de si mesmo.
    /// </summary>
    private double _cornerRadius;
    public double EffectiveCornerRadius => _cornerRadius > 0 ? _cornerRadius : DefaultCornerRadius;

    /// <summary>
    /// O dobro da meia-largura da seção na direção u. É u que importa, e não o circunraio: a guarda
    /// do kernel exige <c>raio de curvatura ≥ max (v−origem)·direção da dobra</c>, e num caminho no
    /// plano a direção da dobra é justamente o eixo u da seção. Para um círculo isso dá
    /// <c>2·R</c> — exatamente a regra de sempre.
    /// </summary>
    private double DefaultCornerRadius => 2.0 * MeiaLarguraEmU;

    /// <summary>A região pré-selecionada que serve de seção, ou null (seção circular digitada).</summary>
    public IProfile? Section { get; private set; }

    /// <summary>Por que a pré-seleção não deu em nada, ou null. Aparece na barra de status.</summary>
    public string? Error { get; private set; }

    private readonly IProfile? _preSection;
    private readonly List<Entity> _prePath;
    private readonly IGeometryEngine? _geometryEngine;

    /// <summary>Tolerância de acerto do clique, em unidades de mundo. Quem ajusta é o CadViewModel.</summary>
    public double HitTolerance { get; set; } = 5.0;

    /// <summary>Arestas escolhidas com a ferramenta já acionada (clique ou retângulo).</summary>
    private readonly List<Entity> _edges = new();

    /// <summary>True quando o caminho veio de arestas, e não de cliques no vazio.</summary>
    public bool HasPickedPath => _edges.Count > 0;

    /// <summary>A seção já resolvida em texto de script. Null enquanto ninguém escolheu região.</summary>
    private FeatureCodeGenerator.SweepSectionCode? _sectionCode;

    // O caminho da pré-seleção, guardado para o Blocked poder refazer a conta com outro raio.
    private EdgeChain? _chain;
    private (Point3D Center, Vector3D Axis, double Radius)? _circlePath;
    private WorkPlane? _pathPlane;

    public SweepCommand(
        IProfile? preSelectedSection = null,
        IEnumerable<Entity>? preSelectedPath = null,
        IGeometryEngine? geometryEngine = null)
    {
        _preSection = preSelectedSection;
        _prePath = preSelectedPath?.Where(e => e.IsVisible).ToList() ?? new List<Entity>();
        _geometryEngine = geometryEngine;
    }

    /// <summary>
    /// Um clique que ESCOLHE geometria: uma região vira a seção, uma aresta entra no caminho.
    /// Devolve true quando consumiu o clique — aí ele não vira ponto de caminho.
    /// <para>
    /// Recebe o ponto CRU, sem snap: com o snap ligado, clicar dentro de uma região poderia pular
    /// para a ponta de uma linha vizinha e a região nunca seria escolhida. É o mesmo motivo pelo
    /// qual o <see cref="SelectTool"/> usa o ponto cru para o seu hit-test.
    /// </para>
    /// </summary>
    public bool TryPickAt(Sketch sketch, Point3D rawPoint)
    {
        if (_state == State.Blocked) AbandonPreSelection();

        var aresta = _geometryEngine?.HitTest(sketch.Entities.Where(e => e.IsVisible), rawPoint, HitTolerance);
        if (aresta is LineEntity or CircleEntity)
        {
            ToggleEdge(aresta);
            return true;
        }

        if (Section == null && FindRegionAtPoint(sketch, rawPoint) is { } regiao)
            return TrySetSection(regiao);

        return false;
    }

    private void ToggleEdge(Entity aresta)
    {
        if (_edges.Remove(aresta))
        {
            aresta.IsSelected = false;
        }
        else
        {
            _edges.Add(aresta);
            aresta.IsSelected = true;      // o feedback é o próprio destaque da entidade
        }

        Error = null;
    }

    private bool TrySetSection(IProfile regiao)
    {
        var code = FeatureCodeGenerator.SweepSectionFromProfile(regiao);
        if (code.Error != null)
        {
            Fail(code.Error);
            return true;                   // consumiu o clique mesmo recusando a região
        }

        Section = regiao;
        _sectionCode = code;
        regiao.IsSelected = true;
        Error = null;
        return true;
    }

    /// <summary>A menor região que contém o ponto — uma ilha dentro de outra continua clicável.</summary>
    private static IProfile? FindRegionAtPoint(Sketch sketch, Point3D point) =>
        sketch.Profiles
            .Where(p => p.IsVisible && p is not FaceProfile && p.ContainsPoint(point))
            .OrderBy(p => p.Area)
            .FirstOrDefault();

    /// <summary>
    /// Lê a pré-seleção e, se der, emite o código na hora. Devolve true quando emitiu.
    /// <para>
    /// Chame DEPOIS de <c>CurrentTool = cmd</c>: o setter só assina <see cref="CodeGenerated"/>
    /// depois de atribuir, então um evento disparado antes disso se perderia. É o mesmo motivo de
    /// <c>ExtrudeCommand.StartDirectExtrusion</c> ser chamado de fora do construtor.
    /// </para>
    /// </summary>
    public bool TryStartFromSelection(Sketch sketch)
    {
        if (_preSection == null && _prePath.Count == 0) return false;   // nada marcado: fluxo de cliques

        if (_preSection != null)
        {
            var code = FeatureCodeGenerator.SweepSectionFromProfile(_preSection);
            if (code.Error != null) return Fail(code.Error);

            Section = _preSection;
            _sectionCode = code;
        }

        if (_prePath.Count == 0)
        {
            // Região escolhida, mas nenhuma aresta: o caminho vem dos cliques (ou de arestas
            // escolhidas agora, com a ferramenta já acionada), com esta seção.
            _state = State.Picking;
            return false;
        }

        // Prepara, mas NÃO conclui: quem conclui é o Enter, sempre. Concluir aqui significaria
        // criar um sólido no instante do atalho, com o que estivesse marcado de antes — inclusive
        // sobra de seleção — e a ferramenta já teria ido embora quando o usuário fosse olhar.
        StageFromEntities(_prePath);
        return false;
    }

    /// <summary>
    /// Monta o caminho a partir de entidades de esboço — venham elas da pré-seleção, de cliques na
    /// ferramenta ou do retângulo de seleção — e emite. É o ponto único onde "arestas" vira
    /// "caminho", para os três jeitos de escolher darem exatamente o mesmo resultado.
    /// </summary>
    private bool ResolveFromEntities(IReadOnlyList<Entity> entities) =>
        StageFromEntities(entities) && Resolve();

    /// <summary>
    /// Interpreta as entidades como caminho e guarda o resultado, sem emitir. Devolve false (e
    /// deixa o porquê em <see cref="Error"/>) quando elas não formam um caminho.
    /// </summary>
    private bool StageFromEntities(IReadOnlyList<Entity> entities)
    {
        // Um estágio novo descarta o anterior: senão uma escolha recusada deixaria valendo o
        // caminho de antes, e o Enter emitiria aquele, não o que está na tela.
        _chain = null;
        _circlePath = null;
        _pathPlane = null;

        var circles = entities.OfType<CircleEntity>().ToList();
        var lines = entities.OfType<LineEntity>().ToList();

        // Resolve em variáveis locais e só publica no fim: um estágio que falha pela metade deixaria
        // um caminho parcial, e o Enter seguinte emitiria pulando as checagens que faltaram.
        EdgeChain? chain = null;
        (Point3D Center, Vector3D Axis, double Radius)? circlePath = null;
        WorkPlane pathPlane;

        if (circles.Count > 0)
        {
            if (circles.Count > 1) return Fail("selecione um círculo só como caminho.");
            if (lines.Count > 0) return Fail("não misture um círculo com linhas no caminho.");

            var circle = circles[0];
            pathPlane = circle.Plane;
            circlePath = (pathPlane.ToWorldSpace(circle.Center), pathPlane.Normal, circle.Radius);
        }
        else
        {
            chain = EdgeChainService.Chain(lines);
            if (chain.Error != null) return Fail(chain.Error);

            pathPlane = chain.Plane;
        }

        // Nenhuma checagem de "mesmo plano" aqui, de propósito: o perfil num plano e o caminho em
        // outro (seção em XY, caminho em XZ) é o jeito normal de modelar. Da seção só se usa a
        // forma, e quem manda na posição e na orientação é o caminho.

        _chain = chain;
        _circlePath = circlePath;
        _pathPlane = pathPlane;

        // Sem região escolhida, a seção é a circular digitada - o raio padrão vale.
        _sectionCode ??= FeatureCodeGenerator.SweepSectionFromRadius(SectionRadius);

        Error = null;
        _state = State.Picking;
        return true;
    }

    /// <summary>Monta o caminho, confere, e emite. Em caso de recusa vai para <see cref="State.Blocked"/>.</summary>
    private bool Resolve()
    {
        var plane = _pathPlane ?? WorkPlane.XY;
        // upReference só quando a seção veio de uma região: no fluxo antigo a seção é circular e a
        // rotação em torno do caminho não quer dizer nada.
        Vector3D? up = Section != null ? plane.Normal : null;
        string code;

        if (_circlePath is { } circle)
        {
            var path = SweepPath.FullCircle(circle.Center, circle.Axis, circle.Radius);
            if (path.Error != null) return Fail(path.Error);

            code = FeatureCodeGenerator.GenerateSweepCode(_sectionCode!, circle.Center, circle.Axis, circle.Radius, up);
        }
        else if (_chain is { } chain)
        {
            // Os pontos da cadeia já vêm em mundo (é o que permite arestas de planos diferentes).
            var world = chain.Points;
            double raio = FitCornerRadius(world, chain.IsClosed, out string? porque);
            if (porque != null) return Fail(porque);

            var path = SweepPath.FromPolyline(world, raio, chain.IsClosed);
            if (path.Error != null) return Fail(path.Error);

            code = FeatureCodeGenerator.GenerateSweepCode(_sectionCode!, world, raio, chain.IsClosed, up);
        }
        else
        {
            return false;
        }

        Error = null;
        _state = State.Picking;
        CodeGenerated?.Invoke(this, code);
        Cancel();
        return true;
    }

    /// <summary>
    /// O raio de canto a usar de fato: o pedido (ou o derivado da seção), ENCURTADO para o que cabe
    /// no caminho.
    /// <para>
    /// Sem isso, uma região de seção larga com arestas curtas era recusada e não havia o que fazer
    /// a não ser adivinhar um número — e um caminho desenhado à mão quase nunca tem folga para
    /// <c>2 ×</c> a meia-largura da seção. Encurtar é seguro: o raio só precisa ser maior que a
    /// meia-largura em u (senão a parede interna cruza o eixo do arco e o sólido dobra para dentro
    /// de si mesmo), e é só esse piso que vira recusa.
    /// </para>
    /// <para>
    /// Um raio DIGITADO é respeitado como pedido: se o usuário deu um número que não cabe, a recusa
    /// do <see cref="SweepPath.FromPolyline"/> é a resposta certa, com as medidas reais na mensagem.
    /// </para>
    /// </summary>
    private double FitCornerRadius(IReadOnlyList<Point3D> world, bool closed, out string? porque)
    {
        porque = null;
        if (_cornerRadius > 0) return _cornerRadius;          // digitado: não mexe

        double pedido = DefaultCornerRadius;
        double cabe = SweepPath.MaxCornerRadius(world, closed);
        if (double.IsPositiveInfinity(cabe)) return pedido;   // caminho sem canto: nada limita

        if (cabe >= pedido) return pedido;

        // Uma folga pequena para não encostar na igualdade exata da checagem de recuo.
        double ajustado = cabe * 0.98;
        double piso = MeiaLarguraEmU;

        if (ajustado <= piso)
        {
            porque = $"a seção tem {2 * piso:0.###} de largura e o caminho só aceita raio de canto " +
                     $"{ajustado:0.###}: alongue as arestas, use uma seção mais estreita, ou digite um raio.";
            return pedido;
        }

        return ajustado;
    }

    /// <summary>A meia-largura da seção na direção u — o que a dobra tem que comportar.</summary>
    private double MeiaLarguraEmU =>
        _sectionCode is { Points.Count: > 0 } sec ? sec.Points.Max(p => Math.Abs(p.U)) : SectionRadius;

    private bool Fail(string error)
    {
        Error = error;
        _state = State.Blocked;
        return false;
    }

    /// <summary>
    /// Todo clique é ESCOLHA de geometria — a ferramenta não desenha caminho. O CadViewModel chama
    /// <see cref="TryPickAt"/> antes daqui com o ponto cru; este caminho existe para quem chama a
    /// ferramenta direto (testes) e para o clique no vazio, que não escolhe nada.
    /// </summary>
    public void OnMouseDown(Sketch sketch, Point3D point) => TryPickAt(sketch, point);

    public void OnMouseMove(Sketch sketch, Point3D point)
    {
        // Sem elástico: não há caminho sendo desenhado para acompanhar o cursor.
    }

    public void Cancel()
    {
        _cornerRadius = 0;
        _state = State.Picking;
        Error = null;
        _chain = null;
        _circlePath = null;
        _pathPlane = null;

        // Desfaz o destaque das arestas que a ferramenta marcou como feedback.
        foreach (var e in _edges) e.IsSelected = false;
        _edges.Clear();
    }

    /// <summary>Descarta o caminho vindo da pré-seleção, mas MANTÉM a seção escolhida.</summary>
    private void AbandonPreSelection()
    {
        _state = State.Picking;
        Error = null;
        _chain = null;
        _circlePath = null;
        _pathPlane = null;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) { Cancel(); return; }
        if (key != CadKey.Enter) return;

        // As arestas podem ter vindo do clique (_edges) ou do retângulo de seleção, que marca
        // IsSelected sem passar pela ferramenta. Colher as duas é o que faz o arraste funcionar.
        var arestas = ColherArestas(sketch);
        if (arestas.Count > 0)
        {
            _sectionCode ??= Section != null
                ? FeatureCodeGenerator.SweepSectionFromProfile(Section)
                : FeatureCodeGenerator.SweepSectionFromRadius(SectionRadius);

            ResolveFromEntities(arestas);
            return;
        }

        // Caminho já preparado pela pré-seleção: o Enter é o que conclui.
        if (_chain != null || _circlePath != null) Resolve();

        // Sem caminho e sem recusa anterior, o que falta é escolher. Uma recusa que já existe é
        // mais específica do que esta e não pode ser apagada por cima.
        else if (Error == null)
            Fail("escolha as arestas do caminho (clique ou arraste sobre linhas já desenhadas).");
    }

    private List<Entity> ColherArestas(Sketch sketch)
    {
        var arestas = new List<Entity>(_edges);
        foreach (var e in sketch.Entities)
        {
            if (e.IsSelected && e.IsVisible && e is LineEntity or CircleEntity && !arestas.Contains(e))
                arestas.Add(e);
        }
        return arestas;
    }

    /// <summary>
    /// Com um caminho em mãos, o valor digitado é o raio dos CANTOS e refaz a conta na hora. Sem
    /// caminho e sem região escolhida, é o raio da SEÇÃO. Mesma convenção do
    /// <see cref="DrawPolygonCommand"/>, onde o número digitado muda de significado conforme o estágio.
    /// </summary>
    public void OnValueEntered(Sketch sketch, double value)
    {
        if (value <= 0) return;

        if (_state == State.Blocked || _chain != null || _circlePath != null)
        {
            _cornerRadius = value;
            Resolve();
            return;
        }

        if (Section == null) SectionRadius = value;
    }

    /// <summary>Nada a prever: a ferramenta não desenha, só escolhe o que já está desenhado.</summary>
    public IEnumerable<Entity> GetPreviewEntities() => Array.Empty<Entity>();

    public string GetMeasurementText()
    {
        if (_state == State.Blocked)
            return $"Canto R: {EffectiveCornerRadius:F2} (digite para mudar)";

        if (_edges.Count > 0)
            return $"Caminho: {_edges.Count} aresta(s)  — Enter conclui";

        return Section != null
            ? "Seção: região escolhida  — escolha as arestas do caminho"
            : $"Seção R: {SectionRadius:F2} (digite para mudar)  — escolha as arestas do caminho";
    }

    public string GetStatusText()
    {
        if (Error is { } erro)
            return $"Sweep: {erro}  — digite outro raio de canto, escolha outro caminho, ou Esc.";

        string secao = Section != null ? "seção: região escolhida" : $"seção: círculo R{SectionRadius:F2} (digite para mudar)";

        if (_edges.Count > 0)
            return $"Sweep: {secao} · {_edges.Count} aresta(s) no caminho — clique/arraste para somar, Enter conclui.";

        return Section != null
            ? $"Sweep: {secao} — clique ou arraste nas ARESTAS já desenhadas do caminho. Enter conclui."
            : $"Sweep: {secao} — clique dentro de uma região para ser a seção, e nas arestas já desenhadas do caminho. Enter conclui.";
    }

    public Point3D? GetReferencePoint() => null;
}
