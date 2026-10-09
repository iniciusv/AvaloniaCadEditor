using System;
using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Entities;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Commands;

/// <summary>
/// O caminho de uma varredura já interpretado a partir do desenho: o que ele é, o que ele exige, e
/// quais entidades de esboço o formaram.
/// <para>
/// Um tipo só — com duas formas — porque antes o caminho morava em três campos anuláveis paralelos
/// no <see cref="SweepCommand"/>, e todo consumidor ramificava em "é cadeia ou é círculo?". Era de
/// lá que vinham os defeitos: um estágio que falhava pela metade deixava parte dos campos
/// preenchida, e o passo seguinte emitia pulando a checagem que faltou. Com uma referência só,
/// publicar o caminho é uma atribuição, e meio-caminho deixa de ser representável.
/// </para>
/// <para>
/// O tipo é deliberadamente BURRO: não gera código. Quem emite é o
/// <see cref="SweepCommand"/>, num <c>switch</c> único — um ponto de ramificação em vez de quatro.
/// </para>
/// </summary>
/// <param name="ReferencePlane">
/// O plano que orienta a seção em torno do caminho (a normal dele vira o <c>upReference</c>). Não é
/// restrição nenhuma sobre onde a seção foi desenhada.
/// </param>
/// <param name="Source">As entidades consumidas ao concluir.</param>
internal abstract record SweepPathPlan(WorkPlane ReferencePlane, IReadOnlyList<Entity> Source)
{
    /// <summary>O caminho conferido pelo domínio, com o raio de canto escolhido.</summary>
    public abstract SweepPath Build(double cornerRadius);

    /// <summary>
    /// O maior raio de canto que cabe neste caminho, ou infinito quando não há canto para limitar.
    /// </summary>
    public abstract double MaxCornerRadius();
}

/// <summary>Cadeia de arestas: uma polilinha em coordenadas de mundo, aberta ou fechada.</summary>
internal sealed record PolylinePlan(
    IReadOnlyList<Point3D> World,
    bool IsClosed,
    WorkPlane ReferencePlane,
    IReadOnlyList<Entity> Source)
    : SweepPathPlan(ReferencePlane, Source)
{
    public override SweepPath Build(double cornerRadius) =>
        SweepPath.FromPolyline(World, cornerRadius, IsClosed);

    public override double MaxCornerRadius() =>
        SweepPath.MaxCornerRadius(World, IsClosed);
}

/// <summary>
/// Volta inteira: o que um círculo do esboço quer dizer como caminho. Não tem canto, logo nenhum
/// raio de canto o limita nem o afeta.
/// </summary>
internal sealed record FullCirclePlan(
    Point3D Center,
    Vector3D Axis,
    double Radius,
    WorkPlane ReferencePlane,
    IReadOnlyList<Entity> Source)
    : SweepPathPlan(ReferencePlane, Source)
{
    public override SweepPath Build(double cornerRadius) =>
        SweepPath.FullCircle(Center, Axis, Radius);

    public override double MaxCornerRadius() => double.PositiveInfinity;
}
