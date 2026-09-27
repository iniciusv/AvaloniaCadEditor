namespace AiCore.Abstractions
{
    /// <summary>
    /// Resultado retornado pelo parsing de uma resposta da IA seguindo um protocolo.
    /// </summary>
    /// <typeparam name="TResult">O tipo do payload estruturado extraído da resposta.</typeparam>
    public class ProtocolParseResult<TResult>
    {
        /// <summary>
        /// Conteúdo de texto amigável destinado à exibição no chat da interface.
        /// </summary>
        public string ChatMessage { get; set; } = string.Empty;

        /// <summary>
        /// O payload técnico/estruturado parseado a partir da resposta (ex: Código C#, Query SQL, JSON, etc.).
        /// </summary>
        public TResult? Payload { get; set; }

        /// <summary>
        /// Indica se a resposta continha um payload válido do protocolo.
        /// </summary>
        public bool HasPayload => Payload != null;
    }

    /// <summary>
    /// Contrato comum para qualquer protocolo de comunicação com IA.
    /// Cada implementação define o system prompt, monta a mensagem com o contexto e
    /// faz o parse da resposta bruta em um payload estruturado.
    /// </summary>
    /// <typeparam name="TContext">O tipo do contexto/estado do domínio passado para a IA.</typeparam>
    /// <typeparam name="TResult">O tipo do payload resultante produzido pela IA.</typeparam>
    public interface IAiProtocol<TContext, TResult>
    {
        /// <summary>
        /// Retorna o System Prompt que instrui a IA sobre seu papel e o formato estrito da resposta.
        /// </summary>
        string GetSystemPrompt();

        /// <summary>
        /// Monta a mensagem do usuário injetando o contexto atual junto com a solicitação do usuário.
        /// </summary>
        /// <param name="currentContext">O contexto atual da aplicação.</param>
        /// <param name="userInstruction">A solicitação em linguagem natural feita pelo usuário.</param>
        /// <returns>A mensagem formatada para ser enviada à IA.</returns>
        string BuildUserMessage(TContext currentContext, string userInstruction);

        /// <summary>
        /// Processa a resposta bruta da IA, separando a mensagem amigável de chat do payload de protocolo.
        /// </summary>
        /// <param name="rawAiResponse">A resposta bruta retornada pela IA.</param>
        /// <returns>O resultado contendo a mensagem de chat e o payload estruturado.</returns>
        ProtocolParseResult<TResult> ParseResponse(string rawAiResponse);
    }
}
