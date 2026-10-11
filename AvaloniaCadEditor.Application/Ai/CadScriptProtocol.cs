using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AiCore.Abstractions;
using AvaloniaCadEditor.Application.Ai.Prompts;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Protocolo de IA para o "CadScript": a IA recebe o código C# vigente do arquivo ativo da cena
/// mais o pedido em linguagem natural, e deve responder com um texto curto seguido (quando a
/// geometria muda) de um único bloco de código contendo o ARQUIVO COMPLETO resultante.
/// Uma cena pode ter um único arquivo (statements soltos) ou vários arquivos — nesse caso, o
/// arquivo ativo tanto pode ser o de entrada (mesmo formato) quanto um arquivo de peça em Pecas/
/// (um record que herda de Part). O formato exigido em cada turno vem em <see cref="CadScriptContext.Kind"/>
/// e é repetido na mensagem do usuário, já que pode mudar de um turno para o outro.
/// Num projeto, o contexto pode trazer vários arquivos (<see cref="CadScriptContext.Scope"/>):
/// a IA então devolve um bloco <c>```cadscript file=Caminho.cs</c> por arquivo alterado.
/// O system prompt vem de um <see cref="ICadScriptPromptProfile"/>; a mensagem do usuário e o parser
/// são os mesmos para todos os perfis.
/// </summary>
public class CadScriptProtocol : IAiProtocol<CadScriptContext, CadScriptChanges>
{
    private static readonly Regex CodeBlockRegex = new(
        @"```(?:cadscript|csharp|cs)?[ \t]*(?:file\s*=\s*""?(?<file>[^\s""`]+)""?)?[ \t]*\r?\n(?<code>.*?)```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private readonly ICadScriptPromptProfile _profile;

    /// <param name="profile">A versão do pré-prompt; null = <see cref="CadScriptPromptProfiles.Default"/>.</param>
    public CadScriptProtocol(ICadScriptPromptProfile? profile = null)
    {
        _profile = profile ?? CadScriptPromptProfiles.Default;
    }

    public ICadScriptPromptProfile Profile => _profile;

    public string GetSystemPrompt() => _profile.BuildSystemPrompt();

    public string BuildUserMessage(CadScriptContext currentContext, string userInstruction)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### PEDIDO");
        sb.AppendLine(userInstruction);
        sb.AppendLine();

        if (currentContext.IsMultiFile)
        {
            AppendScopeFileList(sb, currentContext);
        }
        else
        {
            sb.AppendLine("### ARQUIVO ATIVO");
            string fileName = string.IsNullOrWhiteSpace(currentContext.FileName) ? "Cena.cs" : currentContext.FileName;
            sb.AppendLine($"{fileName} — formato: {KindLabel(currentContext.Kind)}");
        }

        if (currentContext.PartSummaries is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine(currentContext.IsMultiFile
                ? "### OUTRAS PEÇAS DECLARADAS NA CENA (fora do contexto; reaproveite com Add(...); não redeclare)"
                : "### PEÇAS JÁ DECLARADAS NA CENA (reaproveite com Add(...); não redeclare)");
            foreach (var summary in currentContext.PartSummaries)
            {
                sb.AppendLine("  " + summary);
            }
        }

        if (currentContext.IsMultiFile)
        {
            foreach (var doc in currentContext.Scope!.Documents)
            {
                sb.AppendLine();
                sb.AppendLine($"```cadscript file={doc.FilePath}");
                sb.AppendLine(string.IsNullOrWhiteSpace(doc.Text) ? "// (vazio)" : doc.Text.TrimEnd());
                sb.AppendLine("```");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("### CÓDIGO ATUAL DESTE ARQUIVO");
            sb.AppendLine("```cadscript");
            sb.AppendLine(string.IsNullOrWhiteSpace(currentContext.CurrentCode) ? "// (vazio)" : currentContext.CurrentCode.TrimEnd());
            sb.AppendLine("```");
        }

        if (currentContext.Scope?.SelectedFeatures is { Count: > 0 } selectedFeatures)
        {
            sb.AppendLine();
            sb.AppendLine("### SÓLIDOS SELECIONADOS");
            foreach (var selected in selectedFeatures)
            {
                sb.AppendLine("  " + selected);
            }
        }
        else if (!string.IsNullOrWhiteSpace(currentContext.SelectedFeatureName))
        {
            sb.AppendLine();
            sb.AppendLine("### FEATURE SELECIONADA");
            sb.AppendLine(currentContext.SelectedFeatureName);
        }

        if (!string.IsNullOrWhiteSpace(currentContext.ErrorFeedback))
        {
            sb.AppendLine();
            sb.AppendLine("### ERRO DO ARQUIVO ANTERIOR");
            sb.AppendLine(currentContext.ErrorFeedback);
        }

        return sb.ToString();
    }

    private static void AppendScopeFileList(StringBuilder sb, CadScriptContext context)
    {
        sb.AppendLine("### ARQUIVOS NO CONTEXTO (responda com um bloco ```cadscript file=<caminho> por arquivo alterado)");
        foreach (var doc in context.Scope!.Documents)
        {
            sb.AppendLine($"{doc.FilePath} — formato: {KindLabel(context.KindOf(doc.FilePath))}");
        }
    }

    private static string KindLabel(CadFileKind kind) => kind switch
    {
        CadFileKind.PartFile => "peça (record que herda de Part; sem statements soltos, sem AddFeature/Add)",
        CadFileKind.SupportFile => "auxiliar (só declarações de tipos/constantes; sem statements soltos)",
        CadFileKind.SceneEntry => "script — arquivo de entrada da cena (statements soltos + AddFeature/Add)",
        _ => "script — arquivo único, sem projeto (statements soltos + AddFeature; não existe Pecas/: construa toda a geometria aqui)"
    };

    /// <summary>
    /// Extrai o conteúdo de cada arquivo devolvido. Bloco com <c>file=</c> → aquele arquivo; bloco sem →
    /// o arquivo ativo (<see cref="CadScriptFileEdit.FilePath"/> null). Se o mesmo arquivo vier repetido,
    /// vale o último bloco.
    /// </summary>
    public ProtocolParseResult<CadScriptChanges> ParseResponse(string rawAiResponse)
    {
        if (string.IsNullOrWhiteSpace(rawAiResponse))
        {
            return new ProtocolParseResult<CadScriptChanges> { ChatMessage = string.Empty, Payload = null };
        }

        var matches = CodeBlockRegex.Matches(rawAiResponse);
        if (matches.Count == 0)
        {
            return new ProtocolParseResult<CadScriptChanges> { ChatMessage = rawAiResponse.Trim(), Payload = null };
        }

        var files = new List<CadScriptFileEdit>();
        foreach (Match match in matches)
        {
            string? file = match.Groups["file"].Success ? match.Groups["file"].Value.Replace('\\', '/') : null;
            string code = match.Groups["code"].Value.Trim();
            files.RemoveAll(f => string.Equals(f.FilePath, file, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(code)) files.Add(new CadScriptFileEdit(file, code));
        }

        string chatText = CodeBlockRegex.Replace(rawAiResponse, string.Empty).Trim();

        return new ProtocolParseResult<CadScriptChanges>
        {
            ChatMessage = string.IsNullOrWhiteSpace(chatText) ? "Código gerado." : chatText,
            Payload = files.Count == 0 ? null : new CadScriptChanges(files)
        };
    }
}
