namespace AvaloniaCadEditor.Harness.Skills;

/// <summary>
/// As skills disponíveis, por id. Ligar uma skill (<c>harness_settings.json</c>) põe as instruções no
/// system prompt de toda a conversa; escrever <c>/id</c> no começo do pedido a usa só naquele turno.
/// </summary>
public sealed class SkillRegistry
{
    public const string Questionar = "questionar";
    public const string Revisar = "revisar";

    private readonly List<HarnessSkill> _skills;

    public SkillRegistry(IEnumerable<HarnessSkill> skills)
    {
        _skills = skills.ToList();
        var duplicated = _skills.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicated != null) throw new ArgumentException($"Skill repetida: {duplicated.Key}.", nameof(skills));
    }

    /// <summary>As skills que vêm com o app.</summary>
    public static SkillRegistry Default() => new(new[]
    {
        new HarnessSkill(Questionar, "Questionar antes de editar",
            "Antes de mexer na cena, a IA confere o pedido contra a geometria e, se faltar decidir algo, responde só com perguntas (cada uma com uma sugestão).",
            QuestionarInstructions),
        new HarnessSkill(Revisar, "Conferir o resultado",
            "Depois de editar, a IA compara as caixas e os eixos das peças novas ou movidas com o pedido e mostra a conferência na resposta.",
            RevisarInstructions)
    });

    public IReadOnlyList<HarnessSkill> All => _skills;

    public HarnessSkill? Find(string id) => _skills.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>As skills destes ids, na ordem do registro. Id desconhecido (skill removida) é ignorado.</summary>
    public IReadOnlyList<HarnessSkill> Resolve(IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return _skills.Where(s => wanted.Contains(s.Id)).ToList();
    }

    /// <summary>
    /// Tira do começo do pedido os <c>/id</c> de skills conhecidas ("/questionar /revisar põe um motor...").
    /// Para no primeiro termo que não é uma skill, então um "/" no meio do texto continua texto.
    /// </summary>
    public (IReadOnlyList<HarnessSkill> Skills, string Instruction) ParseCommands(string instruction)
    {
        var skills = new List<HarnessSkill>();
        string rest = (instruction ?? string.Empty).TrimStart();
        while (rest.StartsWith('/'))
        {
            int end = rest.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            string token = end < 0 ? rest[1..] : rest[1..end];
            var skill = Find(token);
            if (skill == null) break;
            if (!skills.Contains(skill)) skills.Add(skill);
            rest = end < 0 ? string.Empty : rest[end..].TrimStart();
        }
        return (skills, rest);
    }

    /// <summary>O bloco das skills ligadas, para o fim do system prompt (vazio sem skills).</summary>
    public static string SystemPromptSection(IReadOnlyCollection<HarnessSkill> skills) =>
        skills.Count == 0 ? string.Empty
            : "### SKILLS LIGADAS\nSiga estas instruções em todos os pedidos.\n\n" + string.Join("\n\n", skills.Select(s => s.Section()));

    /// <summary>O bloco das skills pedidas com <c>/id</c>, que vai junto com um pedido só.</summary>
    public static string TurnSection(IReadOnlyCollection<HarnessSkill> skills) =>
        skills.Count == 0 ? string.Empty
            : "### SKILLS DESTE PEDIDO\nSiga estas instruções neste pedido.\n\n" + string.Join("\n\n", skills.Select(s => s.Section()));

    public const string QuestionarInstructions = """
        Antes de alterar qualquer arquivo, descubra se o pedido deixa decisões em aberto.

        1. Levante os fatos com poucas consultas. list_targets já traz a caixa no mundo de todas as peças;
           use get_bounding_box para saber onde fica a origem do código e get_view só quando precisar de
           furos ou faces. Não abra várias vistas de uma peça reta.
        2. Separe o que é fixo do que se move: leia os comentários da cena e veja que peça está presa a qual.
           Uma peça que se move junto com outra não serve de apoio para empurrá-la.
        3. Para cada referência do pedido ("o chão", "a parte mais baixa", "no meio", "a porca no tubo"),
           ache a peça e a cota a que ela se refere. Se mais de uma peça ou cota servir, isso é uma pergunta.

        Se houver decisões que mudam o resultado e que a cena não responde, NÃO altere nada (nem com as
        ferramentas de edição) e responda só com:
        - **O que vi:** os fatos em que as perguntas se apoiam, com o nome da peça e a cota no mundo (min…max).
        - **Perguntas:** numeradas, no máximo 6, uma decisão por pergunta, da mais importante para a menos
          importante. Cada uma com a sua sugestão de resposta e o motivo. As sugestões precisam ser
          coerentes entre si: juntas, têm de formar uma montagem possível.

        Não pergunte o que o pedido, o histórico da conversa ou a cena já respondem, nem detalhes que dá
        para ajustar depois (um raio, uma folga): assuma um valor e diga qual. Se não sobrar nenhuma
        decisão em aberto, faça a alteração normalmente e liste as suposições no fim da resposta.
        """;

    public const string RevisarInstructions = """
        Depois de editar e antes da resposta final, confira o resultado contra o pedido:

        1. Rode evaluate_scene e list_targets e compare as caixas no mundo das peças novas ou movidas com as
           das vizinhas: encostam onde deviam encostar, não se atravessam e ficam onde o pedido disse
           ("em cima", "centrado", "na altura de").
        2. Em encaixes (furo com furo, eixo com porca), confira os eixos com get_view nas duas peças.
        3. Se algo não bate, corrija e confira de novo.

        Na resposta final, inclua uma seção **Conferência** com cada exigência do pedido e a cota que mostra
        que ela foi cumprida, ou o que ficou faltando.
        """;
}
