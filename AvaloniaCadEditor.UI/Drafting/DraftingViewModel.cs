using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;
using AvaloniaCadEditor.Drafting;
using AvaloniaCadEditor.UI.ReferenceGuides;
using AvaloniaCadEditor.UI.ViewModels;

namespace AvaloniaCadEditor.UI.Drafting;

/// <summary>
/// Estado do analisador de cotas na UI. Fica de fora do <see cref="CadViewModel"/> de propósito (como
/// o <see cref="ReferenceGuidesViewModel"/>): só lê a peça isolada e o referencial dela e desenha
/// por cima — não escreve código nem mexe na cena.
///
/// O desenho só existe com uma peça isolada (H), numa vista padrão escolhida pelos botões e com a
/// câmera ortográfica olhando por ela; girar a câmera esconde as cotas até voltar à vista.
/// As cotas manuais e as automáticas escondidas vivem só na sessão e somem ao sair do isolamento.
/// </summary>
public sealed class DraftingViewModel : INotifyPropertyChanged
{
    private readonly CadViewModel _cad;

    /// <summary>Cotas manuais por peça e vista: pares de âncoras (Ids estáveis) e a posição da linha.</summary>
    private readonly Dictionary<(string Owner, StandardView View), List<ManualSpec>> _manual = new();

    /// <summary>Cotas automáticas escondidas pelo usuário, por peça e vista (chave da cota).</summary>
    private readonly Dictionary<(string Owner, StandardView View), HashSet<string>> _hidden = new();

    private int _hintGeneration;

    public DraftingViewModel(CadViewModel cad)
    {
        _cad = cad ?? throw new ArgumentNullException(nameof(cad));
        _cad.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CadViewModel.DisplayFrame))
            {
                if (_cad.DisplayFrame == null)
                {
                    _manual.Clear();
                    _hidden.Clear();
                }
                Recompute();
            }
        };
        RestoreCommand = new RelayCommand(Restore);
    }

    /// <summary>Disparado quando o que é desenhado muda (folha, cotas, pendências, visibilidade).</summary>
    public event Action? Changed;

    private bool _isVisible;
    /// <summary>Toggle "Cotas" da toolbar.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
            if (!value) IsPicking = false;
            Recompute();
        }
    }

    private bool _isPicking;
    /// <summary>Modo "Cotar": cliques na vista escolhem pontos para cotas manuais e cliques nos
    /// textos escondem (automáticas) ou removem (manuais) cotas.</summary>
    public bool IsPicking
    {
        get => _isPicking;
        set
        {
            if (_isPicking == value) return;
            _isPicking = value;
            OnPropertyChanged();
            CancelPending();
            RaiseChanged();
        }
    }

    /// <summary>Última vista padrão escolhida pelos botões (null antes do primeiro clique).</summary>
    public StandardView? View { get; private set; }

    public PartFrame? Frame { get; private set; }

    public DraftingSheet? Sheet { get; private set; }

    /// <summary>Cotas desenhadas: automáticas (menos as escondidas) e manuais.</summary>
    public IReadOnlyList<DraftDimension> Dimensions { get; private set; } = Array.Empty<DraftDimension>();

    /// <summary>Parâmetro do record por trás de uma cota (chave da cota → "RaioFuroPassante").</summary>
    public IReadOnlyDictionary<string, string> ParameterHints { get; private set; } = new Dictionary<string, string>();

    /// <summary>Primeiro e segundo pontos escolhidos de uma cota manual ainda não posicionada.</summary>
    public DraftAnchor? PendingFirst { get; private set; }
    public DraftAnchor? PendingSecond { get; private set; }

    public ICommand RestoreCommand { get; }

    /// <summary>Tamanho da seta, em mm do desenho.</summary>
    public double ArrowSize => (Sheet?.Gap ?? 2) * 0.3;

    /// <summary>Os botões de vista chamam isto (antes de girar a câmera).</summary>
    public void SetView(StandardView view)
    {
        View = view;
        CancelPending();
        Recompute();
    }

    /// <summary>Leva um ponto do desenho para o mundo, logo à frente da peça.</summary>
    public Point3D ToWorld(P2 point)
    {
        var sheet = Sheet!;
        return Frame!.ToWorld(sheet.Basis.Unproject(point, sheet.FrontDepth + sheet.Gap * 0.2));
    }

    /// <summary>True quando a câmera mostra exatamente a vista desenhada (ortho, mesma direção).</summary>
    public bool MatchesCamera(CameraState camera)
    {
        if (Sheet == null || Frame == null || !camera.IsOrthographic) return false;
        var w = Frame.FrameToWorld.ApplyVector(Sheet.Basis.W);
        var eye = Vector3.Normalize(camera.Position - camera.Target);
        return eye.X * w.X + eye.Y * w.Y + eye.Z * w.Z > 0.999;
    }

    public bool IsShowing(CameraState camera) => IsVisible && MatchesCamera(camera);

    // ------------------------------------------------------------------ Análise

    private void Recompute()
    {
        Frame = _cad.DisplayFrame;
        Sheet = null;
        Dimensions = Array.Empty<DraftDimension>();

        var feature = _cad.IsolatedFeature;
        if (IsVisible && Frame != null && feature != null && View is { } view)
        {
            var input = DraftingInput.ForIsolated(feature, Frame, _cad.GetFeatureMesh(feature.Id)?.Positions, view);
            Sheet = input == null ? null : DraftingAnalyzer.Analyze(input);
        }

        RebuildDimensions();
        StartParameterHints();
    }

    private (string Owner, StandardView View)? CurrentKey =>
        Sheet != null && Frame != null ? (Frame.OwnerName, Sheet.Basis.View) : null;

    private void RebuildDimensions()
    {
        if (Sheet == null || CurrentKey is not { } key)
        {
            Dimensions = Array.Empty<DraftDimension>();
            RaiseChanged();
            return;
        }

        var hidden = _hidden.TryGetValue(key, out var h) ? h : new HashSet<string>();
        var list = Sheet.Dimensions.Where(d => !hidden.Contains(d.Key)).ToList();

        if (_manual.TryGetValue(key, out var specs))
        {
            foreach (var spec in specs)
            {
                var resolved = Resolve(spec);
                if (resolved != null) list.Add(resolved);
            }
        }

        Dimensions = list;
        RaiseChanged();
    }

    /// <summary>Uma cota manual é guardada por Ids de âncora e recalculada a cada folha nova: se o
    /// sólido sumiu do código, ela simplesmente não aparece.</summary>
    private DraftDimension? Resolve(ManualSpec spec)
    {
        if (spec.Kind == DimensionKind.Diameter)
        {
            var circle = Sheet!.Entities.OfType<ViewCircle>().FirstOrDefault(c => c.SourceName == spec.A);
            return circle == null ? null : AutoDimensioner.Diameter(circle);
        }

        var a = Sheet!.FindAnchor(spec.A);
        var b = Sheet.FindAnchor(spec.B);
        return a == null || b == null ? null : AutoDimensioner.Linear(a, b, spec.Kind, spec.LinePosition);
    }

    /// <summary>
    /// Rótulo "Ø3,4 · RaioFuroPassante": para as cotas de diâmetro, pergunta ao Roslyn qual
    /// parâmetro do record alimenta o raio do furo. Compilar é caro, então roda fora da UI thread
    /// e os rótulos aparecem quando terminam; uma folha nova descarta o resultado da anterior.
    /// </summary>
    private void StartParameterHints()
    {
        int generation = ++_hintGeneration;
        ParameterHints = new Dictionary<string, string>();

        if (Sheet == null || _cad.IsolatedFeature is not PartInstance instance || _cad.ScriptEditor == null) return;

        var documents = _cad.ScriptEditor.GetSceneDocuments();
        var editor = _cad.ScriptEditor;
        var partType = instance.PartTypeName;
        var targets = Sheet.Dimensions
            .Where(d => (d.Kind == DimensionKind.Diameter || d.Key.StartsWith("ds:", StringComparison.Ordinal)) && d.SourceNames.Count > 0)
            .Select(d => (d.Key, Solid: d.SourceNames[0]))
            .ToList();
        if (targets.Count == 0) return;

        Task.Run(() =>
        {
            var hints = new Dictionary<string, string>();
            foreach (var (key, solid) in targets)
            {
                var parameters = editor.TracePartArgument(documents, partType, solid, "radius");
                if (parameters is { Count: > 0 }) hints[key] = string.Join(", ", parameters);
            }
            return hints;
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _hintGeneration) return;
                ParameterHints = task.Result;
                RaiseChanged();
            });
        });
    }

    // ------------------------------------------------------------------ Cotas manuais

    /// <summary>
    /// Clique na viewport no modo "Cotar". Devolve true se o clique foi consumido.
    /// 1º clique: ponto A. 2º: ponto B (no mesmo centro de furo = diâmetro). 3º: posição da linha
    /// de cota — horizontal ou vertical conforme o lado para onde se puxou.
    /// </summary>
    public bool HandleClick(Point screen, CameraState camera, Size viewportSize)
    {
        if (!IsPicking || !IsShowing(camera) || Sheet == null) return false;

        if (PendingFirst != null && PendingSecond != null)
        {
            var place = ScreenToSheet(screen, camera, viewportSize);
            if (place != null) AddLinear(PendingFirst, PendingSecond, place.Value);
            CancelPending();
            RaiseChanged();
            return true;
        }

        var anchor = NearestAnchor(screen, camera, viewportSize);
        if (anchor == null) return true;   // no modo "Cotar" o clique nunca vai para a seleção

        if (PendingFirst == null)
        {
            PendingFirst = anchor;
        }
        else if (anchor.Id == PendingFirst.Id)
        {
            AddDiameterIfCircle(anchor);
            CancelPending();
        }
        else
        {
            PendingSecond = anchor;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>ESC no modo "Cotar": desiste da cota em andamento. True se havia uma.</summary>
    public bool CancelPending()
    {
        bool had = PendingFirst != null;
        PendingFirst = null;
        PendingSecond = null;
        return had;
    }

    /// <summary>Clique no texto de uma cota: no modo "Cotar" esconde (automática) ou remove
    /// (manual); fora dele, mostra no código o sólido que a cota mede.</summary>
    public void OnLabelClicked(DraftDimension dimension)
    {
        if (CurrentKey is not { } key) return;

        if (IsPicking)
        {
            if (dimension.IsManual)
            {
                if (_manual.TryGetValue(key, out var specs)) specs.RemoveAll(s => Resolve(s)?.Key == dimension.Key);
            }
            else
            {
                if (!_hidden.TryGetValue(key, out var hidden)) _hidden[key] = hidden = new HashSet<string>();
                hidden.Add(dimension.Key);
            }
            RebuildDimensions();
            return;
        }

        if (_cad.IsolatedFeature is PartInstance instance && dimension.SourceNames.Count > 0)
        {
            _cad.ScriptEditor?.RevealPartTool(instance.PartTypeName, dimension.SourceNames[0].Split('#')[0]);
        }
    }

    private void Restore()
    {
        if (CurrentKey is not { } key) return;
        _hidden.Remove(key);
        _manual.Remove(key);
        CancelPending();
        RebuildDimensions();
    }

    private void AddDiameterIfCircle(DraftAnchor anchor)
    {
        var name = anchor.Id[..anchor.Id.LastIndexOf('.')];
        if (Sheet!.Entities.OfType<ViewCircle>().All(c => c.SourceName != name)) return;
        AddSpec(new ManualSpec(name, name, DimensionKind.Diameter, 0));
    }

    private void AddLinear(DraftAnchor a, DraftAnchor b, P2 place)
    {
        // Puxou para cima/baixo do par: cota horizontal; para os lados: vertical.
        double du = Math.Abs(b.Point.U - a.Point.U);
        double dv = Math.Abs(b.Point.V - a.Point.V);
        double minU = Math.Min(a.Point.U, b.Point.U), maxU = Math.Max(a.Point.U, b.Point.U);
        bool outsideU = place.U < minU || place.U > maxU;
        var kind = dv < 1e-6 ? DimensionKind.Horizontal
            : du < 1e-6 ? DimensionKind.Vertical
            : outsideU ? DimensionKind.Vertical : DimensionKind.Horizontal;
        AddSpec(new ManualSpec(a.Id, b.Id, kind, kind == DimensionKind.Horizontal ? place.V : place.U));
    }

    private void AddSpec(ManualSpec spec)
    {
        if (CurrentKey is not { } key) return;
        if (!_manual.TryGetValue(key, out var specs)) _manual[key] = specs = new List<ManualSpec>();
        specs.Add(spec);
        RebuildDimensions();
    }

    private DraftAnchor? NearestAnchor(Point screen, CameraState camera, Size viewportSize)
    {
        const double pickRadius = 10;
        DraftAnchor? best = null;
        double bestDistance = pickRadius;
        foreach (var anchor in Sheet!.Anchors)
        {
            var p = ScreenProjection.WorldToScreen(camera, ToWorld(anchor.Point), viewportSize);
            if (p == null) continue;
            double d = Math.Sqrt(Math.Pow(p.Value.X - screen.X, 2) + Math.Pow(p.Value.Y - screen.Y, 2));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = anchor;
            }
        }
        return best;
    }

    private P2? ScreenToSheet(Point screen, CameraState camera, Size viewportSize)
    {
        var ray = camera.ScreenPointToRay(new Vector2((float)screen.X, (float)screen.Y),
            new Vector2((float)viewportSize.Width, (float)viewportSize.Height));
        var planePoint = ToWorld(new P2(0, 0));
        var w = Frame!.FrameToWorld.ApplyVector(Sheet!.Basis.W);
        var hit = ray.IntersectPlane(
            new Vector3((float)w.X, (float)w.Y, (float)w.Z),
            new Vector3((float)planePoint.X, (float)planePoint.Y, (float)planePoint.Z));
        return hit == null ? null : Sheet.Basis.Project(Frame.ToFrame(hit.Value));
    }

    private void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record ManualSpec(string A, string B, DimensionKind Kind, double LinePosition);
}
