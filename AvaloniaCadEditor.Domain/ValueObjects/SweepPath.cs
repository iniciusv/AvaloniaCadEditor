using System;
using System.Collections.Generic;
using System.Globalization;

namespace AvaloniaCadEditor.Domain.ValueObjects;

/// <summary>Um trecho do caminho de uma varredura: reta ou arco.</summary>
public readonly struct SweepSegment
{
    /// <summary>Arco quando true; reta quando false.</summary>
    public bool IsArc { get; }

    /// <summary>Reta: deslocamento do começo ao fim (direção e comprimento).</summary>
    public Vector3D LineVector { get; }

    /// <summary>Arco: centro.</summary>
    public Point3D ArcCenter { get; }

    /// <summary>Arco: eixo de revolução, normalizado (regra da mão direita).</summary>
    public Vector3D ArcAxis { get; }

    /// <summary>Arco: quanto o caminho gira, em radianos. Sempre positivo.</summary>
    public double ArcAngle { get; }

    private SweepSegment(bool isArc, Vector3D lineVector, Point3D arcCenter, Vector3D arcAxis, double arcAngle)
    {
        IsArc = isArc;
        LineVector = lineVector;
        ArcCenter = arcCenter;
        ArcAxis = arcAxis;
        ArcAngle = arcAngle;
    }

    public static SweepSegment Line(Vector3D vector) =>
        new(false, vector, Point3D.Origin, Vector3D.UnitZ, 0);

    public static SweepSegment Arc(Point3D center, Vector3D axis, double angleRadians) =>
        new(true, new Vector3D(0, 0, 0), center, axis.Normalize(), angleRadians);
}

/// <summary>
/// O caminho de uma varredura, já resolvido em trechos de reta e arco — as duas únicas formas que o
/// kernel sabe construir exatamente (<c>Extrude</c> e <c>Revolve</c>), e as duas que casam face com
/// face sem vão.
/// <para>
/// A entrada típica é uma polilinha com raio de canto (<see cref="FromPolyline"/>): cada vértice
/// interno vira um arco tangente de raio R, e as retas adjacentes recuam <c>R·tan(θ/2)</c> para
/// encostar nele. É assim que se dobra um tubo de verdade.
/// </para>
/// <para>
/// A validação acontece aqui, na construção, e o que falha vira <see cref="Error"/> com
/// <see cref="Segments"/> vazio — o kernel então devolve malha vazia, como faz para qualquer
/// primitiva degenerada. A mensagem existe para quem for diagnosticar (e para os testes).
/// </para>
/// </summary>
public sealed class SweepPath
{
    /// <summary>Abaixo disto dois pontos são o mesmo ponto e dois rumos são o mesmo rumo.</summary>
    public const double Tolerance = 1e-9;

    /// <summary>A polilinha de entrada, ou o ponto inicial quando o caminho veio de um arco só.</summary>
    public IReadOnlyList<Point3D> Points { get; }

    /// <summary>Raio aplicado aos cantos; 0 quando o caminho não veio de uma polilinha.</summary>
    public double CornerRadius { get; }

    /// <summary>
    /// Onde a varredura começa. Num caminho aberto é o primeiro ponto; num fechado é o FIM do arco
    /// do primeiro vértice, já que ali não existe ponta — o vértice virou curva como os outros.
    /// </summary>
    public Point3D Start { get; }

    /// <summary>True quando o último trecho volta ao começo e o sólido não tem tampas.</summary>
    public bool IsClosed { get; }

    /// <summary>Os trechos, em ordem. Vazio quando <see cref="Error"/> não é null.</summary>
    public IReadOnlyList<SweepSegment> Segments { get; }

    /// <summary>Por que o caminho é inválido, ou null se estiver tudo certo.</summary>
    public string? Error { get; }

    private SweepPath(
        IReadOnlyList<Point3D> points, double cornerRadius, Point3D start, bool isClosed,
        IReadOnlyList<SweepSegment> segments, string? error)
    {
        Points = points;
        CornerRadius = cornerRadius;
        Start = start;
        IsClosed = isClosed;
        Segments = segments;
        Error = error;
    }

    private static SweepPath Invalid(IReadOnlyList<Point3D> points, double cornerRadius, string error) =>
        new(points, cornerRadius, points.Count > 0 ? points[0] : Point3D.Origin, false, Array.Empty<SweepSegment>(), error);

    /// <summary>Rumo do caminho em <see cref="Start"/> — a orientação inicial da seção.</summary>
    public Vector3D StartTangent
    {
        get
        {
            if (Segments.Count == 0) return Vector3D.UnitZ;
            var first = Segments[0];
            if (!first.IsArc) return first.LineVector.Normalize();

            // Num arco, a tangente é perpendicular ao eixo e ao raio, pela regra da mão direita.
            return first.ArcAxis.CrossProduct(Start - first.ArcCenter).Normalize();
        }
    }

    /// <summary>
    /// O referencial da seção no começo de cada trecho, mais o do fim do último. Transportar é
    /// aplicar a cada arco a MESMA rotação que o <c>Revolve</c> aplica ao sólido — é isso que
    /// mantém referencial e geometria em acordo e torna o transporte paralelo (sem torção
    /// acumulada). Vive aqui, e não na feature, para que a conferência de fechamento de um caminho
    /// fechado use exatamente o mesmo transporte que a geometria vai usar.
    /// </summary>
    public IReadOnlyList<WorkPlane> Stations(WorkPlane startPlane)
    {
        var stations = new List<WorkPlane>(Segments.Count + 1) { startPlane };
        var plane = startPlane;

        foreach (var segment in Segments)
        {
            if (segment.IsArc)
            {
                if (segment.ArcAxis.Length < Tolerance) break;

                var rotation = Placement.RotationAbout(segment.ArcCenter, segment.ArcAxis, segment.ArcAngle * 180.0 / Math.PI);
                plane = new WorkPlane(
                    rotation.Apply(plane.Origin),
                    rotation.ApplyVector(plane.Normal),
                    rotation.ApplyVector(plane.XAxis));
            }
            else
            {
                plane = new WorkPlane(plane.Origin + segment.LineVector, plane.Normal, plane.XAxis);
            }

            stations.Add(plane);
        }

        return stations;
    }

    /// <summary>
    /// Um arco só, a partir do ponto e do rumo iniciais. <paramref name="bendDirection"/> aponta do
    /// início para o centro do arco e define o sentido da dobra; o sinal do ângulo não.
    /// </summary>
    public static SweepPath Arc(Point3D start, Vector3D startTangent, Vector3D bendDirection, double bendRadius, double angleDegrees)
    {
        var points = new[] { start };
        double angle = angleDegrees * Math.PI / 180.0;

        if (bendRadius <= Tolerance) return Invalid(points, 0, $"O raio de curvatura precisa ser positivo (recebido {Fmt(bendRadius)}).");
        if (angle <= Tolerance) return Invalid(points, 0, $"O ângulo da dobra precisa ser positivo (recebido {Fmt(angleDegrees)}°).");
        if (angle > 2.0 * Math.PI + 1e-9) return Invalid(points, 0, $"O ângulo da dobra passa de uma volta inteira ({Fmt(angleDegrees)}°).");

        var tangent = startTangent.Normalize();
        if (tangent.Length < Tolerance) return Invalid(points, 0, "O rumo inicial não pode ser nulo.");

        // Só o componente perpendicular à tangente define a dobra.
        var radial = bendDirection - tangent * bendDirection.DotProduct(tangent);
        var bendDir = radial.Normalize();
        if (bendDir.Length < Tolerance)
            return Invalid(points, 0, "A direção da dobra é paralela ao rumo do caminho e não define arco nenhum.");

        var axis = tangent.CrossProduct(bendDir);
        var center = start + bendDir * bendRadius;
        return new SweepPath(points, 0, start, false, new[] { SweepSegment.Arc(center, axis, angle) }, null);
    }

    /// <summary>
    /// Uma volta inteira: um arco só de 360° em torno de <paramref name="axis"/>. É o que um CÍRCULO
    /// do esboço quer dizer como caminho (anel, toro).
    /// <para>
    /// Não é um laço de polilinha: não há canto nenhum, logo não há raio de canto, e o
    /// <c>Revolve</c> de 2π fecha o sólido sozinho — sem tampas e sem emenda. O ponto de partida é
    /// um lugar qualquer da circunferência, como num caminho fechado de
    /// <see cref="FromPolyline"/>.
    /// </para>
    /// </summary>
    public static SweepPath FullCircle(Point3D center, Vector3D axis, double radius)
    {
        var fallback = new[] { center };

        if (radius <= Tolerance)
            return Invalid(fallback, 0, $"O raio do caminho circular precisa ser positivo (recebido {Fmt(radius)}).");

        // Normalize() devolve o vetor nulo abaixo de 1e-6: confere DEPOIS, como em Arc.
        var a = axis.Normalize();
        if (a.Length < Tolerance) return Invalid(fallback, 0, "O eixo do caminho circular não pode ser nulo.");

        // Uma perpendicular qualquer ao eixo. O XAxis do WorkPlane do eixo é a escolha que o resto
        // do app já usa para isso, e é determinística.
        var start = center + new WorkPlane(a, center).XAxis * radius;

        var path = new SweepPath(
            new[] { start }, 0, start, isClosed: true,
            new[] { SweepSegment.Arc(center, a, 2.0 * Math.PI) }, null);

        // Defesa: uma volta inteira fecha por construção (a torção sai na ordem de 1e-14°), mas a
        // conferência é a mesma que a geometria vai usar e não custa nada.
        return path.ClosureError() is { } erro ? Invalid(fallback, 0, erro) : path;
    }

    /// <summary>
    /// O maior raio de canto que ainda CABE nesta polilinha, ou infinito quando não há canto nenhum
    /// para limitar.
    /// <para>
    /// Todo recuo é proporcional ao raio (<c>R·tan(θ/2)</c>), e a restrição é que dois cantos
    /// vizinhos não comam a mesma reta duas vezes. Então o limite de cada trecho é
    /// <c>comprimento / (tan(θᵃ/2) + tan(θᵇ/2))</c> e o do caminho é o menor deles. Serve para
    /// ajustar um raio pedido grande demais em vez de só recusar o caminho.
    /// </para>
    /// </summary>
    public static double MaxCornerRadius(IEnumerable<Point3D> points, bool closed = false)
    {
        var pts = Deduplicate(points, closed);
        int count = pts.Count;
        if (count < (closed ? 3 : 2)) return 0;

        var g = MeasurePolyline(pts, closed);
        if (g.Reversal >= 0) return 0;                       // reversão: não se arredonda

        double limite = double.PositiveInfinity;
        for (int i = 0; i < g.Lens.Length; i++)
        {
            double soma = g.TanHalf[i] + g.TanHalf[(i + 1) % count];
            if (soma <= 0) continue;                         // trecho entre dois pontos sem canto
            limite = Math.Min(limite, g.Lens[i] / soma);
        }

        return limite;
    }

    /// <summary>
    /// A geometria de recuo de uma polilinha: direção e comprimento de cada trecho, e o ângulo de
    /// cada vértice com o seu <c>tan(θ/2)</c> — as duas contas de que o recuo <c>R·tan(θ/2)</c>
    /// precisa. Vértice alinhado e ponta de caminho aberto saem com ângulo zero, que é o que
    /// significa "não vira canto".
    /// </summary>
    private sealed record PolylineGeometry(
        Vector3D[] Dirs,
        double[] Lens,
        double[] Angles,
        double[] TanHalf,
        /// <summary>Vértice que reverte 180°, ou -1. Uma reversão não se arredonda.</summary>
        int Reversal);

    /// <summary>
    /// Mede a polilinha uma vez. Existe para <see cref="FromPolyline"/> e
    /// <see cref="MaxCornerRadius"/> usarem a MESMA conta: eram duas cópias, e duas cópias de uma
    /// conta de tolerância derivam na primeira vez que alguém mexe só numa delas.
    /// </summary>
    private static PolylineGeometry MeasurePolyline(List<Point3D> pts, bool closed)
    {
        int count = pts.Count;
        int edges = closed ? count : count - 1;

        var dirs = new Vector3D[edges];
        var lens = new double[edges];
        for (int i = 0; i < edges; i++)
        {
            var d = pts[(i + 1) % count] - pts[i];
            lens[i] = d.Length;
            dirs[i] = d.Normalize();
        }

        var angles = new double[count];
        var tanHalf = new double[count];
        int reversal = -1;

        // Num caminho aberto as duas pontas não são canto; num fechado todo vértice é.
        int primeiroCanto = closed ? 0 : 1;
        int ultimoCanto = closed ? count - 1 : count - 2;

        for (int v = primeiroCanto; v <= ultimoCanto; v++)
        {
            var entrada = dirs[(v - 1 + edges) % edges];
            var saida = dirs[v % edges];

            double cos = Math.Clamp(entrada.DotProduct(saida), -1.0, 1.0);
            double theta = Math.Acos(cos);

            if (theta < 1e-6) continue;                      // alinhado: não vira canto
            if (theta > Math.PI - 1e-6)
            {
                if (reversal < 0) reversal = v;              // o primeiro basta para a mensagem
                continue;
            }

            angles[v] = theta;
            tanHalf[v] = Math.Tan(theta / 2.0);
        }

        return new PolylineGeometry(dirs, lens, angles, tanHalf, reversal);
    }

    /// <summary>
    /// Polilinha com os cantos arredondados por <paramref name="cornerRadius"/>. Funciona em 3D:
    /// cada arco vive no plano dos dois segmentos que ele liga. Cantos alinhados não viram arco (as
    /// retas seguem como uma só).
    /// <para>
    /// Com <paramref name="closed"/>, a aresta do último ponto de volta ao primeiro também entra e
    /// TODO vértice vira canto — o sólido sai sem tampas. Um laço fechado só funciona se o
    /// referencial da seção voltar ao lugar depois da volta inteira, o que vale para caminhos
    /// planos; fora de um plano o transporte paralelo acumula torção e a última face chegaria
    /// girada em relação à primeira. Isso é conferido aqui e vira <see cref="Error"/>. Vale mesmo
    /// para seção circular: o kernel varre um POLÍGONO, que não é simétrico a qualquer ângulo.
    /// </para>
    /// </summary>
    public static SweepPath FromPolyline(IEnumerable<Point3D> points, double cornerRadius, bool closed = false)
    {
        var pts = Deduplicate(points, closed);

        if (pts.Count < (closed ? 3 : 2))
            return Invalid(pts, cornerRadius, closed
                ? "Um caminho fechado precisa de pelo menos três pontos distintos."
                : "Um caminho precisa de pelo menos dois pontos distintos.");
        if (cornerRadius < 0) return Invalid(pts, cornerRadius, $"O raio de canto não pode ser negativo (recebido {Fmt(cornerRadius)}).");

        int count = pts.Count;
        int edges = closed ? count : count - 1;

        if ((closed || count > 2) && cornerRadius <= Tolerance)
            return Invalid(pts, cornerRadius, "Uma polilinha com cantos precisa de um raio de canto positivo: canto vivo não se constrói com varredura.");

        var g = MeasurePolyline(pts, closed);
        var dirs = g.Dirs;
        var lens = g.Lens;
        var angles = g.Angles;

        if (g.Reversal is var rev && rev >= 0)
            return Invalid(pts, cornerRadius, $"O caminho dobra 180° no ponto {rev} {Fmt(pts[rev])}: não dá para arredondar uma reversão.");

        // Recuo de cada vértice que vira canto. Num caminho aberto as duas pontas não recuam.
        var trims = new double[count];
        for (int v = 0; v < count; v++) trims[v] = cornerRadius * g.TanHalf[v];

        int ultimoCanto = closed ? count - 1 : count - 2;

        // Dois cantos vizinhos não podem comer a mesma reta duas vezes.
        for (int i = 0; i < edges; i++)
        {
            int fim = (i + 1) % count;
            double needed = trims[i] + trims[fim];
            if (needed > lens[i] + 1e-9)
            {
                return Invalid(pts, cornerRadius,
                    $"O raio de canto {Fmt(cornerRadius)} não cabe entre os pontos {i} {Fmt(pts[i])} e {fim} {Fmt(pts[fim])}: " +
                    $"os arredondamentos pedem {Fmt(needed)} de um trecho de {Fmt(lens[i])}.");
            }
        }

        var segments = new List<SweepSegment>();
        Point3D cursor;
        Point3D start;

        if (closed)
        {
            // Começa DEPOIS do arco do vértice 0: num laço não há ponta onde começar, então o
            // ponto de partida é um lugar qualquer do caminho, e o fim do primeiro arco serve.
            if (trims[0] <= Tolerance)
                return Invalid(pts, cornerRadius, "O primeiro ponto de um caminho fechado precisa ser um canto de verdade, não um ponto alinhado.");
            start = pts[0] + dirs[0] * trims[0];
            cursor = start;
        }
        else
        {
            start = pts[0];
            cursor = start;
        }

        for (int v = closed ? 1 : 1; v <= ultimoCanto; v++)
        {
            if (trims[v] <= Tolerance) continue;                   // alinhado: a reta segue adiante

            var entrada = dirs[(v - 1 + edges) % edges];
            var saida = dirs[v % edges];
            var arcStart = pts[v] - entrada * trims[v];

            var toCursor = arcStart - cursor;
            if (toCursor.Length > Tolerance) segments.Add(SweepSegment.Line(toCursor));

            // Perpendicular à entrada no plano das duas direções, apontando para dentro da curva.
            var bendDir = (saida - entrada * entrada.DotProduct(saida)).Normalize();
            segments.Add(SweepSegment.Arc(arcStart + bendDir * cornerRadius, entrada.CrossProduct(bendDir), angles[v]));

            cursor = pts[v] + saida * trims[v];
        }

        if (closed)
        {
            // Fecha: reta até o começo do arco do vértice 0, e o próprio arco.
            var entrada = dirs[edges - 1];
            var saida = dirs[0];
            var arcStart = pts[0] - entrada * trims[0];

            var toCursor = arcStart - cursor;
            if (toCursor.Length > Tolerance) segments.Add(SweepSegment.Line(toCursor));

            var bendDir = (saida - entrada * entrada.DotProduct(saida)).Normalize();
            segments.Add(SweepSegment.Arc(arcStart + bendDir * cornerRadius, entrada.CrossProduct(bendDir), angles[0]));
        }
        else
        {
            var tail = pts[^1] - cursor;
            if (tail.Length > Tolerance) segments.Add(SweepSegment.Line(tail));
        }

        if (segments.Count == 0) return Invalid(pts, cornerRadius, "O caminho não tem comprimento.");

        var path = new SweepPath(pts, cornerRadius, start, closed, segments, null);
        if (!closed) return path;

        return path.ClosureError() is { } erro ? Invalid(pts, cornerRadius, erro) : path;
    }

    /// <summary>
    /// Para um caminho fechado: por que a volta não fecha, ou null se fechar. Confere posição e
    /// referencial com o MESMO transporte que a geometria vai usar, então o que passar aqui gera
    /// faces coincidentes de verdade na emenda.
    /// </summary>
    private string? ClosureError()
    {
        var inicial = new WorkPlane(StartTangent, Start);
        var final = Stations(inicial)[^1];

        double desvio = final.Origin.DistanceTo(inicial.Origin);
        if (desvio > 1e-6)
            return $"O caminho fechado não volta ao ponto de partida (sobra {Fmt(desvio)}).";

        // A torção acumulada (holonomia): num laço fora de um plano o referencial não volta, e a
        // última face chegaria girada em relação à primeira.
        double cos = Math.Clamp(final.XAxis.DotProduct(inicial.XAxis), -1.0, 1.0);
        double torcao = Math.Acos(cos) * 180.0 / Math.PI;
        if (torcao > 1e-4)
            return $"O caminho fechado não é plano: depois da volta a seção chega girada {Fmt(torcao)}° " +
                   "em relação ao começo, e as duas faces da emenda não coincidiriam.";

        return null;
    }

    private static List<Point3D> Deduplicate(IEnumerable<Point3D> points, bool closed)
    {
        var result = new List<Point3D>();
        foreach (var p in points)
        {
            if (result.Count > 0 && result[^1].DistanceTo(p) < 1e-9) continue;
            result.Add(p);
        }
        // Num caminho fechado, repetir o primeiro ponto no fim é a grafia natural — mas a aresta de
        // volta é implícita, então o ponto repetido sobra.
        if (closed && result.Count > 1 && result[0].DistanceTo(result[^1]) < 1e-9) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Fmt(Point3D p) => $"({Fmt(p.X)}, {Fmt(p.Y)}, {Fmt(p.Z)})";
}
