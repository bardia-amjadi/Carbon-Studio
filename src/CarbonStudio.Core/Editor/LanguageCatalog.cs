namespace CarbonStudio.Core.Editor;

/// <param name="Id">Language identifier understood by the Monaco editor (verified against the bundled monaco-editor 0.57 grammars).</param>
public sealed record LanguageInfo(string Id, string DisplayName);

/// <summary>Maps file names to editor languages. Grammars provide syntax highlighting only; they imply no language-server support.</summary>
public static class LanguageCatalog
{
    public static LanguageInfo PlainText { get; } = new("plaintext", "Plain Text");

    private static readonly Dictionary<string, LanguageInfo> ByExtension = Build();

    private static readonly Dictionary<string, LanguageInfo> ByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = new("dockerfile", "Dockerfile"),
        [".editorconfig"] = new("ini", "INI"),
        [".gitconfig"] = new("ini", "INI"),
        ["CMakeLists.txt"] = PlainText,
    };

    /// <summary>Chooses a language from a file name (path allowed). Unknown names are plain text.</summary>
    public static LanguageInfo Detect(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return PlainText;
        }

        var name = Path.GetFileName(fileNameOrPath);
        if (ByFileName.TryGetValue(name, out var exact))
        {
            return exact;
        }

        var extension = Path.GetExtension(name);
        return extension.Length > 1 && ByExtension.TryGetValue(extension, out var info) ? info : PlainText;
    }

    private static Dictionary<string, LanguageInfo> Build()
    {
        var map = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);

        void Add(string id, string display, params string[] extensions)
        {
            var info = new LanguageInfo(id, display);
            foreach (var extension in extensions)
            {
                map[extension] = info;
            }
        }

        Add("csharp", "C#", ".cs", ".csx");
        Add("vb", "Visual Basic", ".vb");
        Add("fsharp", "F#", ".fs", ".fsx", ".fsi");
        Add("cpp", "C/C++", ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".ino");
        Add("javascript", "JavaScript", ".js", ".mjs", ".cjs", ".jsx");
        Add("typescript", "TypeScript", ".ts", ".tsx", ".mts", ".cts");
        Add("json", "JSON", ".json", ".jsonc", ".webmanifest", ".code-workspace");
        Add(
            "xml",
            "XML",
            ".xml", ".xaml", ".axaml", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".props", ".targets", ".config", ".resx", ".slnx",
            ".wxs", ".wixproj", ".nuspec", ".manifest", ".svg", ".xsl", ".xslt", ".plist", ".appxmanifest");
        Add("html", "HTML", ".html", ".htm", ".xhtml");
        Add("razor", "Razor", ".cshtml", ".razor");
        Add("handlebars", "Handlebars", ".hbs", ".handlebars");
        Add("css", "CSS", ".css");
        Add("scss", "SCSS", ".scss");
        Add("less", "Less", ".less");
        Add("python", "Python", ".py", ".pyw", ".pyi");
        Add("markdown", "Markdown", ".md", ".markdown");
        Add("yaml", "YAML", ".yml", ".yaml");
        Add("ini", "INI", ".ini", ".cfg", ".conf");
        Add("shell", "Shell Script", ".sh", ".bash", ".zsh");
        Add("powershell", "PowerShell", ".ps1", ".psm1", ".psd1");
        Add("bat", "Batch", ".bat", ".cmd");
        Add("sql", "SQL", ".sql");
        Add("java", "Java", ".java");
        Add("kotlin", "Kotlin", ".kt", ".kts");
        Add("go", "Go", ".go");
        Add("rust", "Rust", ".rs");
        Add("php", "PHP", ".php");
        Add("ruby", "Ruby", ".rb");
        Add("lua", "Lua", ".lua");
        Add("swift", "Swift", ".swift");
        Add("dart", "Dart", ".dart");
        Add("dockerfile", "Dockerfile", ".dockerfile");
        Add("protobuf", "Protocol Buffers", ".proto");
        Add("graphql", "GraphQL", ".graphql", ".gql");
        Add("r", "R", ".r");
        Add("perl", "Perl", ".pl", ".pm");
        Add("scala", "Scala", ".scala");
        Add("objective-c", "Objective-C", ".m", ".mm");
        return map;
    }
}
