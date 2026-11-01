namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// Orientação de estilo do perfil <see cref="EditablePromptProfile"/>: código que as ferramentas de edição
/// manual conseguem reescrever (números nas coordenadas, poucos parâmetros, sem laços nem funções), com os
/// recursos avançados liberados só quando o pedido os pede. Plano: docs/planos/2026-11-01-codigo-editavel-por-padrao.md.
/// Os exemplos seguem o estilo à risca: um teste exige que nenhum gere aviso do <see cref="CadScriptEditabilityLint"/>.
/// </summary>
public static class EditablePromptRules
{
    public const string Style =
        """
        ESTILO PADRÃO — CÓDIGO EDITÁVEL À MÃO:
        O usuário ajusta o modelo pela interface (arrastar, inspector, duplo-clique no furo), e essas ferramentas só
        conseguem reescrever NÚMEROS. Por isso, a menos que o pedido peça outra coisa:
        - Coordenadas como números: new Point3D(10, 10, 0). Quando a posição depende de uma dimensão da peça, use
          Parametro ± número: new Point3D(Largura - 10, 10, 0). Nada de variáveis intermediárias para posição
          (x1, passo, margem) nem contas como Largura / 4 ou margem + i * passo.
        - No máximo 3 parâmetros por peça, só as dimensões principais que alguém realmente mudaria. As outras
          medidas vão como números no Build().
        - Sem laços (for, foreach, while), LINQ, funções locais, métodos auxiliares, [Range], parâmetros bool de
          opção ou static class de medidas. N furos iguais = N declarações, uma por linha, cada uma com os seus números.
        - Na cena: Placement.At com números. Crie uma variável só para um valor usado por mais de uma instância.
        - Peça de catálogo (trilho, motor, perfil): as medidas do catálogo como números; parâmetro só para o que varia
          de verdade (ex.: Comprimento).

        QUANDO LIBERAR OS RECURSOS AVANÇADOS:
        - Só se o pedido disser explicitamente: "paramétrico", "com parâmetro para ...", "repita N vezes", "padrão de
          furos a cada X", "deixe ajustável" ou equivalente. Nesse caso use variáveis, laços e funções à vontade, mas
          SÓ para calcular valores: cada sólido continua com declaração própria e Name literal.
        - Ao editar código que já usa esses recursos, mantenha o estilo do arquivo; não reescreva o que não foi pedido.
        """;

    public const string ScriptExample =
        """
        var chapa_1 = new BoxFeature(Point3D.Origin, 80, 50, 6) { Name = "chapa_1" };
        AddFeature(chapa_1);
        var furo_1 = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_1", IsTransientTool = true };
        AddFeature(furo_1);
        var furo_2 = new CylinderFeature(new Point3D(70, 10, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_2", IsTransientTool = true };
        AddFeature(furo_2);
        var furo_3 = new CylinderFeature(new Point3D(40, 40, 0), Vector3D.UnitZ, 2.5, 6) { Name = "furo_3", IsTransientTool = true };
        AddFeature(furo_3);
        var chapa_furada = BooleanFeature.CreateDifference("chapa_furada", chapa_1, furo_1, furo_2, furo_3);
        AddFeature(chapa_furada);
        """;

    public const string PartExample =
        """
        public sealed record Chapa(double Largura = 80, double Comprimento = 50, double Espessura = 6) : Part
        {
            protected override IFeature Build()
            {
                var corpo = new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "corpo" };
                var furo_1 = new CylinderFeature(new Point3D(10, 10, 0), Vector3D.UnitZ, 2.5, Espessura) { Name = "furo_1", IsTransientTool = true };
                var furo_2 = new CylinderFeature(new Point3D(Largura - 10, 10, 0), Vector3D.UnitZ, 2.5, Espessura) { Name = "furo_2", IsTransientTool = true };
                var furo_3 = new CylinderFeature(new Point3D(10, Comprimento - 10, 0), Vector3D.UnitZ, 2.5, Espessura) { Name = "furo_3", IsTransientTool = true };
                var furo_4 = new CylinderFeature(new Point3D(Largura - 10, Comprimento - 10, 0), Vector3D.UnitZ, 2.5, Espessura) { Name = "furo_4", IsTransientTool = true };
                return BooleanFeature.CreateDifference("chapa", corpo, furo_1, furo_2, furo_3, furo_4);
            }
        }
        """;

    public const string SceneUsingPartExample =
        """
        var chapa_1 = Add(new Chapa());
        var chapa_2 = Add(new Chapa(Largura: 120), at: Placement.At(0, 70, 0));
        """;
}
