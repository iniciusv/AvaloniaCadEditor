using System;
using System.IO;
using System.Text.Json;
using AvaloniaCadEditor.Application.Interfaces;
using AvaloniaCadEditor.Domain.Settings;

namespace AvaloniaCadEditor.Infrastructure.Settings;

/// <summary>
/// Persiste a configuração de tesselação em %AppData%/AvaloniaCadEditor/tessellation_settings.json.
/// <para>
/// Guarda o último conteúdo lido em memória, validado pela data de modificação do arquivo: quem
/// chama <see cref="GetSettings"/> em caminho quente (uma vez por frame no render, uma vez por
/// feature ao refazer a malha da cena) paga um <c>stat</c> em vez de uma leitura e desserialização
/// do JSON.
/// </para>
/// <para>
/// A data de modificação é consultada SEMPRE, e não só na primeira vez, porque seis dos oito
/// campos (deflexões, FaceSegments, CreaseAngle e as duas cores de aresta) não têm controle na
/// interface: editar o arquivo à mão é a única maneira de ajustá-los. Um cache cego quebraria
/// isso de duas formas — a edição não apareceria, e o próximo <see cref="SaveSettings"/> vindo da
/// UI (que lê os oito campos, muda um e grava todos) regravaria os valores velhos por cima,
/// apagando o ajuste em silêncio.
/// </para>
/// </summary>
public class JsonTessellationSettingsProvider : ITessellationSettingsProvider
{
    /// <summary>Conteúdo lido e a data de modificação que ele representa, trocados juntos.</summary>
    private sealed record Snapshot(TessellationSettings Settings, DateTime Stamp);

    private readonly string _filePath;

    // Trocado por inteiro, nunca mutado no lugar: a atribuição de referência é atômica, e por isso
    // a leitura pela render thread (OpenGlControlBase.OnOpenGlRender) não precisa de lock. Ao
    // alterar isto, mantenha a regra — mutar o objeto cacheado reintroduz a corrida.
    private volatile Snapshot? _snapshot;

    /// <param name="folder">
    /// Pasta do arquivo. Por padrão, %AppData%/AvaloniaCadEditor. Existe para os testes poderem
    /// usar uma pasta temporária em vez do arquivo real do usuário.
    /// </param>
    public JsonTessellationSettingsProvider(string? folder = null)
    {
        folder ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AvaloniaCadEditor");
        _filePath = Path.Combine(folder, "tessellation_settings.json");
    }

    public TessellationSettings GetSettings()
    {
        // Lida ANTES do conteúdo: se uma gravação cair entre as duas, o conteúdo novo fica
        // guardado sob a data antiga e a próxima chamada relê — uma leitura extra, nunca um
        // valor velho preso no cache.
        DateTime stamp = CurrentStamp();

        var snapshot = _snapshot;
        if (snapshot != null && snapshot.Stamp == stamp)
        {
            return Copy(snapshot.Settings);
        }

        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<TessellationSettings>(json);
                if (settings != null)
                {
                    _snapshot = new Snapshot(Copy(settings), stamp);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                // Não cacheia a falha: a próxima chamada tenta de novo.
                Console.WriteLine($"[Settings] Falha ao ler arquivo de configurações: {ex.Message}");
            }
        }

        // Default fallback
        var defaultSettings = new TessellationSettings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(TessellationSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
            _snapshot = new Snapshot(Copy(settings), CurrentStamp());
        }
        catch (Exception ex)
        {
            _snapshot = null;
            Console.WriteLine($"[Settings] Falha ao salvar arquivo de configurações: {ex.Message}");
        }
    }

    private DateTime CurrentStamp()
    {
        try
        {
            return File.Exists(_filePath) ? File.GetLastWriteTimeUtc(_filePath) : default;
        }
        catch (Exception)
        {
            // Sem data confiável, o cache não vale: devolve algo que nunca casa.
            return DateTime.MaxValue;
        }
    }

    /// <summary>
    /// Cópia dos campos. <see cref="TessellationSettings"/> é mutável, e quem chama
    /// <see cref="GetSettings"/> altera o objeto recebido antes de salvar — devolver a instância
    /// cacheada deixaria essa alteração visível para todo mundo antes do save, inclusive para a
    /// chave do cache de malhas do kernel (<c>GeometryKey</c>), que é montada desses campos.
    /// </summary>
    private static TessellationSettings Copy(TessellationSettings s) => new()
    {
        LinearDeflection = s.LinearDeflection,
        AngularDeflection = s.AngularDeflection,
        ProfileSegments = s.ProfileSegments,
        FaceSegments = s.FaceSegments,
        ShowInternalEdges = s.ShowInternalEdges,
        CreaseAngle = s.CreaseAngle,
        FeatureEdgeColor = s.FeatureEdgeColor,
        InternalEdgeColor = s.InternalEdgeColor
    };
}
