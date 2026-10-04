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
        string content = (
            "# API do AvaloniaCad (gerada automaticamente — não edite)\n\n" +
            "Tipos e membros disponíveis no código das cenas. Gerado a partir do código do AvaloniaCad.\n\n" +
            "```\n" + CadScriptApiReference.Build() + "```\n\n" +
            BuildRulesSection()).Replace("\r\n", "\n");

        string path = project.ToFullPath(ApiReferenceFile);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    /// <summary>
    /// As regras e exemplos de como escrever o código de uma cena, para o fim do
    /// <see cref="ApiReferenceFile"/>. Texto vem inteiro de <see cref="CadScriptRules"/> — a mesma
    /// fonte do system prompt do chat e do <see cref="AgentsFile"/>.
    /// </summary>
    public static string BuildRulesSection() => $"""
        ## Como escrever o código da cena

        ### Qualquer arquivo
        {CadScriptRules.Common}
        {CadScriptRules.TrackableSolids}

        ### Arquivo de entrada ({SceneProject.DefaultEntryFile}) — statements top-level
        {CadScriptRules.SceneFile}

        ### Peças (Pecas/*.cs)
        {CadScriptRules.PartFile}

        ### Instanciando uma peça no arquivo de entrada
        {CadScriptRules.PartUsage}

        ### Exemplos de peça

        ```csharp
        {CadScriptRules.PartExamplePlaca}
        ```

        ```csharp
        {CadScriptRules.PartExampleChapaFurada}
        ```

        ```csharp
        {CadScriptRules.PartExampleBlocoQuatroFuros}
        ```

        E o arquivo de entrada que usa a `Placa` acima (sem redeclarar o tipo):

        ```csharp
        {CadScriptRules.SceneUsingPartExample}
        ```

        """;

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
        - `{SceneProject.DefaultEntryFile}`: a cena (top-level statements).
        - `Pecas/`: as peças e os tipos reutilizáveis usados pela cena.
        - `{ApiReferenceFile}`: referência completa da API disponível (gerada pelo app; não edite).
        - `.cad/`: arquivos gerados pelo app. Não edite.

        ## Regras (qualquer arquivo)
        {CadScriptRules.Common}
        - Use apenas os tipos de `{ApiReferenceFile}` e da biblioteca padrão do .NET.
        {CadScriptRules.TrackableSolids}

        ## Arquivo de entrada (`{SceneProject.DefaultEntryFile}`)
        {CadScriptRules.SceneFile}

        ## Peças paramétricas (`Pecas/`)
        {CadScriptRules.PartFile}

        ### Instanciando uma peça na cena
        {CadScriptRules.PartUsage}

        Exemplos completos em `{ApiReferenceFile}` (seção "Como escrever o código da cena").

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

    /// <summary>
    /// Cria um arquivo de peça na cena a partir de um nome ("Suporte", "Pecas/Suporte" ou
    /// "Pecas/Suporte.cs"). Sem pasta, vai para Pecas/. Devolve o caminho relativo criado.
    /// </summary>
    public static string CreatePartFile(SceneProject project, string requestedName)
    {
        string name = (requestedName ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Digite um nome para o arquivo (ex.: Suporte ou Pecas/Suporte).");
        }
        if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            name += ".cs";
        }
        if (!name.Contains('/'))
        {
            name = "Pecas/" + name;
        }

        string fullPath = project.ToFullPath(name);
        string relative = project.ToRelativePath(fullPath);
        if (project.IsIgnoredPath(fullPath))
        {
            throw new InvalidOperationException($"'{relative}' fica fora da cena ou numa pasta ignorada (bin/, obj/, .cad/).");
        }
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"O arquivo '{relative}' já existe.");
        }

        string typeName = ToTypeName(Path.GetFileNameWithoutExtension(fullPath));
        Write(project.RootPath, relative, $$"""
            /// <summary>Descreva a peça {{typeName}}.</summary>
            public sealed record {{typeName}}(double Largura = 50, double Comprimento = 50, double Espessura = 10) : Part
            {
                // Geometria em coordenadas locais: a origem da peça é (0, 0, 0).
                protected override IFeature Build() =>
                    new BoxFeature(Point3D.Origin, Largura, Comprimento, Espessura) { Name = "{{typeName}}" };
            }

            """);
        return relative;
    }

    /// <summary>Converte um nome de arquivo num identificador C# válido (ex.: "suporte-lateral" → "SuporteLateral").</summary>
    public static string ToTypeName(string fileName)
    {
        var parts = fileName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var clean = new string(part.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(clean[0])).Append(clean, 1, clean.Length - 1);
        }
        string result = sb.ToString();
        if (result.Length == 0) return "NovaPeca";
        return char.IsDigit(result[0]) ? "Peca" + result : result;
    }

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
