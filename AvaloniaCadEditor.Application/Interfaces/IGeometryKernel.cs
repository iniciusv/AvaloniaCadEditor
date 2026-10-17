using System.Collections.Generic;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.Mesh;

using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Application.Interfaces;

public interface IGeometryKernel
{
    TriangleMesh BuildFeatureMesh(IFeature feature, TessellationSettings? settings = null);
    TriangleMesh BuildSolidMesh(IEnumerable<IFeature> features, TessellationSettings? settings = null);
    void InvalidateShape(System.Guid featureId);
    void InvalidateAll();

    /// <summary>
    /// Com <c>true</c>, todo <see cref="FilletFeature"/> (arredondamento ou chanfro) devolve o sólido base,
    /// inclusive dentro de booleanas e de peças — para marcar as quinas vivas do base no esboço. É estado de
    /// sessão, nunca do modelo: o CadScript não muda. Padrão: o kernel não sabe suprimir e ignora.
    /// </summary>
    bool SuppressFillets { get => false; set { } }

    /// <summary>
    /// Chamado quando a cena é reexecutada (features novas, com Ids novos). Descarta o que é
    /// indexado por Id, mas pode manter malhas indexadas pela geometria (<see cref="FeatureFingerprint"/>)
    /// para reaproveitá-las. Padrão: o mesmo que <see cref="InvalidateAll"/>.
    /// </summary>
    void BeginSceneRebuild() => InvalidateAll();

    /// <summary>
    /// Por que a geometria de uma feature não saiu, indexado pelo Id dela — caminho de varredura
    /// inválido, seção que não cabe no raio de curvatura, exceção na construção.
    /// <para>
    /// Existe porque uma primitiva degenerada devolve malha VAZIA, e não erro: sem isto o sólido
    /// some da viewport e a explicação não chega a lugar nenhum. Tem a mesma vida do cache de
    /// formas (some ao invalidar a feature ou reexecutar a cena), e não a da última chamada de
    /// construção, já que o cache de malhas da cena guarda a malha vazia e não chamaria o kernel
    /// de novo.
    /// </para>
    /// <para>A mensagem já traz o nome da feature; não é preciso procurá-la para exibir.</para>
    /// </summary>
    IReadOnlyDictionary<System.Guid, string> BuildProblems => NoProblems;

    private static readonly IReadOnlyDictionary<System.Guid, string> NoProblems =
        new Dictionary<System.Guid, string>();
}
