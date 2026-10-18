namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>
/// Até onde um agente pode ir num turno. Na DeepSeek cada passo custa (o contexto inteiro vai de
/// novo); num modelo local é grátis, então os limites podem ser bem maiores.
/// </summary>
public sealed record AgentLimits
{
    /// <summary>Chamadas de ferramenta por turno.</summary>
    public int MaxToolSteps { get; init; } = 15;

    /// <summary>Tempo total do turno, somando as idas ao modelo e as ferramentas.</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(10);

    public static AgentLimits Remote { get; } = new();

    public static AgentLimits Local { get; } = new() { MaxToolSteps = 60, MaxDuration = TimeSpan.FromMinutes(60) };
}
