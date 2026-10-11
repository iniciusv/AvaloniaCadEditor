using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// A parte do system prompt que nenhum perfil pode mudar: o formato da resposta que o
/// <see cref="CadScriptProtocol.ParseResponse"/> sabe ler (blocos <c>```cadscript</c>, com <c>file=</c> no modo
/// multi-arquivo) e as seções que o <see cref="CadScriptProtocol.BuildUserMessage"/> escreve.
/// </summary>
public static class CadScriptResponseFormat
{
    public const string Text =
        """
        FORMATO OBRIGATÓRIO DA RESPOSTA:
        1. Um parágrafo curto (1 a 3 frases), em português, explicando o que foi feito ou respondendo à pergunta.
        2. Com "### ARQUIVO ATIVO": se — e somente se — o pedido exigir alterar o arquivo, inclua logo em seguida
           UM ÚNICO bloco de código delimitado por ```cadscript e ``` contendo o ARQUIVO COMPLETO resultante (nunca
           um trecho, nunca um diff: sempre o arquivo inteiro, pronto para substituí-lo e ser executado).
        3. Com "### ARQUIVOS NO CONTEXTO": inclua UM bloco por arquivo ALTERADO (ou criado), cada um aberto com
           ```cadscript file=<caminho exato do arquivo> e contendo o ARQUIVO COMPLETO resultante. NÃO repita arquivos
           que não mudaram. Só altere arquivos listados no contexto; o único arquivo novo permitido é uma peça em
           Pecas/. Exemplo de resposta que altera dois arquivos:
           ```cadscript file=Pecas/Placa.cs
           public sealed record Placa(double Largura = 120, double Comprimento = 60, double Espessura = 5) : Part { ... }
           ```
           ```cadscript file=Cena.cs
           var placa_1 = Add(new Placa(), at: Placement.At(0, 0, 0));
           ```
        4. Se o pedido for apenas uma pergunta, ou pedir algo que não cabe nos formatos indicados,
           responda somente com o texto, sem bloco de código.
        """;

    /// <summary>Escreve cada exemplo do tipo pedido num bloco <c>```cadscript</c>.</summary>
    internal static void AppendExamples(StringBuilder sb, IEnumerable<CadScriptPromptExample> examples, CadScriptExampleKind kind)
    {
        foreach (var example in examples.Where(e => e.Kind == kind))
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Code.Trim());
            sb.AppendLine("```");
        }
    }
}
