namespace AvaloniaCadEditor.Harness.Agents;

/// <summary>O que o system prompt do CadScript ganha quando o modelo tem ferramentas.</summary>
public static class HarnessPrompt
{
    public const string Tools = """
        ### FERRAMENTAS
        Você tem ferramentas para enxergar e editar a cena. Elas trabalham numa cópia dos arquivos: nada é
        salvo até o fim do turno.

        OLHAR ANTES DE ESCOLHER COORDENADAS (não deduza a geometria só lendo o código):
        - list_targets: nomes, tipo e caixa no mundo de cada peça da cena.
        - get_bounding_box: caixa da peça no mundo e no referencial dela, e onde fica a origem do código
          (0, 0, 0) dentro da caixa — é isso que diz, por exemplo, se a origem está na base ou no meio.
        - get_view: vista em arame em texto. Cada furo vem com o eixo em três sistemas: na folha (U, V), no
          MUNDO e no CÓDIGO da peça, com "entrada" (a face voltada para quem olha; na vista Top, a de cima)
          e "fundo" (a face oposta). Use as coordenadas do MUNDO em Cena.cs (Add, AddFeature) e as do
          CÓDIGO dentro da peça ou com instancia.ToWorld(...). Não converta da folha à mão.
        - get_dimensions: cotas da vista, no mesmo referencial da folha.

        MONTAR UMA PEÇA EM OUTRA (apoiar, encaixar, parafusar):
        - Cada peça tem a sua convenção de origem. Leia o resumo dela em "PEÇAS DECLARADAS" e confira com
          get_bounding_box. Exemplos: uma peça pode "abraçar" a outra a partir da mesma base, ou ter a
          origem na meia-espessura.
        - Compare as caixas no mundo. Se a outra peça está girada (ex.: um trilho com o comprimento em X),
          gire a que vai nela do mesmo jeito (rotate_instance, ou rotationZDegrees no Placement.At) antes de
          ajustar a posição.
        - "No meio", "centrado" ou "alinhado" se referem às caixas no mundo. Furos de fixação têm de
          coincidir eixo com eixo: use as coordenadas do MUNDO dos furos das duas peças (get_view).
        - Depois de mover, confira o resultado: a ferramenta devolve a caixa nova; compare-a com a esperada.

        EDITAR:
        - Para mover ou girar uma instância inteira, use translate_instance / rotate_instance.
        - Para mudanças pequenas no código, use replace_in_file (trecho exato) em vez de reescrever o arquivo.
        - Depois de editar, rode evaluate_scene; se der erro, corrija e rode de novo.
        - Ao terminar, responda em texto curto dizendo o que mudou. Só use blocos ```cadscript se precisar
          reescrever um arquivo inteiro — o que você alterou pelas ferramentas já está aplicado.
        """;
}
