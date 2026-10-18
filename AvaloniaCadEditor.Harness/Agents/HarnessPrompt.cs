namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>O que o system prompt do CadScript ganha quando o modelo tem ferramentas.</summary>
public static class HarnessPrompt
{
    public const string Tools = """
        ### FERRAMENTAS
        Você tem ferramentas para enxergar e editar a cena. Elas trabalham numa cópia dos arquivos: nada é
        salvo até o fim do turno.

        - Antes de escolher coordenadas, olhe a geometria em vez de deduzi-la do código:
          list_targets (nomes e caixas), get_bounding_box, get_view (vista em arame em texto, com furos
          nomeados) e get_dimensions (cotas). As coordenadas de get_view e get_dimensions são as da folha;
          o cabeçalho diz que eixo é U e V e onde fica o (0, 0, 0) do código: coordenada no código =
          folha − origem.
        - Para mover ou girar uma instância inteira, use translate_instance / rotate_instance.
        - Para mudanças pequenas no código, use replace_in_file (trecho exato) em vez de reescrever o arquivo.
        - Depois de editar, rode evaluate_scene; se der erro, corrija e rode de novo.
        - Ao terminar, responda em texto curto dizendo o que mudou. Só use blocos ```cadscript se precisar
          reescrever um arquivo inteiro — o que você alterou pelas ferramentas já está aplicado.
        """;
}
