using System.Collections.Generic;

namespace AvaloniaCadEditor.Application.Interfaces;

/// <summary>
/// A pasta da biblioteca de peças: uma pasta única, fora de qualquer cena, que o usuário escolhe e pode
/// versionar com git. Peças de um projeto são uma subpasta dela.
/// </summary>
public interface IPartLibraryStore
{
    /// <summary>Caminho absoluto da pasta (pode ainda não existir).</summary>
    string RootPath { get; }

    /// <summary>
    /// Os <c>.cs</c> da pasta e das subpastas, com caminho relativo e '/' como separador, em ordem estável.
    /// Pasta inexistente devolve vazio. Ignora bin/, obj/, .git/ e .vs/.
    /// </summary>
    IReadOnlyList<ScriptDocument> ReadDocuments();

    /// <summary>Grava um arquivo (caminho relativo, com '/'), criando as pastas. Só a pedido do usuário (Enviar).</summary>
    void Write(string relativePath, string text);

    /// <summary>Troca a pasta e lembra a escolha nas configurações.</summary>
    void SetRootPath(string folder);
}

/// <summary>Imagens de miniatura guardadas pela chave (hash do conteúdo), fora da pasta da biblioteca.</summary>
public interface IThumbnailCache
{
    byte[]? Get(string key);
    void Put(string key, byte[] png);
}
