using System;
using System.IO;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Infrastructure.Settings;
using FluentAssertions;
using Xunit;

namespace AvaloniaCadEditor.Tests.Settings;

/// <summary>
/// O provider guarda o conteúdo lido em memória, validado pela data de modificação do arquivo.
/// Estes testes travam as duas metades disso: que o cache existe (não relê o disco de graça) e
/// que ele nunca serve conteúdo velho depois de o arquivo mudar — o que, no caso dos seis campos
/// sem controle na interface, é a diferença entre preservar e apagar o ajuste do usuário.
/// </summary>
public class JsonTessellationSettingsProviderTests
{
    /// <summary>Pasta temporária própria; o provider real aponta para %AppData% do usuário.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path_ = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "avaloniacad-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path_);
        }

        public string Path_ { get; }

        public string File_ => System.IO.Path.Combine(Path_, "tessellation_settings.json");

        /// <summary>
        /// Grava o arquivo "à mão" (como o usuário faria num editor de texto) com uma data de
        /// modificação explicitamente mais nova. A data é fixada em vez de deixada ao sistema
        /// porque duas gravações seguidas podem cair no mesmo tick do relógio do arquivo, e o
        /// teste passaria a depender do timing.
        /// </summary>
        public void EditByHand(string json)
        {
            var previous = File.Exists(File_) ? File.GetLastWriteTimeUtc(File_) : DateTime.UtcNow;
            File.WriteAllText(File_, json);
            File.SetLastWriteTimeUtc(File_, previous.AddSeconds(1));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path_)) Directory.Delete(Path_, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void SaveSettings_DepoisGetSettings_RoundTripPreservaValores()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);

        provider.SaveSettings(new TessellationSettings
        {
            LinearDeflection = 0.01,
            ProfileSegments = 64,
            ShowInternalEdges = true,
            FeatureEdgeColor = "#FF102030"
        });

        var loaded = provider.GetSettings();

        loaded.LinearDeflection.Should().Be(0.01);
        loaded.ProfileSegments.Should().Be(64);
        loaded.ShowInternalEdges.Should().BeTrue();
        loaded.FeatureEdgeColor.Should().Be("#FF102030");
    }

    [Fact]
    public void GetSettings_QuandoArquivoNaoExiste_CriaComOsPadroes()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);

        var settings = provider.GetSettings();

        settings.ProfileSegments.Should().Be(new TessellationSettings().ProfileSegments);
        File.Exists(folder.File_).Should().BeTrue("o fallback grava os padrões no disco");
    }

    [Fact]
    public void Construtor_NaoCriaAPastaNemOArquivo()
    {
        string path = Path.Combine(Path.GetTempPath(), "avaloniacad-tests", Guid.NewGuid().ToString("N"));

        _ = new JsonTessellationSettingsProvider(path);

        Directory.Exists(path).Should().BeFalse("o construtor não deve fazer I/O");
    }

    [Fact]
    public void GetSettings_ChamadoDuasVezes_DevolveInstanciasDiferentes()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);
        provider.SaveSettings(new TessellationSettings());

        var first = provider.GetSettings();
        var second = provider.GetSettings();

        second.Should().NotBeSameAs(first, "cada chamada devolve uma cópia, nunca a instância cacheada");
    }

    [Fact]
    public void GetSettings_MutarOResultado_NaoAfetaAProximaChamada()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);
        provider.SaveSettings(new TessellationSettings { ProfileSegments = 32 });

        var mine = provider.GetSettings();
        mine.ProfileSegments = 999;

        provider.GetSettings().ProfileSegments.Should().Be(32);
    }

    [Fact]
    public void GetSettings_ComOArquivoInalterado_NaoReleODisco()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);
        provider.SaveSettings(new TessellationSettings { ProfileSegments = 48 });
        provider.GetSettings().ProfileSegments.Should().Be(48);

        // Troca o conteúdo PRESERVANDO a data de modificação: um provider que relê o disco a cada
        // chamada veria 7; um que confia no cache validado pela data continua em 48.
        var stamp = File.GetLastWriteTimeUtc(folder.File_);
        File.WriteAllText(folder.File_, "{ \"ProfileSegments\": 7 }");
        File.SetLastWriteTimeUtc(folder.File_, stamp);

        provider.GetSettings().ProfileSegments.Should().Be(48);
    }

    [Fact]
    public void GetSettings_QuandoOUsuarioEditaOArquivoAMao_PassaAVerOValorNovo()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);
        provider.SaveSettings(new TessellationSettings { LinearDeflection = 0.1 });
        provider.GetSettings().LinearDeflection.Should().Be(0.1);

        folder.EditByHand("{ \"LinearDeflection\": 0.01 }");

        provider.GetSettings().LinearDeflection.Should().Be(0.01);
    }

    /// <summary>
    /// O caso que justifica a validação por data. <c>LinearDeflection</c> não tem controle na
    /// interface: só existe por edição manual do JSON. Os setters da UI
    /// (<c>CadViewModel.ShowInternalEdges</c>, <c>ProfileSegments</c>) leem os oito campos, mudam
    /// um e gravam todos — com um cache cego, esse ciclo regravaria o valor velho por cima e
    /// apagaria o ajuste do usuário sem aviso.
    /// </summary>
    [Fact]
    public void LerMutarGravar_DepoisDeEdicaoManual_NaoApagaOCampoEditado()
    {
        using var folder = new TempFolder();
        var provider = new JsonTessellationSettingsProvider(folder.Path_);
        provider.SaveSettings(new TessellationSettings { LinearDeflection = 0.1, ShowInternalEdges = false });
        provider.GetSettings();   // popula o cache com LinearDeflection = 0.1

        folder.EditByHand("{ \"LinearDeflection\": 0.01, \"ShowInternalEdges\": false }");

        // O que o setter da UI faz ao marcar "arestas internas".
        var settings = provider.GetSettings();
        settings.ShowInternalEdges = true;
        provider.SaveSettings(settings);

        var reloaded = provider.GetSettings();
        reloaded.ShowInternalEdges.Should().BeTrue("a mudança da UI é aplicada");
        reloaded.LinearDeflection.Should().Be(0.01, "o ajuste manual sobrevive a um save vindo da UI");
    }
}
