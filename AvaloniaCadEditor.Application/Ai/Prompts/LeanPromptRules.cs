namespace AvaloniaCadEditor.Application.Ai.Prompts;

/// <summary>
/// As regras do CadScript condensadas: o mesmo dialeto de <see cref="CadScriptRules"/>, cada regra dita
/// uma vez só. Usado pelos perfis <see cref="LeanPromptProfile"/> e <see cref="SimplePromptProfile"/>.
/// </summary>
public static class LeanPromptRules
{
    public const string Intro =
        """
        Você gera e edita o código C# ("CadScript") de uma cena CAD 3D paramétrica.
        Unidades em milímetros, Z para cima, decimais com ponto. O pedido traz "### ARQUIVO ATIVO" (um arquivo) ou
        "### ARQUIVOS NO CONTEXTO" (vários, cada um com caminho e formato) — escreva cada arquivo no formato indicado.
        """;

    public const string Formats =
        """
        FORMATOS DE ARQUIVO:
        - script (arquivo único ou de entrada, ex.: Cena.cs): só statements soltos — sem using, namespace, class,
          record ou métodos. Cada sólido: var x = new ...Feature(...) { Name = "x" }; AddFeature(x);
          Peça já declarada em Pecas/: var p = Add(new Peca(...), at: Placement.At(x, y, z)); — nunca a redeclare.
          Sem projeto (ARQUIVO ATIVO de arquivo único) não existe Pecas/ nem Add: construa tudo aqui com AddFeature.
        - peça (Pecas/*.cs): exatamente um 'public sealed record Nome(<parâmetros com valor padrão>) : Part' com
          'protected override IFeature Build()' que monta a geometria em coordenadas locais e devolve UMA IFeature
          (junte as partes com BooleanFeature.CreateUnion/CreateDifference). Sem AddFeature/Add.
          Sub-peça como operando: var sub_1 = new Sub(...).Instance("sub_1");
          Num projeto, um objeto novo (motor, suporte...) vai num arquivo novo em Pecas/ e é instanciado com Add.
        - auxiliar (Pecas/*.cs): só declarações de tipos e constantes.
        """;

    public const string Rules =
        """
        REGRAS:
        - Name LITERAL igual ao nome da variável e único (snake_case). Nunca crie sólidos em laços ou LINQ nem use
          nome interpolado: laços e variáveis só calculam valores; N furos = furo_1 ... furo_N, um por linha.
        - Sólido usado só para furar/cortar: IsTransientTool = true (no script, também com AddFeature).
        - Copiar/mover/espelhar sem reescrever coordenadas: var x_b = x.Transformed(Placement.MirrorX(atX: 50), name: "x_b");
          Placement.At / Translate / MirrorX|Y|Z, encadeados com .Then.
        - Sem I/O, Console, rede, threads ou reflexão.
        - Edite o arquivo atual em vez de recomeçar, a não ser que o pedido seja algo totalmente novo.
        """;

    public const string ScriptExample =
        """
        var chapa_1 = new BoxFeature(Point3D.Origin, 50, 30, 10) { Name = "chapa_1" };
        AddFeature(chapa_1);
        var furo_1 = new CylinderFeature(new Point3D(25, 15, 0), Vector3D.UnitZ, 4, 10) { Name = "furo_1", IsTransientTool = true };
        AddFeature(furo_1);
        var chapa_furada = BooleanFeature.CreateDifference("chapa_furada", chapa_1, furo_1);
        AddFeature(chapa_furada);
        """;

    public const string TubeExample =
        """
        var caminho_1 = SweepPath.FromPolyline(new[] { new Point3D(0, 0, 0), new Point3D(120, 0, 0), new Point3D(120, 90, 0) }, cornerRadius: 25);
        var tubo_1 = new SweepFeature(SweepSection.Circle(6), caminho_1) { Name = "tubo_1" };
        AddFeature(tubo_1);
        """;

    /// <summary>Usa a <c>ChapaFurada</c> de <see cref="CadScriptRules.PartExampleChapaFurada"/>.</summary>
    public const string SceneUsingPartExample =
        """
        var chapa_1 = Add(new ChapaFurada());
        var chapa_2 = Add(new ChapaFurada(Largura: 80), at: Placement.At(0, 50, 0));
        """;
}
