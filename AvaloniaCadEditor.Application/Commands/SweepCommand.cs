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
public class SweepCommand : ICommandTool, IGeometryPickingTool
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

    /// <summary>
    /// Sim: pegar de uma vez as arestas de um caminho é o caso comum, e uma a uma é tedioso. Quem
    /// arrasta é a UI; aqui as arestas são colhidas no momento de concluir.
    /// </summary>
    public bool UsesSelectionBox => true;

    /// <summary>Arestas escolhidas com a ferramenta já acionada (clique ou retângulo).</summary>
    private readonly List<Entity> _edges = new();

    /// <summary>True quando o caminho veio de arestas, e não de cliques no vazio.</summary>
    public bool HasPickedPath => _edges.Count > 0;

    /// <summary>A seção já resolvida em texto de script. Null enquanto ninguém escolheu região.</summary>
    private FeatureCodeGenerator.SweepSectionCode? _sectionCode;

    /// <summary>
    /// O caminho escolhido, já interpretado. Uma referência só: meio-caminho não é representável, e
    /// é isso que impede um estágio recusado de deixar resto para o passo seguinte emitir.
    /// Guardado também para um raio digitado poder refazer a conta.
    /// </summary>
    private SweepPathPlan? _path;

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
            if (code.Error != null) { Fail(code.Error); return false; }

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
    private bool ResolveFromEntities(Sketch sketch, IReadOnlyList<Entity> entities) =>
        StageFromEntities(entities) && Resolve(sketch);

    /// <summary>
    /// Interpreta as entidades como caminho e guarda o resultado, sem emitir. Devolve false (e
    /// deixa o porquê em <see cref="Error"/>) quando elas não formam um caminho.
    /// </summary>
    private bool StageFromEntities(IReadOnlyList<Entity> entities)
    {
        // Um estágio novo descarta o anterior: senão uma escolha recusada deixaria valendo o
        // caminho de antes, e o Enter emitiria aquele, não o que está na tela.
        _path = null;

        var plan = Interpret(entities);
        if (plan == null) return false;                      // o porquê já está em Error

        // Nenhuma checagem de "mesmo plano" aqui, de propósito: o perfil num plano e o caminho em
        // outro (seção em XY, caminho em XZ) é o jeito normal de modelar. Da seção só se usa a
        // forma, e quem manda na posição e na orientação é o caminho.
        _path = plan;

        // Sem região escolhida, a seção é a circular digitada - o raio padrão vale.
        _sectionCode ??= FeatureCodeGenerator.SweepSectionFromRadius(SectionRadius);

        Error = null;
        _state = State.Picking;
        return true;
    }

    /// <summary>
    /// Que caminho estas entidades descrevem, ou null com o porquê em <see cref="Error"/>. Devolve
    /// o plano PRONTO — não há como sair daqui com um caminho pela metade.
    /// </summary>
    private SweepPathPlan? Interpret(IReadOnlyList<Entity> entities)
    {
        var circles = entities.OfType<CircleEntity>().ToList();
        var lines = entities.OfType<LineEntity>().ToList();

        if (circles.Count > 0)
        {
            if (circles.Count > 1) { Fail("selecione um círculo só como caminho."); return null; }
            if (lines.Count > 0) { Fail("não misture um círculo com linhas no caminho."); return null; }

            var circle = circles[0];
            var plane = circle.Plane;
            return new FullCirclePlan(
                plane.ToWorldSpace(circle.Center), plane.Normal, circle.Radius, plane, entities.ToList());
        }

        var chain = EdgeChainService.Chain(lines);
        if (chain.Error != null) { Fail(chain.Error); return null; }

        return new PolylinePlan(chain.Points, chain.IsClosed, chain.Plane, entities.ToList());
    }

    /// <summary>Monta o caminho, confere, e emite. Em caso de recusa vai para <see cref="State.Blocked"/>.</summary>
    private bool Resolve(Sketch sketch)
    {
        if (_path is not { } plan) return false;

        // O raio de canto é escolhido uma vez e serve para conferir e para emitir: conferir com um
        // número e emitir outro daria um sólido diferente do que foi validado.
        double? raio = FitCornerRadius(plan);
        if (raio == null) return false;                      // o porquê já está em Error

        var path = plan.Build(raio.Value);
        if (path.Error != null) { Fail(path.Error); return false; }

        // upReference só quando a seção veio de uma região: uma seção circular é simétrica e a
        // rotação em torno do caminho não quer dizer nada.
        Vector3D? up = Section != null ? plan.ReferencePlane.Normal : null;

        string code = plan switch
        {
            FullCirclePlan c => FeatureCodeGenerator.GenerateSweepCode(_sectionCode!, c.Center, c.Axis, c.Radius, up),
            PolylinePlan p => FeatureCodeGenerator.GenerateSweepCode(_sectionCode!, p.World, raio.Value, p.IsClosed, up),
            _ => throw new NotSupportedException($"caminho de varredura não previsto: {plan.GetType().Name}"),
        };

        Error = null;
        _state = State.Picking;

        // Antes de emitir: o reexecutar da cena refaz as regiões a partir do que sobrou no esboço,
        // e o que foi consumido não pode reaparecer.
        ConsumeSketchGeometry(sketch);

        CodeGenerated?.Invoke(this, code);
        Cancel();
        return true;
    }

    /// <summary>
    /// Remove do esboço o desenho que a varredura consumiu: as arestas do caminho e o contorno da
    /// região usada como seção. Mesmo comportamento da extrusão (ver
    /// <c>FeatureCodeGenerator.GenerateExtrusionCode</c>) — o desenho 2D era o meio de chegar ao
    /// sólido, e deixá-lo riscado por cima dele só atrapalha a vista e o próximo clique.
    /// <para>
    /// Mexe só na sessão: linhas e círculos de esboço não são código de cena, então isto não
    /// reescreve o script. O preço é que varrer uma segunda seção pelo mesmo caminho exige
    /// redesenhá-lo — o mesmo preço que a extrusão já cobra.
    /// </para>
    /// <para>
    /// Uma região sem <c>SourceEntityIds</c> não deixa nada para trás, e isso está certo: ela não
    /// veio de linhas de esboço (é uma face de sólido, ou foi montada à mão), então não há desenho
    /// a consumir. Não é esquecimento.
    /// </para>
    /// </summary>
    private void ConsumeSketchGeometry(Sketch sketch)
    {
        var ids = new List<Guid>();
        if (_path is { } plan) foreach (var e in plan.Source) ids.Add(e.Id);
        if (Section != null) ids.AddRange(Section.SourceEntityIds);

        foreach (var id in ids.Distinct())
        {
            var entity = sketch.Entities.FirstOrDefault(e => e.Id == id);
            if (entity != null) sketch.RemoveEntity(entity);
        }
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
    /// <para>
    /// Null é recusa, com o porquê em <see cref="Error"/> — o mesmo canal do resto da classe, para
    /// não haver dois jeitos de uma coisa dar errado aqui.
    /// </para>
    /// </summary>
    private double? FitCornerRadius(SweepPathPlan plan)
    {
        if (_cornerRadius > 0) return _cornerRadius;          // digitado: não mexe

        double pedido = DefaultCornerRadius;
        double cabe = plan.MaxCornerRadius();
        if (double.IsPositiveInfinity(cabe)) return pedido;   // caminho sem canto: nada limita
        if (cabe >= pedido) return pedido;

        // Uma folga pequena para não encostar na igualdade exata da checagem de recuo.
        double ajustado = cabe * 0.98;
        double piso = MeiaLarguraEmU;

        if (ajustado <= piso)
        {
            Fail($"a seção tem {2 * piso:0.###} de largura e o caminho só aceita raio de canto " +
                 $"{ajustado:0.###}: alongue as arestas, use uma seção mais estreita, ou digite um raio.");
            return null;
        }

        return ajustado;
    }

    /// <summary>A meia-largura da seção na direção u — o que a dobra tem que comportar.</summary>
    private double MeiaLarguraEmU =>
        _sectionCode is { Points.Count: > 0 } sec ? sec.Points.Max(p => Math.Abs(p.U)) : SectionRadius;

    /// <summary>
    /// Registra a recusa e para. Devolve void de propósito: já devolveu bool, e aí o mesmo helper
    /// carregava dois significados opostos ("deu certo" num lugar, "consumiu o clique" no outro).
    /// </summary>
    private void Fail(string error)
    {
        Error = error;
        _state = State.Blocked;
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
        _path = null;

        // Desfaz o destaque das arestas que a ferramenta marcou como feedback.
        foreach (var e in _edges) e.IsSelected = false;
        _edges.Clear();
    }

    /// <summary>Descarta o caminho vindo da pré-seleção, mas MANTÉM a seção escolhida.</summary>
    private void AbandonPreSelection()
    {
        _state = State.Picking;
        Error = null;
        _path = null;
    }

    public void OnKeyDown(Sketch sketch, CadKey key)
    {
        if (key == CadKey.Escape) { Cancel(); return; }
        if (key != CadKey.Enter) return;

        // Sem caminho e sem recusa anterior, o que falta é escolher. Uma recusa que já existe é
        // mais específica do que esta e não pode ser apagada por cima.
        if (!TryConclude(sketch) && Error == null)
            Fail("escolha as arestas do caminho (clique ou arraste sobre linhas já desenhadas).");
    }

    /// <summary>
    /// Conclui com o caminho que houver: as arestas escolhidas agora, ou o que a pré-seleção já
    /// preparou. Devolve false quando não há caminho nenhum (ou quando ele foi recusado).
    /// <para>
    /// Ponto único de conclusão — o Enter e o raio digitado passam os dois por aqui, senão um dos
    /// dois acabaria enxergando menos caminho que o outro.
    /// </para>
    /// </summary>
    private bool TryConclude(Sketch sketch)
    {
        // As arestas podem ter vindo do clique (_edges) ou do retângulo de seleção, que marca
        // IsSelected sem passar pela ferramenta. Colher as duas é o que faz o arraste funcionar.
        var arestas = ColherArestas(sketch);
        if (arestas.Count > 0)
        {
            _sectionCode ??= Section != null
                ? FeatureCodeGenerator.SweepSectionFromProfile(Section)
                : FeatureCodeGenerator.SweepSectionFromRadius(SectionRadius);

            return ResolveFromEntities(sketch, arestas);
        }

        return _path != null && Resolve(sketch);
    }

    /// <summary>Há caminho (ou candidato a caminho) em mãos? É o que decide o que um número digitado quer dizer.</summary>
    private bool HasPathCandidate(Sketch sketch) =>
        _state == State.Blocked || _path != null || ColherArestas(sketch).Count > 0;

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

        // Com caminho em mãos o número é o raio dos CANTOS e refaz a conta na hora. Antes disso é o
        // raio da SEÇÃO circular - e com uma região escolhida não quer dizer nada.
        if (HasPathCandidate(sketch))
        {
            _cornerRadius = value;
            TryConclude(sketch);
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
