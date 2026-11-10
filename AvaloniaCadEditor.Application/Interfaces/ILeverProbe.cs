using System;
using System.Collections.Generic;
using System.Threading;
using AvaloniaCadEditor.Application.Levers;
using AvaloniaCadEditor.Domain.Features;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>
/// Cria sondas: a cena compilada uma vez com os números de uma peça trocáveis. É a porta para o
/// Roslyn; o Application não sabe como os números são trocados.
/// </summary>
public interface ILeverProbeFactory
{
    /// <summary>
    /// A sonda dos números que mudam a peça <paramref name="partTypeName"/>: os literais do record, os
    /// argumentos de <c>new Peca(...)</c> na cena e os valores padrão dos parâmetros. <c>null</c> quando a
    /// peça não é achada ou a cena não compila.
    /// </summary>
    ILeverProbe? Create(IReadOnlyList<ScriptDocument> documents, string partTypeName, CancellationToken cancellationToken = default);
}

/// <summary>
/// A cena instrumentada, pronta para executar com valores trocados. Nada é gravado: cada execução é em
/// memória e independente das outras. Uma execução por vez (a implementação serializa as chamadas).
/// </summary>
public interface ILeverProbe : IDisposable
{
    /// <summary>Os números que a sonda sabe trocar, com o valor atual.</summary>
    IReadOnlyList<CodeLever> Levers { get; }

    /// <summary>
    /// Executa a cena com os valores de <paramref name="values"/> (por <see cref="CodeLever.Id"/>; os que
    /// faltam ficam como no código) e devolve as features de topo. <c>null</c> se a execução falhar (um
    /// valor que a peça recusa, por exemplo).
    /// </summary>
    IReadOnlyList<IFeature>? Run(IReadOnlyDictionary<int, double> values);
}
