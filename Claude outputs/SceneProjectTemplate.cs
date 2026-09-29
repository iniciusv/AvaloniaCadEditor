using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Scripting;

namespace AvaloniaCadEditor.Application.Scenes;

/// <summary>Onde estão as ferramentas do AvaloniaCad que a cena gerada precisa referenciar.</summary>
/// <param name="AvaloniaCadDirectory">Pasta com AvaloniaCadEditor.Domain.dll (termina com separador).</param>
/// <param name="CheckExecutable">Executável que roda <c>check &lt;pasta&gt;</c>.</param>
public sealed record SceneToolPaths(string AvaloniaCadDirectory, string CheckExecutable)
{
    /// <summary>Caminhos do processo atual (o app em execução).</summary>
    public static SceneToolPaths FromCurrentProcess() =>
        new(AppContext.BaseDirectory, Environment.ProcessPath ?? "AvaloniaCadEditor.UI.exe");
}

/// <summary>
/// Cria a estrutura de uma cena autônoma: .csproj que compila com <c>dotnet build</c>,
/// cena de exemplo, e o protocolo para IAs externas (AGENTS.md + .cad/api.md).
/// </summary>
public static class SceneProjectTemplate
{
    public const string AgentsFile = "AGENTS.md";
    public const string ClaudeFile = "CLAUDE.md";
    public const string ApiReferenceFile = SceneProject.GeneratedFolder + "/api.md";

    /// <summary>
    /// Cria a cena em <paramref name="folder"/> (que deve estar vazia ou não existir).
    /// O nome da cena é o nome da pasta.
    /// </summary>
    public static SceneProject Create(string folder, SceneToolPaths tools)
    {
        string root = Path.GetFullPath(folder);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException($"Escolha uma pasta vazia para a nova cena: {root}");
        }
        Directory.CreateDirectory(root);

        string name = SanitizeProjectName(new DirectoryInfo(root).Name);

        Write(root, name + ".csproj", BuildProjectFile(tools));
        Write(root, SceneProject.DefaultEntryFile, SampleScene);
        Write(root, "Pecas/Medidas.cs", SamplePart);
        Write(root, ".gitignore", "bin/\nobj/\n");
        Write(root, AgentsFile, BuildAgentsFile(name, tools));
        Write(root, ClaudeFile, "@AGENTS.md\n");

        var project = SceneProject.Load(root);
        RefreshGeneratedFiles(project);
        return project;
    }

    /// <summary>
    /// Regrava os arquivos derivados do app (referência da API). Chamado ao abrir a cena para que
    /// uma atualização do AvaloniaCad chegue às IAs externas.
    /// </summary>
    public static void RefreshGeneratedFiles(SceneProject project)
    {
        string content =
            "# API do AvaloniaCad (gerada automaticamente — não edite)\n\n" +
            "Tipos e membros disponíveis no código das cenas. Gerado a partir do código do AvaloniaCad.\n\n" +
            "```\n" + CadScriptApiReference.Build().Replace("\r\n", "\n") + "```\n";

        string path = project.ToFullPath(ApiReferenceFile);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    public static string BuildProjectFile(SceneToolPaths tools)
    {
        string dir = tools.AvaloniaCadDirectory;
        if (!dir.EndsWith(Path.DirectorySeparatorChar) && !dir.EndsWith(Path.AltDirectorySeparatorChar))
        {
            dir += Path.DirectorySeparatorChar;
        }

        var usings = new StringBuilder();
        foreach (var ns in SceneHost.GlobalUsings)
        {
            usings.AppendLine($"    <Using Include=\"{ns}\" />");
        }
        usings.AppendLine($"    <Using Include=\"{SceneHost.GlobalStaticUsing}\" Static=\"true\" />");

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- Cena do AvaloniaCad. Compila sozinha com `dotnet build`; o app lê só este arquivo
                   por convenção (todos os *.cs da pasta, exceto bin/, obj/ e .cad/). -->
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
                <Nullable>disable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <AvaloniaCadDir Condition="'$(AvaloniaCadDir)' == ''">{SecurityElement.Escape(dir)}</AvaloniaCadDir>
              </PropertyGroup>

              <ItemGroup>
                <Reference Include="AvaloniaCadEditor.Domain">
                  <HintPath>$(AvaloniaCadDir)AvaloniaCadEditor.Domain.dll</HintPath>
                </Reference>
              </ItemGroup>

              <ItemGroup>
            {usings.ToString().TrimEnd()}
              </ItemGroup>

              <ItemGroup>
                <Compile Remove=".cad/**" />
              </ItemGroup>
            </Project>

            """;
    }

    public static string BuildAgentsFile(string sceneName, SceneToolPaths tools) => $"""
        # Cena "{sceneName}" (AvaloniaCad)

        Esta pasta é uma cena do AvaloniaCad: um projeto C# comum que descreve um modelo 3D paramétrico.
        O app AvaloniaCad observa esta pasta e redesenha o modelo sempre que um arquivo muda.

        ## Estrutura
        - `{SceneProject.DefaultEntryFile}`: a cena (top-level statements). Registra cada feature com `AddFeature(variavel)`.
        - `Pecas/`: classes e funções reutilizáveis usadas pela cena. Arquivos aqui NÃO podem ter top-level statements.
        - `{ApiReferenceFile}`: referência completa da API disponível (gerada pelo app; não edite).
        - `.cad/`: arquivos gerados pelo app. Não edite.

        ## Regras
        - Unidades em milímetros; o eixo Z aponta para cima.
        - Use apenas os tipos de `{ApiReferenceFile}` e da biblioteca padrão do .NET. Não use System.IO, rede, threads ou reflexão.
        - Toda feature visível deve ser registrada com `AddFeature(...)`. Dê a cada feature `Name` igual ao nome da variável.
        - Ferramentas de operações booleanas (ex.: o cilindro que fura) usam `IsTransientTool = true` e também são registradas.

        ## Verificação (obrigatória depois de qualquer edição)
        1. `dotnet build` — garante que o código compila (tipos e referências entre arquivos).
        2. `"{tools.CheckExecutable}" check . --geometry` — executa a cena e valida a geometria no kernel.

        Os dois precisam terminar com código de saída 0. Os erros vêm no formato `arquivo(linha,coluna): mensagem`;
        corrija e repita até passar.

        """;

    private const string SampleScene = """
        // Cena principal: top-level statements. Tipos reutilizáveis ficam em Pecas/.
        var base_1 = new BoxFeature(baseCorner: new Point3D(0, 0, 0), width: 60, length: 40, height: Medidas.Espessura) { Name = "base_1" };
        AddFeature(base_1);

        """;

    private const string SamplePart = """
        /// <summary>Medidas compartilhadas entre as peças desta cena.</summary>
        public static class Medidas
        {
            /// <summary>Espessura padrão das chapas, em mm.</summary>
            public const double Espessura = 10;
        }

        """;

    private static void Write(string root, string relativePath, string content)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
    }

    private static string SanitizeProjectName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray();
        string result = new string(chars).Trim('.');
        return string.IsNullOrEmpty(result) ? "Cena" : result;
    }
}
