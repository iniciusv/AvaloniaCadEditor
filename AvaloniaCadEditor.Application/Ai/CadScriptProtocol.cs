using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AiCore.Abstractions;

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
/// </summary>
public class CadScriptProtocol : IAiProtocol<CadScriptContext, CadScriptChanges>
{
    private static readonly Regex CodeBlockRegex = new(
        @"```(?:cadscript|csharp|cs)?[ \t]*(?:file\s*=\s*""?(?<file>[^\s""`]+)""?)?[ \t]*\r?\n(?<code>.*?)```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Exemplos few-shot de SCRIPT (arquivo único ou de entrada) mostrados à IA. Cobertos por teste
    /// que garante que cada um deles realmente compila e executa no <c>RoslynSceneEvaluator</c> —
    /// ou seja, o "dialeto" ensinado à IA é sempre válido.
    /// </summary>
    public static readonly IReadOnlyList<string> Examples = new[]
    {
        // Caixa simples
        """
        var box_a1b2 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_a1b2" };
        AddFeature(box_a1b2);
        """,

        // Furo cilíndrico numa caixa (diferença booleana)
        """
        var box_1234 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_1234" };
        AddFeature(box_1234);
        var hole_5678 = new CylinderFeature(globalBaseCenter: new Point3D(25, 15, 0), globalDirection: new Vector3D(0, 0, 1), radius: 4, height: 10) { Name = "hole_5678", IsTransientTool = true };
        AddFeature(hole_5678);
        var boolOp_9abc = BooleanFeature.CreateDifference("boolOp_9abc", box_1234, hole_5678);
        AddFeature(boolOp_9abc);
        """,

        // Furo escareado: cone truncado subtraído a partir da face de cima (topRadius menor = fundo do furo).
        // ConeFeature cobre cone completo (topRadius: 0), tronco de cone e chanfro cônico de furo.
        """
        var chapa_a1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 40, length: 40, height: 10) { Name = "chapa_a1" };
        AddFeature(chapa_a1);
        var escareado_b2 = new ConeFeature(globalBaseCenter: new Point3D(20, 20, 10), globalDirection: new Vector3D(0, 0, -1), bottomRadius: 6, topRadius: 3, height: 4) { Name = "escareado_b2", IsTransientTool = true };
        AddFeature(escareado_b2);
        var boolOp_c3 = BooleanFeature.CreateDifference("boolOp_c3", chapa_a1, escareado_b2);
        AddFeature(boolOp_c3);
        """,

        // Fillet (arredondamento) em todas as arestas de uma caixa
        """
        var box_edge1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 40, length: 40, height: 20) { Name = "box_edge1" };
        AddFeature(box_edge1);
        var fillet_edge2 = new FilletFeature(name: "fillet_edge2", baseFeature: box_edge1, radius: 3) { Mode = FilletMode.Round, ApplyToAllEdges = true };
        AddFeature(fillet_edge2);
        """,

        // Tubo dobrado: trecho reto + cotovelo (SweepFeature varre a seção ao longo de UM arco).
        // bendDirection aponta do início do arco para o centro dele e define o sentido da dobra;
        // o próximo trecho começa em curva_b2.EndCenter, na direção curva_b2.EndTangent.
        """
        var tubo_reto_a1 = new CylinderFeature(globalBaseCenter: new Point3D(0, 0, 0), globalDirection: new Vector3D(1, 0, 0), radius: 5, height: 50) { Name = "tubo_reto_a1" };
        AddFeature(tubo_reto_a1);
        var curva_b2 = new SweepFeature(start: new Point3D(50, 0, 0), startTangent: new Vector3D(1, 0, 0), sectionRadius: 5, bendDirection: new Vector3D(0, 1, 0), bendRadius: 30, angleDegrees: 90) { Name = "curva_b2" };
        AddFeature(curva_b2);
        var tubo_c3 = BooleanFeature.CreateUnion("tubo_c3", tubo_reto_a1, curva_b2);
        AddFeature(tubo_c3);
        """,

        // Tubo dobrado por uma POLILINHA: um sólido só, com os cantos arredondados pelo raio dado.
        // Prefira isto a emendar cilindros e cotovelos quando o caminho tiver mais de uma dobra.
        """
        var caminho_d1 = SweepPath.FromPolyline(new[] { new Point3D(0, 0, 0), new Point3D(120, 0, 0), new Point3D(120, 90, 0) }, cornerRadius: 25);
        var tubo_d2 = new SweepFeature(SweepSection.Circle(6), caminho_d1) { Name = "tubo_d2" };
        AddFeature(tubo_d2);
        """,

        // Prisma extrudado a partir de um polígono (perfil em L) no plano padrão
        """
        var prism_L_verts = new[] { new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(20, 10, 0), new Point3D(10, 10, 0), new Point3D(10, 20, 0), new Point3D(0, 20, 0) };
        var prism_L = new PrismFeature(prism_L_verts, height: 15) { Name = "prism_L" };
        AddFeature(prism_L);
        """
    };

    /// <summary>
    /// Exemplo de arquivo de ENTRADA que usa uma peça já declarada em Pecas/ (não a redeclara).
    /// Ao contrário de <see cref="Examples"/>, não compila sozinho (Placa não está declarada aqui) —
    /// é mostrado ao lado do primeiro <see cref="PartExamples"/> (que declara Placa) e testado junto
    /// dele, como os dois arquivos de um projeto de verdade.
    /// </summary>
    public const string SceneUsingPartExample = CadScriptRules.SceneUsingPartExample;

    /// <summary>
    /// Exemplos few-shot de ARQUIVO DE PEÇA (Pecas/*.cs): um único record que herda de Part.
    /// Vêm de <see cref="CadScriptRules"/>, que também os entrega ao <c>.cad/api.md</c> da cena.
    /// Cobertos por teste que compõe cada exemplo com um arquivo de entrada sintético
    /// (<c>Add(new Peca(...))</c>) e confirma que compila e executa no <c>RoslynSceneEvaluator</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> PartExamples = new[]
    {
        CadScriptRules.PartExamplePlaca,
        CadScriptRules.PartExampleChapaFurada,
        CadScriptRules.PartExampleBlocoQuatroFuros
    };

    public string GetSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Você é um assistente especializado em um editor CAD paramétrico 3D construído em C#.");
        sb.AppendLine("Sua tarefa é gerar/editar o código C# de UM ARQUIVO por vez da cena 3D. Uma cena pode ser:");
        sb.AppendLine("- um SCRIPT ÚNICO: statements top-level que descrevem toda a geometria (formato \"script\");");
        sb.AppendLine("- um PROJETO com vários arquivos: um arquivo de entrada (ex.: Cena.cs, também formato \"script\") e,");
        sb.AppendLine("  em Pecas/, arquivos de PEÇA que declaram tipos reutilizáveis (formato \"peça\").");
        sb.AppendLine("Cada pedido traz OU uma seção \"### ARQUIVO ATIVO\" (um só arquivo em edição) OU uma seção");
        sb.AppendLine("\"### ARQUIVOS NO CONTEXTO\" (vários arquivos do projeto, cada um com seu caminho e formato). Leia essas");
        sb.AppendLine("seções a cada turno — os arquivos e formatos podem mudar de um turno para o outro — e escreva cada");
        sb.AppendLine("arquivo EXATAMENTE no formato indicado para ele.");
        sb.AppendLine();
        sb.AppendLine("FORMATO \"script\" (arquivo único, ou o arquivo de entrada de um projeto):");
        sb.AppendLine(CadScriptRules.SceneFile);
        sb.AppendLine("- Ao pedirem uma peça nova (ex.: \"crie um motor X\"):");
        sb.AppendLine("  - com \"### ARQUIVOS NO CONTEXTO\" (projeto): crie-a num arquivo novo (bloco file=Pecas/NomeDaPeca.cs) e");
        sb.AppendLine("    instancie-a aqui com Add(...);");
        sb.AppendLine("  - com \"### ARQUIVO ATIVO\" (arquivo único, SEM projeto: não existe Pecas/ nem Add(...)): as regras sobre");
        sb.AppendLine("    Pecas/ não se aplicam. Construa a geometria direto neste arquivo, com features (BoxFeature, CylinderFeature,");
        sb.AppendLine("    ConeFeature, BooleanFeature...) registradas com AddFeature, sem declarar record/classe. Nunca recuse nem");
        sb.AppendLine("    peça para criar outro arquivo.");
        sb.AppendLine();
        sb.AppendLine("FORMATO \"peça\" (arquivo em Pecas/):");
        sb.AppendLine(CadScriptRules.PartFile);
        sb.AppendLine();
        sb.AppendLine("USANDO UMA PEÇA NO ARQUIVO DE ENTRADA:");
        sb.AppendLine(CadScriptRules.PartUsage);
        sb.AppendLine();
        sb.AppendLine("REGRAS COMUNS AOS DOIS FORMATOS:");
        sb.AppendLine(CadScriptRules.Common);
        sb.AppendLine(CadScriptRules.TrackableSolids);
        sb.AppendLine("- Prefira reaproveitar/editar o ARQUIVO ATUAL em vez de recomeçar do zero, a menos que o pedido peça algo totalmente novo.");
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (extraída automaticamente do código-fonte atual do projeto — sempre confie nela, não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"script\":");
        foreach (var example in Examples)
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Trim());
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"peça\":");
        foreach (var example in PartExamples)
        {
            sb.AppendLine("```cadscript");
            sb.AppendLine(example.Trim());
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("EXEMPLO — arquivo de entrada usando a peça Placa acima (não redeclare o tipo):");
        sb.AppendLine("```cadscript");
        sb.AppendLine(SceneUsingPartExample.Trim());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("FORMATO OBRIGATÓRIO DA RESPOSTA:");
        sb.AppendLine("1. Um parágrafo curto (1 a 3 frases), em português, explicando o que foi feito ou respondendo à pergunta.");
        sb.AppendLine("2. Com \"### ARQUIVO ATIVO\": se — e somente se — o pedido exigir alterar o arquivo, inclua logo em seguida");
        sb.AppendLine("   UM ÚNICO bloco de código delimitado por ```cadscript e ``` contendo o ARQUIVO COMPLETO resultante (nunca");
        sb.AppendLine("   um trecho, nunca um diff: sempre o arquivo inteiro, pronto para substituí-lo e ser executado).");
        sb.AppendLine("3. Com \"### ARQUIVOS NO CONTEXTO\": inclua UM bloco por arquivo ALTERADO (ou criado), cada um aberto com");
        sb.AppendLine("   ```cadscript file=<caminho exato do arquivo> e contendo o ARQUIVO COMPLETO resultante. NÃO repita arquivos");
        sb.AppendLine("   que não mudaram. Só altere arquivos listados no contexto; o único arquivo novo permitido é uma peça em");
        sb.AppendLine("   Pecas/. Exemplo de resposta que altera dois arquivos:");
        sb.AppendLine("   ```cadscript file=Pecas/Placa.cs");
        sb.AppendLine("   public sealed record Placa(double Largura = 120, double Comprimento = 60, double Espessura = 5) : Part { ... }");
        sb.AppendLine("   ```");
        sb.AppendLine("   ```cadscript file=Cena.cs");
        sb.AppendLine("   var placa_1 = Add(new Placa(), at: Placement.At(0, 0, 0));");
        sb.AppendLine("   ```");
        sb.AppendLine("4. Se o pedido for apenas uma pergunta, ou pedir algo que não cabe nos formatos indicados,");
        sb.AppendLine("   responda somente com o texto, sem bloco de código.");

        return sb.ToString();
    }

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
