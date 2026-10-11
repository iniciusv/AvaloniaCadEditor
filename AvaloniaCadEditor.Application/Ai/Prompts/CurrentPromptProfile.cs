using System.Collections.Generic;
using System.Text;

namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// O pré-prompt de referência (o padrão do app): regras completas de <see cref="CadScriptRules"/>, a API
/// inteira e todos os exemplos. Coberto por um teste golden — qualquer mudança no texto é intencional.
/// Novas estratégias de prompt devem virar um perfil novo, não uma edição deste.
/// </summary>
public sealed class CurrentPromptProfile : ICadScriptPromptProfile
{
    public const string ProfileId = "atual";

    public string Id => ProfileId;
    public string DisplayName => "Atual (completo)";
    public string Description => "Regras completas, API inteira e todos os exemplos — o prompt de referência.";

    public IReadOnlyList<CadScriptPromptExample> Examples { get; } = new CadScriptPromptExample[]
    {
        // Caixa simples
        new(CadScriptExampleKind.Script,
            """
            var box_a1b2 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_a1b2" };
            AddFeature(box_a1b2);
            """),

        // Furo cilíndrico numa caixa (diferença booleana)
        new(CadScriptExampleKind.Script,
            """
            var box_1234 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 50, length: 30, height: 10) { Name = "box_1234" };
            AddFeature(box_1234);
            var hole_5678 = new CylinderFeature(globalBaseCenter: new Point3D(25, 15, 0), globalDirection: new Vector3D(0, 0, 1), radius: 4, height: 10) { Name = "hole_5678", IsTransientTool = true };
            AddFeature(hole_5678);
            var boolOp_9abc = BooleanFeature.CreateDifference("boolOp_9abc", box_1234, hole_5678);
            AddFeature(boolOp_9abc);
            """),

        // Furo escareado: cone truncado subtraído a partir da face de cima (topRadius menor = fundo do furo).
        // ConeFeature cobre cone completo (topRadius: 0), tronco de cone e chanfro cônico de furo.
        new(CadScriptExampleKind.Script,
            """
            var chapa_a1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 40, length: 40, height: 10) { Name = "chapa_a1" };
            AddFeature(chapa_a1);
            var escareado_b2 = new ConeFeature(globalBaseCenter: new Point3D(20, 20, 10), globalDirection: new Vector3D(0, 0, -1), bottomRadius: 6, topRadius: 3, height: 4) { Name = "escareado_b2", IsTransientTool = true };
            AddFeature(escareado_b2);
            var boolOp_c3 = BooleanFeature.CreateDifference("boolOp_c3", chapa_a1, escareado_b2);
            AddFeature(boolOp_c3);
            """),

        // Fillet (arredondamento) em todas as arestas de uma caixa
        new(CadScriptExampleKind.Script,
            """
            var box_edge1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 40, length: 40, height: 20) { Name = "box_edge1" };
            AddFeature(box_edge1);
            var fillet_edge2 = new FilletFeature(name: "fillet_edge2", baseFeature: box_edge1, radius: 3) { Mode = FilletMode.Round, ApplyToAllEdges = true };
            AddFeature(fillet_edge2);
            """),

        // Tubo dobrado: trecho reto + cotovelo (SweepFeature varre a seção ao longo de UM arco).
        // bendDirection aponta do início do arco para o centro dele e define o sentido da dobra;
        // o próximo trecho começa em curva_b2.EndCenter, na direção curva_b2.EndTangent.
        new(CadScriptExampleKind.Script,
            """
            var tubo_reto_a1 = new CylinderFeature(globalBaseCenter: new Point3D(0, 0, 0), globalDirection: new Vector3D(1, 0, 0), radius: 5, height: 50) { Name = "tubo_reto_a1" };
            AddFeature(tubo_reto_a1);
            var curva_b2 = new SweepFeature(start: new Point3D(50, 0, 0), startTangent: new Vector3D(1, 0, 0), sectionRadius: 5, bendDirection: new Vector3D(0, 1, 0), bendRadius: 30, angleDegrees: 90) { Name = "curva_b2" };
            AddFeature(curva_b2);
            var tubo_c3 = BooleanFeature.CreateUnion("tubo_c3", tubo_reto_a1, curva_b2);
            AddFeature(tubo_c3);
            """),

        // Tubo dobrado por uma POLILINHA: um sólido só, com os cantos arredondados pelo raio dado.
        // Prefira isto a emendar cilindros e cotovelos quando o caminho tiver mais de uma dobra.
        new(CadScriptExampleKind.Script,
            """
            var caminho_d1 = SweepPath.FromPolyline(new[] { new Point3D(0, 0, 0), new Point3D(120, 0, 0), new Point3D(120, 90, 0) }, cornerRadius: 25);
            var tubo_d2 = new SweepFeature(SweepSection.Circle(6), caminho_d1) { Name = "tubo_d2" };
            AddFeature(tubo_d2);
            """),

        // Anel / argola: uma VOLTA INTEIRA como caminho (SweepPath.FullCircle). Não tem canto, logo
        // não tem raio de canto, e a volta fecha sozinha — o sólido sai sem tampas. A seção pode ser
        // qualquer contorno; aqui um retângulo, para um anel de seção quadrada.
        new(CadScriptExampleKind.Script,
            """
            var caminho_e1 = SweepPath.FullCircle(center: new Point3D(0, 0, 0), axis: new Vector3D(0, 0, 1), radius: 40);
            var anel_e2 = new SweepFeature(SweepSection.Rectangle(6, 6), caminho_e1) { Name = "anel_e2" };
            AddFeature(anel_e2);
            """),

        // Prisma extrudado a partir de um polígono (perfil em L) no plano padrão
        new(CadScriptExampleKind.Script,
            """
            var prism_L_verts = new[] { new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(20, 10, 0), new Point3D(10, 10, 0), new Point3D(10, 20, 0), new Point3D(0, 20, 0) };
            var prism_L = new PrismFeature(prism_L_verts, height: 15) { Name = "prism_L" };
            AddFeature(prism_L);
            """),

        // Peças: vêm de CadScriptRules, que também as entrega ao .cad/api.md da cena.
        new(CadScriptExampleKind.Part, CadScriptRules.PartExamplePlaca),
        new(CadScriptExampleKind.Part, CadScriptRules.PartExampleChapaFurada),
        new(CadScriptExampleKind.Part, CadScriptRules.PartExampleBlocoQuatroFuros),

        // Arquivo de entrada que usa a Placa acima (não a redeclara).
        new(CadScriptExampleKind.SceneUsingPart, CadScriptRules.SceneUsingPartExample)
    };

    public string BuildSystemPrompt()
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
        sb.AppendLine("MOVER, GIRAR, COPIAR E ESPELHAR UM SÓLIDO JÁ DECLARADO:");
        sb.AppendLine(CadScriptRules.Transforms);
        sb.AppendLine();
        sb.AppendLine("REGRAS COMUNS AOS DOIS FORMATOS:");
        sb.AppendLine(CadScriptRules.Common);
        sb.AppendLine(CadScriptRules.TrackableSolids);
        sb.AppendLine("- Prefira reaproveitar/editar o ARQUIVO ATUAL em vez de recomeçar do zero, a menos que o pedido peça algo totalmente novo.");
        sb.AppendLine();
        sb.AppendLine("API DISPONÍVEL (extraída automaticamente do código-fonte atual do projeto — sempre confie nela, não invente membros):");
        sb.AppendLine(CadScriptApiReference.Build());
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"script\":");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Script);
        sb.AppendLine();
        sb.AppendLine("EXEMPLOS VÁLIDOS — formato \"peça\":");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.Part);
        sb.AppendLine();
        sb.AppendLine("EXEMPLO — arquivo de entrada usando a peça Placa acima (não redeclare o tipo):");
        CadScriptResponseFormat.AppendExamples(sb, Examples, CadScriptExampleKind.SceneUsingPart);
        sb.AppendLine();
        sb.AppendLine(CadScriptResponseFormat.Text);

        return sb.ToString();
    }
}
