using AiCore.Models;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Uma conversa com a IA que gera/edita o CadScript da cena. A implementação padrão é
/// <see cref="CadScriptChatSession"/> (resposta com o arquivo completo + auto-fix); outras
/// (ex.: com ferramentas) podem ser ligadas pelo composition root sem mudar a UI.
/// </summary>
public interface ICadScriptChatSession
{
    /// <summary>Mensagens enviadas e recebidas (system, user, assistant...).</summary>
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>Manter o histórico entre turnos (multi-round); false = cada envio é independente.</summary>
    bool KeepHistory { get; set; }

    /// <summary>Começa uma nova conversa, descartando o histórico (mas mantendo as opções da IA).</summary>
    void NewConversation();

    /// <summary>Troca a chave/modelo/URL em uso (ex.: após o usuário editar as configurações na UI).</summary>
    void UpdateOptions(LlmOptions options);

    /// <summary>Remove um turno inteiro do histórico. <paramref name="turnIndex"/> = posição ordinal do turno.</summary>
    void DeleteTurn(int turnIndex);

    Task<CadScriptTurnResult> SendAsync(string instruction, CadScriptContext context, CancellationToken cancellationToken = default);
}
