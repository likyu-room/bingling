using System.Text.Json;
using System.Text.RegularExpressions;

namespace Filee.App.Tests;

/// <summary>Keeps en / es / ko / zh-CN / pl / hi in sync so no language silently falls back to English.</summary>
public partial class LocalizationTests
{
    private static string I18nDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Filee.slnx")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Filee.App", "Assets", "i18n");
        }
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDirectory, code + ".json")))!;

    public static TheoryData<string> Languages => new() { "es", "ko", "zh-CN", "pl", "hi" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Load("en").Keys.ToHashSet();
        var other = Load(code).Keys.ToHashSet();

        Assert.Empty(english.Except(other));
        Assert.Empty(other.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Format_placeholders_match_english(string code)
    {
        var english = Load("en");
        foreach (var (key, value) in Load(code))
            Assert.True(
                Placeholders(english[key]).SetEquals(Placeholders(value)),
                $"{code}:{key} placeholders differ from English");
    }

    [Fact]
    public void Every_built_in_preset_has_its_name_in_english_and_the_tools_a_short_ring_name()
    {
        var english = Load("en");
        var named = Filee.Core.Presets.BuiltInData.CreatePresets().Where(p => p.NameKey is not null).ToList();
        Assert.All(named, p => Assert.True(english.ContainsKey(p.NameKey!), $"{p.Id}: {p.NameKey}"));
        // Shown on the ring the "Tools" group fans out, where the group's name already gives the context.
        foreach (var id in new[] { "crop", "add-background", "redact", "annotate", "compress-image", "strip-metadata" })
            Assert.True(english.ContainsKey(named.Single(p => p.Id == id).NameKey + ".short"), id);
    }

    [Fact]
    public void Every_key_used_in_xaml_exists()
    {
        var english = Load("en");
        var src = Path.GetFullPath(Path.Combine(I18nDirectory, "..", ".."));
        var missing = Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => DynamicResourceKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => k.Contains('.') && !k.StartsWith("Icon.", StringComparison.Ordinal))
            .Where(k => !english.ContainsKey(k))
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_string_still_calls_this_app_Filee_Plus(string code)
    {
        // Keys about the original Filee (the "Filee classic" look, the support and sponsor texts) say "Filee" on purpose;
        // none of them ever said "Filee Plus", so no key needs an exception.
        var old = Load(code).Where(p => p.Value.Contains("Filee Plus", StringComparison.OrdinalIgnoreCase)).Select(p => p.Key);
        Assert.Empty(old);
        Assert.DoesNotContain(Load("en"), p => p.Value.Contains("Filee Plus", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Only_texts_about_the_original_Filee_say_Filee(string code)
    {
        // The "Filee classic" look, the support card and the sponsor window name the original Filee; nothing else
        // names Filee, and the command line is "bingling".
        foreach (var language in new[] { code, "en" })
        {
            var other = Load(language)
                .Where(p => p.Value.Contains("filee", StringComparison.OrdinalIgnoreCase))
                .Where(p => p.Key != "theme.style.Classic" && !p.Key.StartsWith("about.support_", StringComparison.Ordinal)
                            && !p.Key.StartsWith("sponsor.", StringComparison.Ordinal))
                .Select(p => p.Key);
            Assert.Empty(other);
            Assert.Contains("bingling watch ", Load(language)["watch.cli_hint"]);
        }
    }

    [Fact]
    public void The_app_is_called_Bingling_and_the_original_Filee_keeps_its_name()
    {
        Assert.Equal("빙글링", Load("ko")["app.name"]);
        foreach (var code in new[] { "en", "es", "hi", "pl", "zh-CN" })
            Assert.Equal("Bingling", Load(code)["app.name"]);

        foreach (var code in new[] { "en", "ko", "es", "hi", "pl", "zh-CN" })
        {
            var strings = Load(code);
            Assert.Contains("Filee", strings["theme.style.Classic"]);
            Assert.Contains("Filee", strings["about.support_title"]);
            Assert.Contains("Filee", strings["sponsor.title"]);
            Assert.Contains("{0}", strings["about.based_on"]);
        }
    }

    [Fact]
    public void Korean_particles_after_the_name_fit_a_final_consonant()
    {
        // 빙글링 ends in ㅇ: 을 / 이 / 으로 / 과 / 은, never 를 / 가 / 로 / 와 / 는.
        var wrong = Load("ko").Where(p => WrongParticle().IsMatch(p.Value)).Select(p => p.Key);
        Assert.Empty(wrong);

        var ko = Load("ko");
        Assert.Equal("빙글링을 계속 실행할까요?", ko["close.title"]);
        Assert.Equal("빙글링으로 변환", ko["general.context_menu_label"]);
        Assert.Equal("로그인할 때 빙글링 시작", ko["general.start_with_system"]);
        Assert.Equal("빙글링 열기", ko["tray.open"]);
        Assert.Equal("빙글링이 관리자 권한으로 실행 중이에요", ko["elevation.notice_title"]);
    }

    /// <summary>The name followed by a particle that belongs after a vowel.</summary>
    [GeneratedRegex("빙글링(?:를|가|는|와|로|랑|라)")]
    internal static partial Regex WrongParticle();

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{DynamicResource ([\w.]+)\}")]
    private static partial Regex DynamicResourceKey();
}
