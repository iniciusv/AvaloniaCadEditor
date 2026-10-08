namespace AvaloniaCadEditor.Analysis;

/// <summary>Tolerâncias da análise de relações. Todas as distâncias em unidades da cena (mm).</summary>
public sealed class AnalysisOptions
{
    /// <summary>Duas coordenadas a menos que isto são "a mesma" (um alinhamento de verdade). Um pouco
    /// mais folgada que a igualdade de ponto flutuante: valores derivados (como o centro de um arco
    /// reajustado a partir de vértices arredondados) carregam ruído numérico próprio.</summary>
    public double AlignTolerance { get; init; } = 3e-3;

    /// <summary>Coordenadas mais distantes que <see cref="AlignTolerance"/> mas a menos que isto são um
    /// quase-alinhamento (provável erro de modelagem), e não são ignoradas.</summary>
    public double NearMissTolerance { get; init; } = 0.5;

    /// <summary>Quão perto um centro de espelhamento candidato precisa estar de outro para os dois se juntarem.</summary>
    public double SymmetryTolerance { get; init; } = 1e-3;

    /// <summary>Uma aresta de perfil está alinhada a um eixo quando |dx ou dy| / comprimento fica abaixo disto.</summary>
    public double EdgeAngleTolerance { get; init; } = 0.03;

    /// <summary>Tolerância relativa (fração do raio) para casar circuncentros consecutivos de um arco.</summary>
    public double ArcCenterTolerance { get; init; } = 0.08;

    /// <summary>Tolerância relativa para comparar raios consecutivos de um arco.</summary>
    public double ArcRadiusTolerance { get; init; } = 0.1;

    /// <summary>Mínimo de vértices consecutivos (como trincas de circuncentro) para chamar algo de arco.</summary>
    public int MinArcRunLength { get; init; } = 3;

    /// <summary>Limite de furos analisados, para manter o pior caso sob controle.</summary>
    public int MaxHoles { get; init; } = 2000;

    /// <summary>Limite de fatos considerados na busca de simetria (que é quadrática).</summary>
    public int MaxSymmetryFacts { get; init; } = 1500;
}
