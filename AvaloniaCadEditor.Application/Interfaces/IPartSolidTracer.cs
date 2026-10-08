using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>O que, no código, alimenta um sólido criado dentro do <c>Build()</c> de uma peça.</summary>
public enum SolidInputKind
{
    /// <summary>Parâmetro do construtor primário do record da peça — o "botão" que o inspector edita.</summary>
    PartParameter,

    /// <summary>Variável local declarada no próprio <c>Build()</c> (ex.: <c>var raioFuro = ...</c>).</summary>
    Local,

    /// <summary>Função local do <c>Build()</c> que calcula um valor (ex.: <c>CantoBaseX</c>).</summary>
    LocalFunction,

    /// <summary>Campo const/estático declarado nos arquivos da cena (ex.: uma <c>static class Medidas</c>).</summary>
    Constant
}

/// <summary>Uma entrada da cadeia que produz um sólido.</summary>
/// <param name="Name">Nome do símbolo (ex.: "DistanciaFuroPlaca1Y", "raioFuro").</param>
/// <param name="Kind">De onde o valor vem.</param>
/// <param name="ValueCode">
/// Valor padrão do parâmetro do record, ou a expressão que calcula o local; null quando não há
/// (ex.: parâmetro sem valor padrão).
/// </param>
/// <param name="Spans">Declaração e ocorrências, para destacar no editor.</param>
/// <param name="SharedWith">
/// Outros sólidos nomeados do mesmo <c>Build()</c> que também dependem desta entrada. Vazio =
/// exclusiva do sólido rastreado, então mexer nela move só ele.
/// </param>
public sealed record SolidInput(
    string Name,
    SolidInputKind Kind,
    string? ValueCode,
    IReadOnlyList<CodeLocation> Spans,
    IReadOnlyList<string> SharedWith)
{
    /// <summary>True se nenhum outro sólido da peça depende desta entrada.</summary>
    public bool IsExclusive => SharedWith.Count == 0;
}

/// <summary>Resultado de rastrear um sólido de peça: onde ele nasce e o que o alimenta.</summary>
/// <param name="Declaration">A instrução inteira que declara o sólido.</param>
/// <param name="Inputs">Entradas da cadeia, exclusivas primeiro e, dentro de cada grupo, por nome.</param>
public sealed record PartSolidInputs(
    string PartTypeName,
    string SolidName,
    CodeLocation Declaration,
    IReadOnlyList<SolidInput> Inputs);

/// <summary>
/// Rastreia, com o modelo semântico do Roslyn, tudo que alimenta um sólido nomeado criado dentro
/// do <c>Build()</c> de uma peça — variáveis locais, funções locais, constantes e, no fim da
/// cadeia, os parâmetros do record. Serve para destacar a cadeia no editor e para o inspector
/// saber quais parâmetros afetam o sólido selecionado (e quais afetam só ele).
/// </summary>
public interface IPartSolidTracer
{
    /// <summary>
    /// A cadeia que produz <paramref name="solidName"/> dentro do record
    /// <paramref name="partTypeName"/>. Null quando a peça ou o sólido não são achados — por
    /// exemplo, um sólido criado num laço com nome interpolado.
    /// </summary>
    PartSolidInputs? Trace(IReadOnlyList<ScriptDocument> documents, string partTypeName, string solidName);

    /// <summary>
    /// Os parâmetros do record que alimentam UM argumento do construtor do sólido — por exemplo, o
    /// <c>radius</c> de <c>new CylinderFeature(centro, eixo, RaioFuroPassante, ...)</c> devolve
    /// <c>["RaioFuroPassante"]</c>. Atravessa locais e funções locais como <see cref="Trace"/>.
    /// Null quando a peça, o sólido ou o argumento não são achados.
    /// </summary>
    /// <param name="constructorParameterName">Nome do parâmetro do construtor da feature (ex.: "radius").</param>
    IReadOnlyList<string>? TraceArgument(IReadOnlyList<ScriptDocument> documents, string partTypeName, string solidName, string constructorParameterName);
}
