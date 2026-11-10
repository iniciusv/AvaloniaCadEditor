using System.Globalization;
using AvaloniaCadEditor.Application.Interfaces;

namespace AvaloniaCadEditor.Application.Levers;

/// <summary>De onde vem o número que a alavanca troca.</summary>
public enum CodeLeverKind
{
    /// <summary>Um literal no corpo do record da peça (no <c>Build()</c>, em geral).</summary>
    Literal,

    /// <summary>Um literal nos argumentos de <c>new Peca(...)</c> na cena.</summary>
    Argument,

    /// <summary>O valor padrão de um parâmetro do record (<c>double Largura = 80</c>): vale para toda
    /// criação da peça que não passa o parâmetro.</summary>
    Default
}

/// <summary>
/// Um número do código que pode mudar a peça: um literal (<c>10</c>, <c>-2.5</c>) num lugar exato do
/// fonte. É o que uma cota pode "puxar". Imutável; o <see cref="Id"/> só vale dentro da sonda que o criou.
/// </summary>
/// <param name="Id">Identificador na sonda (<see cref="ILeverProbe"/>).</param>
/// <param name="Location">O trecho do literal no fonte (com o sinal, se for <c>-5</c>).</param>
/// <param name="Text">O texto do trecho quando a sonda foi criada: gravar confere que ainda é este.</param>
/// <param name="Value">O valor atual.</param>
/// <param name="Owner">A quem o número pertence: o sólido (<c>furo_a</c>), a peça na cena, ou o parâmetro.</param>
/// <param name="Path">O que o número preenche, do mais externo ao mais interno (<c>center.x</c>); vazio
/// quando não se sabe.</param>
public sealed record CodeLever(
    int Id,
    CodeLeverKind Kind,
    CodeLocation Location,
    string Text,
    double Value,
    string Owner,
    string Path)
{
    /// <summary>"furo_a · center.x", "Largura (padrão)".</summary>
    public string Label => Kind == CodeLeverKind.Default
        ? $"{Owner} (padrão)"
        : string.IsNullOrEmpty(Path) ? Owner : $"{Owner} · {Path}";

    /// <summary>O literal foi escrito como <c>double</c> (<c>2.5</c>, <c>3e2</c>, <c>4d</c>): continua
    /// assim, ou <c>var x = 2.5;</c> viraria <c>int</c>.</summary>
    public bool KeepsDoubleSyntax => Text.IndexOfAny(['.', 'e', 'E', 'd', 'D']) >= 0;

    /// <summary>O texto a gravar para <paramref name="value"/>: até 4 casas, ponto decimal.</summary>
    public string Format(double value)
    {
        double rounded = Math.Round(value, 4);
        if (rounded == 0) rounded = 0; // sem "-0"
        var text = rounded.ToString("0.####", CultureInfo.InvariantCulture);
        if (KeepsDoubleSyntax && !text.Contains('.')) text += ".0";
        return text;
    }

    /// <summary><paramref name="value"/> como ficará gravado (o mesmo arredondamento de <see cref="Format"/>).</summary>
    public static double Round(double value) => Math.Round(value, 4);
}
