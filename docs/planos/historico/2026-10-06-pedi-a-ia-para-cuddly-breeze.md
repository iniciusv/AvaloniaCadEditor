# Conexão em Y para mangueira 5 mm — transição suave por varredura

## Context

A peça que a IA gerou não é um Y: é um **T**. Ela monta o corpo com quatro cilindros axiais — haste em
−Z, dois ramais em +Z e um coletor horizontal em X — unidos a 90°, deixando dois cantos vivos côncavos
na junção.

A primeira versão deste plano trocou isso por duas varreduras (`SweepFeature`), e ao rodar
**o ramal direito não apareceu**, sem nenhuma mensagem na barra de status. A causa foi encontrada e o
código abaixo já está corrigido — o diagnóstico está na seção seguinte, porque a lição vale para
qualquer peça futura que una tubos varridos.

Decisões: ângulo de abertura **45°**, os dois tubos superiores **quase se tocando** (`Deslocamento` 5 mm
= centros a 10 mm, paredes a 0,5 mm), **com** reforço no vão do Y, e a peça entra em `AiCore/Cena.cs`.

```
      ANTES (T)                      DEPOIS (Y)
   ||        ||                   ||||
   ||        ||                   ||||  <- 0,5 mm entre as paredes
   ++========++  <- coletor         ||||
        ||       cantos vivos         \\//   arco R8, tangente
        ||                             \\/
        ||                              ||  <- alma (reforço e
        ||                              ||     web entre os canais)
```

---

## Por que o ramal direito desaparecia

Duas coisas se somaram:

**1. O OCCT não resolve a fusão de dois tubos tangentes.** Os dois ramais se tocam
tangencialmente na virilha: enquanto se afastam, existe um z onde as duas superfícies externas são
tangentes (com os padrões, z ≈ 6,3) — abaixo dele se cruzam
transversalmente, acima estão separadas. Contato tangencial ao longo de uma curva é o caso clássico
que faz o BOPAlgo falhar.

**2. O kernel descarta o operando perdedor em silêncio.** Em
[OpenCascadeGeometryKernel.cs:670-698](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L670-L698),
o próprio comentário do `FuseBalanced` assume: *"Um par que falhe a fusão (raro) é descartado
silenciosamente, do jeito que o código sequencial anterior já tolerava."*

```csharp
var fused = a.Fuse(b);
if (fused == null || fused.IsNull)
{
    if (owned.Remove(b)) b.Dispose();
    next.Add(a);          // mantém A e JOGA B FORA, sem ReportProblem
    continue;
}
```

E o emparelhamento explica a assimetria. `CreateUnion("corpo_y", tronco, ramo_esq, ramo_dir, alma_juncao)`
chega ao kernel como a lista `[tronco, ramo_esq, ramo_dir, alma_juncao]`
([OpenCascadeGeometryKernel.cs:626-631](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L626-L631)),
que o `FuseBalanced` funde em árvore:

```
nível 1:  (tronco ∪ ramo_esq) = A        (ramo_dir ∪ alma_juncao) = B
nível 2:  A ∪ B   <-- falha aqui, e B inteiro é descartado
```

Sobram tronco + ramal esquerdo. A `alma_juncao`, cujo trabalho é justamente **enterrar o ponto de
tangência dentro de um sólido**, estava do lado errado da árvore: entrava junto com `ramo_dir`, não
antes dele.

### As três correções no código

1. **Ordem de fusão explícita**: três uniões encadeadas, cada uma com **um** operando-ferramenta, de
   modo que o kernel sempre receba uma lista de dois shapes e faça um `Fuse` determinístico. A alma
   entra **primeiro**, antes de qualquer ramal; cada ramal depois é fundido contra um sólido que já
   contém o ponto de tangência.
2. **Alma redimensionada para cumprir esse papel**: base de raio `RaioAlma` (3,5 — menor que
   `RaioExterno`, então ela nasce estritamente *dentro* do tronco e atravessa a tampa dele de forma
   transversal, sem criar uma segunda tangência), e altura **derivada** de
   `recuoCanto + zFimCurva + Sobreposicao`, para o ápice cair 1 mm depois do fim das curvas — a partir
   dali os dois ramais sobem paralelos e separados, então não existe mais contato para enterrar. É por
   isso que `AlturaAlma` deixou de ser parâmetro: era um número que *tinha* que satisfazer uma
   condição geométrica, e errá-lo traz o bug de volta sem aviso.
3. **Os dois furos varridos são subtraídos em etapas separadas.** Eles sofrem da mesma tangência
   (mais abaixo, onde os eixos dos dois canais ainda estão a menos de `2·RaioFuroJuncao`) e, no
   `Subtract`, as ferramentas são **unidas entre si**
   antes do corte ([linha 636](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L636)) —
   o mesmo `FuseBalanced`, a mesma armadilha. Dois `CreateDifference` encadeados nunca põem os dois
   na mesma fusão.

Efeito colateral bom: a alma agora é exatamente a **parede entre os dois canais**. Ela sobrevive ao
corte só acima da altura em que os dois canais se separam — abaixo disso está dentro do furo e é
removida — e é justamente ali que os canais
deixam de ser uma cavidade única. Sem ela, a parede entre os canais seria uma lâmina de espessura zero
no ponto de tangência.

---

## Arquivos

| Arquivo | Ação |
|---|---|
| `AiCore/Pecas/ConexaoYAdaptador5mm.cs` | **criar/substituir** — código completo abaixo |
| [AiCore/Cena.cs](AiCore/Cena.cs) | acrescentar uma linha `Add(...)` |

`AiCore/.cad/api.md` é gerado automaticamente (`SceneProjectTemplate.RefreshGeneratedFiles`) — não editar.

## Cotas derivadas (valores padrão)

| Variável | Fórmula | Valor |
|---|---|---|
| `recuoCanto` | `RaioCurvatura · tan(α/2)` | 3,314 |
| `avancoZ` | `Deslocamento / tan α` | 5,000 |
| `zFimCurva` | `avancoZ + recuoCanto` | 8,314 |
| `zBocaInferior` / `zBocaSuperior` | `−ComprimentoRamal` / `avancoZ + ComprimentoRamal` | −20,0 / 25,0 |
| `zInicioRamo` | `−(recuoCanto + Sobreposicao)` | −4,314 |
| `alturaTronco` | `ComprimentoRamal − recuoCanto + Sobreposicao` | 17,686 |
| `alturaAlma` | `recuoCanto + zFimCurva + Sobreposicao` | 12,628 |
| `raioBlocoChanfro` | `min(RaioExterno + 1 , 2·Deslocamento − RaioExterno − 0,1)` | 5,150 |

Altura total **45 mm**, as duas bocas superiores em z = 25, centros a 10 mm (0,5 mm entre as paredes).

### A janela de `RaioCurvatura` — o valor mais sensível da peça

Com os ramais quase se tocando, o raio de curvatura fica **preso entre dois limites**:

- **Piso:** `RaioCurvatura > RaioExterno` (4,75). O kernel rejeita o arco quando a seção cruza o eixo de
  revolução — a seção dobraria para dentro de si mesma e o sólido não apareceria.
- **Teto:** `RaioCurvatura ≤ Deslocamento / (2·sin α·tan(α/2))` = **8,53**. O trecho diagonal mede
  `Deslocamento/sin α` = 7,07 mm e os dois arredondamentos consomem `2·recuoCanto` dele; passar do teto
  faz `SweepPath.FromPolyline` devolver `Error` ("o raio de canto não cabe entre os pontos 1 e 2").

O padrão **8,0** fica dentro dessa janela com 0,44 mm de reta diagonal sobrando — ou seja, os dois arcos
quase se encostam e o ramal é praticamente um S contínuo, que é a transição mais suave possível com
estas cotas. Era o 9,5 da versão anterior que não cabia: ele só funcionava com `Deslocamento` 12.

As outras folgas seguem garantidas: o trecho reto de cada boca mede 20 ≥ 3,314; a entrada do ramal mede
4,314 ≥ 3,314.

**Mínimo de `Deslocamento`:** 4,8 mm. Abaixo disso os dois tubos se interpenetram e o bloco do chanfro
não consegue mais cobrir a borda sem morder a boca vizinha (`2·Deslocamento − RaioExterno − 0,1` cai
abaixo de `RaioExterno`).

---

## `AiCore/Pecas/ConexaoYAdaptador5mm.cs`

```csharp
/// <summary>
/// Conexão em "Y" para mangueiras de 5 mm: um bocal inferior em -Z que se bifurca em DOIS bocais
/// superiores paralelos ao eixo Z, com os topos na mesma altura e QUASE SE TOCANDO — Deslocamento é o
/// afastamento de CADA ramal em relação ao eixo, então os centros ficam a 2·Deslocamento e a folga entre
/// as paredes é 2·(Deslocamento - RaioExterno), 0,5 mm com os padrões.
/// <para>
/// A bifurcação é SUAVE, não um "T": cada ramal é um único tubo varrido ao longo de um caminho
/// reta -> arco -> diagonal -> arco -> reta (SweepPath.FromPolyline com raio de canto). Não existe
/// emenda a 90° nem coletor horizontal; o caminho é quem dá a orientação, então a boca de cima sai
/// perpendicular a +Z por construção.
/// </para>
/// <para>
/// A medida crítica de cada conexão é o DIAMETRO INTERNO, que é CONICO: a abertura é mais larga
/// (RaioFuroPorta) e afina para dentro (RaioFuroJuncao), prensando a mangueira ao ser empurrada.
/// No miolo curvo o furo é um segundo sweep de mesmo caminho e seção menor, então o canal acompanha
/// a curva em vez de ser furado em cruz. Chanfro (bevel 45°) nas bordas externas das três bocas.
/// </para>
/// <para>
/// ORDEM DE FUSAO: os dois ramais se tocam TANGENCIALMENTE na virilha (há um z, dentro das curvas, em
/// que as duas superfícies externas são tangentes antes de se separarem), e o OCCT não resolve fusão de
/// sólidos tangentes: o par falha e o FuseBalanced descarta um dos lados EM SILENCIO, fazendo um
/// ramal inteiro desaparecer sem erro. Por isso a união é encadeada, uma ferramenta por booleana, e a
/// alma entra ANTES dos ramais: ela enterra o ponto de tangência dentro de um sólido, e cada ramal é
/// então fundido contra algo que já o contém. Pelo mesmo motivo os dois furos varridos são subtraídos
/// em dois CreateDifference separados — num Subtract as ferramentas são unidas entre si antes do corte.
/// NAO reagrupe essas booleanas numa só.
/// </para>
/// <para>
/// A alma também é a parede entre os dois canais: sobrevive ao corte apenas acima da altura em que os
/// dois canais se separam, que é onde eles deixam de ser uma cavidade única. Cone
/// que estreita subindo é auto-suportado, então a peça imprime em pé sobre a boca inferior.
/// </para>
/// <para>
/// RaioCurvatura vive numa JANELA estreita, e é o valor mais sensível da peça. Piso: tem que ser maior
/// que RaioExterno, porque num arco de varredura a seção não pode cruzar o eixo de revolução — ela
/// dobraria para dentro de si mesma e nada apareceria. Teto: Deslocamento / (2·sin(a)·tan(a/2)), porque
/// o trecho diagonal mede Deslocamento/sin(a) e os dois arredondamentos consomem 2·RaioCurvatura·tan(a/2)
/// dele; passar disso faz SweepPath devolver Error e os dois ramais somem. Com os padrões a janela é
/// (4,75 ; 8,53] e o valor usado é 8,0 — os dois arcos quase se encostam, deixando 0,44 mm de reta, o que
/// torna o ramal praticamente um S contínuo. Ao mexer em Deslocamento ou AnguloAbertura, recalcule o teto.
/// </para>
/// Coordenadas LOCAIS: nó da bifurcação na origem, boca inferior em -Z, bocas superiores em +Z.
/// </summary>
public sealed record ConexaoYAdaptador5mm(
    [System.ComponentModel.DataAnnotations.Range(1.0, 50.0)] double RaioExterno = 4.75,
    [System.ComponentModel.DataAnnotations.Range(1.0, 200.0)] double ComprimentoRamal = 20.0,
    [System.ComponentModel.DataAnnotations.Range(0.1, 40.0)] double RaioFuroPorta = 3.25,
    [System.ComponentModel.DataAnnotations.Range(0.1, 40.0)] double RaioFuroJuncao = 2.75,
    [System.ComponentModel.DataAnnotations.Range(0.0, 10.0)] double Chanfro = 0.5,
    [System.ComponentModel.DataAnnotations.Range(0.5, 60.0)] double Deslocamento = 5.0,
    [System.ComponentModel.DataAnnotations.Range(5.0, 80.0)] double AnguloAbertura = 45.0,
    [System.ComponentModel.DataAnnotations.Range(0.5, 60.0)] double RaioCurvatura = 8.0,
    [System.ComponentModel.DataAnnotations.Range(0.5, 40.0)] double RaioAlma = 3.5,
    [System.ComponentModel.DataAnnotations.Range(0.1, 10.0)] double Sobreposicao = 1.0) : Part
{
    protected override IFeature Build()
    {
        var dir_cima = new Vector3D(0, 0, 1);
        var dir_baixo = new Vector3D(0, 0, -1);

        // ---------- Cotas derivadas (só valores; nenhum sólido nasce aqui) ----------
        var anguloRad = AnguloAbertura * System.Math.PI / 180.0;

        // Recuo que cada canto arredondado come das retas vizinhas: R·tan(θ/2).
        var recuoCanto = RaioCurvatura * System.Math.Tan(anguloRad / 2.0);

        // Quanto o trecho diagonal sobe para afastar o ramal de Deslocamento em X.
        var avancoZ = Deslocamento / System.Math.Tan(anguloRad);

        // Altura em que as curvas terminam e os dois ramais passam a subir paralelos, 2·(Deslocamento
        // - RaioExterno) separados: a partir daqui eles não se tocam mais nunca. Usado como limite
        // superior da alma. NAO tente calcular o ponto exato de tangência por RaioExterno/tan(angulo):
        // isso só valeria se o eixo do ramal seguisse a reta diagonal, e quando os arcos consomem
        // quase todo o diagonal (o caso dos ramais quase se tocando) o eixo segue o ARCO, e a
        // separação acontece MAIS ALTO do que aquela fórmula diz.
        var zFimCurva = avancoZ + recuoCanto;

        var zBocaInferior = -ComprimentoRamal;
        var zBocaSuperior = avancoZ + ComprimentoRamal;

        // Os ramais nascem DENTRO do tronco: recuoCanto para o arco começar em z = -recuoCanto,
        // mais Sobreposicao de reta para a fusão ter volume comum em vez de encostar.
        var zInicioRamo = -(recuoCanto + Sobreposicao);
        var alturaTronco = ComprimentoRamal - recuoCanto + Sobreposicao;

        // Derivada, não parâmetro: o ápice tem que passar do fim das curvas, senão sobra tangência de
        // ramal exposta e a fusão falha em silêncio (foi exatamente esse o bug do ramal que sumia).
        var alturaAlma = recuoCanto + zFimCurva + Sobreposicao;

        var raioChanfro = RaioExterno - Chanfro;
        // O bloco do chanfro tem que passar de RaioExterno para cobrir a borda toda, mas NAO pode
        // alcançar a boca vizinha: com os dois tubos quase se tocando, a parede do outro está a
        // 2·Deslocamento - RaioExterno do centro desta boca.
        var raioBlocoChanfro = System.Math.Min(RaioExterno + 1.0, 2.0 * Deslocamento - RaioExterno - 0.1);

        // ---------- Caminhos: reta, arco, diagonal, arco, reta ----------
        // Os dois vértices internos viram arcos tangentes de RaioCurvatura; o último trecho é
        // vertical, então a boca de cima nasce perpendicular a +Z.
        var caminho_ramo_esq = SweepPath.FromPolyline(new[]
        {
            new Point3D(0, 0, zInicioRamo),
            new Point3D(0, 0, 0),
            new Point3D(-Deslocamento, 0, avancoZ),
            new Point3D(-Deslocamento, 0, zBocaSuperior)
        }, cornerRadius: RaioCurvatura);

        var caminho_ramo_dir = SweepPath.FromPolyline(new[]
        {
            new Point3D(0, 0, zInicioRamo),
            new Point3D(0, 0, 0),
            new Point3D(Deslocamento, 0, avancoZ),
            new Point3D(Deslocamento, 0, zBocaSuperior)
        }, cornerRadius: RaioCurvatura);

        // ---------- Corpo externo ----------
        var tronco = new CylinderFeature(globalBaseCenter: new Point3D(0, 0, zBocaInferior), globalDirection: dir_cima, radius: RaioExterno, height: alturaTronco) { Name = "tronco" };

        // Nasce dentro do tronco (RaioAlma < RaioExterno, sem tangência com a parede dele), atravessa
        // a tampa dele e sobe até 1 mm depois do fim das curvas, cobrindo toda a faixa em que os dois
        // ramais ainda se cruzam ou se tocam. Acima disso eles sobem paralelos e separados, então não
        // há mais nada para enterrar. De quebra, o que sobra dela depois dos furos é a parede entre os
        // dois canais.
        var alma_juncao = new ConeFeature(globalBaseCenter: new Point3D(0, 0, -recuoCanto), globalDirection: dir_cima, bottomRadius: RaioAlma, topRadius: 0.0, height: alturaAlma) { Name = "alma_juncao" };

        var ramo_dir = new SweepFeature(SweepSection.Circle(RaioExterno), caminho_ramo_dir) { Name = "ramo_dir" };
        var ramo_esq = new SweepFeature(SweepSection.Circle(RaioExterno), caminho_ramo_esq) { Name = "ramo_esq" };

        // Uma ferramenta por booleana: o kernel recebe sempre dois shapes e faz um Fuse determinístico.
        // A alma primeiro; os ramais depois, um a um, contra um sólido que já cobre a tangência.
        var corpo_nucleo = BooleanFeature.CreateUnion("corpo_nucleo", tronco, alma_juncao);
        var corpo_meio_y = BooleanFeature.CreateUnion("corpo_meio_y", corpo_nucleo, ramo_dir);
        var corpo_y = BooleanFeature.CreateUnion("corpo_y", corpo_meio_y, ramo_esq);

        // ---------- Furos internos ----------
        // Cones de aperto nas três bocas (largo na abertura, afinando para dentro) e canais varridos
        // que acompanham a curva. Cones e canais se sobrepõem em Sobreposicao.
        var furo_tronco = new ConeFeature(globalBaseCenter: new Point3D(0, 0, zBocaInferior), globalDirection: dir_cima, bottomRadius: RaioFuroPorta, topRadius: RaioFuroJuncao, height: alturaTronco) { Name = "furo_tronco", IsTransientTool = true };
        var furo_ramo_dir = new SweepFeature(SweepSection.Circle(RaioFuroJuncao), caminho_ramo_dir) { Name = "furo_ramo_dir", IsTransientTool = true };
        var furo_ramo_esq = new SweepFeature(SweepSection.Circle(RaioFuroJuncao), caminho_ramo_esq) { Name = "furo_ramo_esq", IsTransientTool = true };
        var furo_porta_esq = new ConeFeature(globalBaseCenter: new Point3D(-Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, bottomRadius: RaioFuroPorta, topRadius: RaioFuroJuncao, height: alturaTronco) { Name = "furo_porta_esq", IsTransientTool = true };
        var furo_porta_dir = new ConeFeature(globalBaseCenter: new Point3D(Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, bottomRadius: RaioFuroPorta, topRadius: RaioFuroJuncao, height: alturaTronco) { Name = "furo_porta_dir", IsTransientTool = true };

        // Os dois canais varridos são tangentes entre si do mesmo jeito que os ramais: um corte cada,
        // para nunca caírem na mesma união de ferramentas.
        var canal_dir = BooleanFeature.CreateDifference("canal_dir", corpo_y, furo_ramo_dir);
        var canal_y = BooleanFeature.CreateDifference("canal_y", canal_dir, furo_ramo_esq);

        // ---------- Chanfros (bevel 45°) nas três bocas ----------
        // Anel = bloco cilíndrico menos o cone: subtraí-lo tira só o canto externo da borda.
        var bloco_chanfro_tronco = new CylinderFeature(globalBaseCenter: new Point3D(0, 0, zBocaInferior), globalDirection: dir_cima, radius: raioBlocoChanfro, height: Chanfro) { Name = "bloco_chanfro_tronco", IsTransientTool = true };
        var cone_chanfro_tronco = new ConeFeature(globalBaseCenter: new Point3D(0, 0, zBocaInferior), globalDirection: dir_cima, bottomRadius: raioChanfro, topRadius: RaioExterno, height: Chanfro) { Name = "cone_chanfro_tronco", IsTransientTool = true };
        var anel_chanfro_tronco = BooleanFeature.CreateDifference("anel_chanfro_tronco", bloco_chanfro_tronco, cone_chanfro_tronco);
        anel_chanfro_tronco.IsTransientTool = true;

        var bloco_chanfro_esq = new CylinderFeature(globalBaseCenter: new Point3D(-Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, radius: raioBlocoChanfro, height: Chanfro) { Name = "bloco_chanfro_esq", IsTransientTool = true };
        var cone_chanfro_esq = new ConeFeature(globalBaseCenter: new Point3D(-Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, bottomRadius: raioChanfro, topRadius: RaioExterno, height: Chanfro) { Name = "cone_chanfro_esq", IsTransientTool = true };
        var anel_chanfro_esq = BooleanFeature.CreateDifference("anel_chanfro_esq", bloco_chanfro_esq, cone_chanfro_esq);
        anel_chanfro_esq.IsTransientTool = true;

        var bloco_chanfro_dir = new CylinderFeature(globalBaseCenter: new Point3D(Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, radius: raioBlocoChanfro, height: Chanfro) { Name = "bloco_chanfro_dir", IsTransientTool = true };
        var cone_chanfro_dir = new ConeFeature(globalBaseCenter: new Point3D(Deslocamento, 0, zBocaSuperior), globalDirection: dir_baixo, bottomRadius: raioChanfro, topRadius: RaioExterno, height: Chanfro) { Name = "cone_chanfro_dir", IsTransientTool = true };
        var anel_chanfro_dir = BooleanFeature.CreateDifference("anel_chanfro_dir", bloco_chanfro_dir, cone_chanfro_dir);
        anel_chanfro_dir.IsTransientTool = true;

        // ---------- Resultado ----------
        // As ferramentas que sobraram não se tocam entre si, então podem entrar num corte só.
        return BooleanFeature.CreateDifference(
            "conexao_y_adaptador_5mm",
            canal_y,
            furo_tronco, furo_porta_esq, furo_porta_dir,
            anel_chanfro_tronco, anel_chanfro_esq, anel_chanfro_dir);
    }
}
```

## `AiCore/Cena.cs` — linha a acrescentar no fim

```csharp
// Conexão em Y para mangueira de 5 mm: boca inferior apoiada no plano (a peça vai de -20 a +25 em Z local).
var conexaoY = Add(new ConexaoYAdaptador5mm(), at: Placement.At(120, 120, 20), name: "conexao_y_5mm", color: "#C8A2C8");
```

---

## Conserto opcional no kernel (fora da peça)

A falha só foi difícil de achar porque o `FuseBalanced` descarta um sólido sem dizer nada — o que
derrota o commit b855d83, que existe justamente para levar o motivo de uma feature não gerar geometria
até a barra de status. Vale fazer esse caminho reportar:

```csharp
// OpenCascadeGeometryKernel.cs, dentro do if (fused == null || fused.IsNull)
ReportProblem(feature, "a fusão de dois sólidos falhou (sólidos tangentes?) e um deles foi descartado.");
```

Exige passar a feature em questão para o `FuseBalanced` (hoje ele recebe só a lista de shapes), e os
chamadores são `BuildSweep` e `BuildBoolean`. Mesma observação vale para os cinco `return null` sem
`ReportProblem` em
[BuildSweep](AvaloniaCadEditor.Infrastructure/OpenCascade/OpenCascadeGeometryKernel.cs#L505-L597).
Decida se quer isso agora ou em commit separado — é mudança em Infrastructure, não na peça.

## Verificação

`dotnet build` **não** valida as peças: `AiCore.csproj` exclui `Cena.cs` e `Pecas/**` do `<Compile>`
(são compiladas pelo `RoslynSceneEvaluator`). A validação real é o `check`:

1. `dotnet build`
2. `AvaloniaCadEditor.UI/bin/Debug/net10.0-windows/AvaloniaCadEditor.UI.exe check AiCore --geometry`
   — constrói cada feature no kernel OCCT; tem que sair `OK: N feature(s), geometria validada.` com
   código 0.
3. **Conferência específica deste bug** (o `check` passa mesmo com um ramal descartado, porque a malha
   não fica vazia): no editor, olhar a peça de frente e confirmar que os **dois** ramais existem e são
   simétricos. Se um faltar de novo, o suspeito é a mesma tangência — aumentar `RaioAlma` engorda o
   sólido que cobre o ponto de contato.
4. Canal interno: girar a peça e conferir que as três bocas comunicam, e que existe parede entre os
   dois canais na altura da virilha (é a alma).

## Risco residual

A entrada dos ramais (`Sobreposicao` = 1 mm) é coaxial com o tronco e de mesmo raio, então as
superfícies laterais são **coincidentes** ali. Coincidência de faces (same-domain) é caso suportado
pelo OCCT e o repo já depende disso — `corpo` e `corpo_espelhado` em
[BlocoFixacaoBelt.cs](AiCore/Pecas/BlocoFixacaoBelt.cs) encostam no plano X = xm. O que o OCCT **não**
resolve é tangência, e é isso que a ordem de fusão acima neutraliza.
