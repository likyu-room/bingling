// What Bingling shows and writes into files names it Bingling: error messages, version lines, file metadata, dialog
// texts. A "filee" left in a string literal of the source is the original Filee by name, an internal name, or an
// identity of the macOS and Linux packages that is renamed in a step of its own; anything else fails here.

using System.Text.RegularExpressions;

namespace Filee.App.Tests;

public class SourceNameTests
{
    private static readonly Regex Literal = new(@"""(?:[^""\\\r\n]|\\.)*""");

    private static readonly Regex Comment = new(@"<!--.*?-->|/\*.*?\*/", RegexOptions.Singleline);

    // The name as a word or a part of one, but not "FileExplorer" and the like.
    private static readonly Regex Name = new("Filee|filee|FILEE");

    private static readonly Regex Allowed = new(string.Join('|',
        @"Filee\.[A-Z][A-Za-z]*", @"Filee\.slnx", @"fonts:Filee", @"FileeRelease", @"FILEE_[A-Z_]+", // namespaces, resources, build and environment names
        @"Filee Plus", @"FileePlus", // the former name, only to find and move its data
        @"Filee 1\.12\.0", @"based on Filee", @"@filee", // the original Filee: the release this builds on, its author's page
        @"/Icons/filee\.", @"\.filee-component", @"filee-\*\.log", // asset names, the engine marker, Filee Plus's old logs
        @"com\.filee\.app", @"filee\.desktop", @"filee\.nemo_action", @"filee-convert", @"FileeServicesProvider"));

    [Fact]
    public void No_string_in_the_source_calls_this_app_Filee()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Filee.slnx")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Repository root not found.");

        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal)
                                 || f.EndsWith(".c", StringComparison.Ordinal) || f.EndsWith(".rc", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var lines = Comment.Replace(File.ReadAllText(file), "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith('*'))
                    continue;
                found.AddRange(Literal.Matches(code)
                    .Where(m => Name.IsMatch(Allowed.Replace(m.Value, "")))
                    .Select(m => $"{Path.GetRelativePath(dir, file)}: {m.Value}"));
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }
}
