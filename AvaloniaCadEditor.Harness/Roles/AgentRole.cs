namespace AvaloniaCadEditor.Harness.Roles;

/// <summary>O papel de um agente no turno; cada papel aponta para um perfil de endpoint.</summary>
public enum AgentRole
{
    /// <summary>Conversa com o usuário e edita a cena (com todas as ferramentas).</summary>
    Editor,

    /// <summary>Só lê a geometria e resume os fatos para o Editor (ferramentas de leitura).</summary>
    Inspector,

    /// <summary>Reservado: revisa o resultado do turno contra o pedido.</summary>
    Reviewer
}
