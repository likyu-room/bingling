// Keeps the installer script (installer/Filee.iss) and the app in step: the options Setup starts Bingling with, the
// Explorer menu package it registers, the engine folders it looks into and how it removes Filee Plus (1.1.0).

using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core;
using Filee.Core.Localization;
using Filee.Engines.Infrastructure;
using Filee.Platform.Windows;

namespace Filee.App.Tests;

public class InstallerTests
{
    private static string Script
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "Filee.slnx")))
                dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Repository root not found.");
            return File.ReadAllText(Path.Combine(dir, "installer", "Filee.iss"));
        }
    }

    [Fact]
    public void Script_passes_only_options_the_app_knows()
    {
        // Each option below is checked by a parse test; a new one in the script must be added here.
        var used = Regex.Matches(Script, @"--[a-z][a-z-]+=?").Select(m => m.Value).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "--background", "--quit", "--uninstall-cleanup", "--install-engines=", "--start-with-windows=", "--context-menu=" },
            used);
    }

    [Fact]
    public void Installer_choices_are_parsed()
    {
        var options = CommandLine.Parse(["--install-engines=pandoc, FFmpeg,pandoc", "--start-with-windows=off", "--context-menu=on"]);
        Assert.Equal(["pandoc", "ffmpeg"], options.InstallEngines);
        Assert.False(options.StartWithWindows);
        Assert.True(options.ContextMenu);
        Assert.False(options.Background);
        Assert.Empty(options.ConvertFiles);

        // Picking no engine is an answer too (the first-run question is skipped), unlike not passing the option.
        Assert.Empty(CommandLine.Parse(["--install-engines="]).InstallEngines!);
        Assert.Null(CommandLine.Parse(["--background"]).InstallEngines);
        Assert.Null(CommandLine.Parse(["--start-with-windows=maybe"]).StartWithWindows);
        Assert.Null(CommandLine.Parse([]).ContextMenu);
        Assert.True(CommandLine.Parse(["--start-with-system=on"]).StartWithWindows);
    }

    [Fact]
    public void Quit_and_cleanup_are_recognised()
    {
        Assert.True(CommandLine.Parse(["--quit"]).Quit);
        Assert.True(CommandLine.Parse(["--Uninstall-Cleanup"]).UninstallCleanup);
        Assert.False(CommandLine.Parse(["--background"]).Quit);
    }

    [AvaloniaFact]
    public void Engines_from_the_installer_start_only_when_something_is_left_to_install()
    {
        TestServices.EnsureInitialized("en");
        var vm = new EngineSetupViewModel(AppHost.Get<EngineDownloadService>(), AppHost.Get<ILocalizer>());
        Assert.False(vm.InstallNow(["no-such-engine"]));
        Assert.False(vm.InstallNow([]));
        Assert.False(vm.Started);
    }

    [Fact]
    public void Script_uses_the_explorer_menu_package_identity()
    {
        var script = Script;
        Assert.Contains($"ExplorerMenuName = '{ExplorerMenuPackage.PackageName}';", script);
        Assert.Contains($"ExplorerMenuFamily = '{ExplorerMenuPackage.FamilyName}';", script);
        Assert.Contains($"ExplorerMenuDll = '{ExplorerMenuPackage.DllFileName}';", script);
        Assert.Contains($"ExplorerMenuPackageFile = '{ExplorerMenuPackage.PackageFileName}';", script);
        Assert.Contains($"ExplorerMenuMinimumBuild = {ExplorerMenuPackage.MinimumBuild};", script);
    }

    [Fact]
    public void Script_finds_downloaded_engines_where_the_app_puts_them()
    {
        var marker = typeof(EngineInstaller).GetField("MarkerName", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue();
        Assert.Contains($"EngineMarker = '{marker}';", Script);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bingling", "engines"),
            EngineEnvironment.DownloadRoot);
        Assert.Contains(@"ExpandConstant('{localappdata}\Bingling\engines\')", Script);
    }

    [Fact]
    public void Script_has_its_own_identity_so_it_installs_next_to_the_original_Filee()
    {
        var script = Script;
        Assert.Contains($"#define AppName \"{AppIdentity.Name}\"", script);
        Assert.Contains($"#define AppGuid \"{BinglingGuid}\"", script);
        Assert.Contains($"OutputBaseFilename={AppIdentity.FilePrefix}-{{#AppVersion}}-win-Setup", script);
        Assert.Contains(@"DefaultDirName={autopf}\{#AppName}", script);
        // The original Filee's installer id, upstream address and per-user folders must not appear anywhere.
        Assert.DoesNotContain(OriginalFileeGuid, script);
        Assert.DoesNotContain("KnifeLemon", script);
        Assert.DoesNotContain(@"{localappdata}\Filee\", script);
        Assert.DoesNotContain(@"{localappdata}\Filee'", script);
    }

    [Fact]
    public void Bingling_is_a_new_program_not_an_update_of_Filee_Plus_or_the_original_Filee()
    {
        Assert.NotEqual(FileePlusGuid, BinglingGuid);
        Assert.NotEqual(OriginalFileeGuid, BinglingGuid);
        // Filee Plus's id is only used to find and remove it.
        Assert.Single(Regex.Matches(Script, FileePlusGuid));
        Assert.Contains($"FileePlusGuid = '{FileePlusGuid}';", Script);
    }

    [Fact]
    public void Setup_shows_the_name_in_the_install_language_and_never_says_Filee()
    {
        var script = Script;
        Assert.Contains("en.ShortcutName=Bingling", script);
        Assert.Contains("ko.ShortcutName=빙글링", script);
        Assert.Contains("zhcn.ShortcutName=Bingling", script);
        Assert.Contains(@"Name: ""{autoprograms}\{cm:ShortcutName}""", script);
        Assert.Contains(@"Name: ""{autodesktop}\{cm:ShortcutName}""", script);
        // No text Setup shows names Filee or its command.
        var messages = script.Split('\n').Where(l => Regex.IsMatch(l, @"^(en|ko|zhcn)\.")).ToList();
        Assert.NotEmpty(messages);
        Assert.DoesNotContain(messages, l => l.Contains("filee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_command_line_is_offered_as_bingling_and_left_off_unless_chosen()
    {
        var script = Script;
        Assert.Contains("en.TaskCli=Add the \"bingling\" command to PATH (convert from the command line)", script);
        Assert.Contains("ko.TaskCli=\"bingling\" 명령어를 PATH에 추가 (명령줄에서 변환)", script);
        Assert.Contains("zhcn.TaskCli=将 \"bingling\" 命令添加到 PATH（在命令行中转换）", script);
        var task = script.Split('\n').Single(l => l.StartsWith("Name: \"cli\";", StringComparison.Ordinal));
        Assert.Contains("Flags: unchecked", task);
        Assert.Contains(@"ExpandConstant('{app}\cli')", script);
    }

    [Fact]
    public void Korean_setup_texts_use_particles_that_fit_the_name()
    {
        var script = Script;
        Assert.DoesNotMatch(LocalizationTests.WrongParticle(), script);
        Assert.Contains("탐색기 메뉴에 빙글링을 추가하는 중...", script);
        Assert.DoesNotContain("Filee Plus를", script);
    }

    [Fact]
    public void Setup_removes_Filee_Plus_after_ending_it_and_keeping_its_engines()
    {
        var script = Script;
        var remove = Body(script, "procedure RemoveFileePlus;");
        // Ended first (asked, then by its path), engines renamed before its uninstaller would delete them, then removed.
        var quit = remove.IndexOf("'--quit'", StringComparison.Ordinal);
        var kill = remove.IndexOf("KillProcessAt(Exe);", StringComparison.Ordinal);
        var keep = remove.IndexOf("KeepFileePlusEngines;", StringComparison.Ordinal);
        var uninstall = remove.IndexOf("'/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'", StringComparison.Ordinal);
        Assert.True(quit > 0 && kill > quit && keep > kill && uninstall > keep, remove);
        // Its uninstall entry, by its own id only; per machine first, then per user.
        var find = Body(script, "function FindFileePlus(var Uninstaller: String): Boolean;");
        Assert.Contains("RegQueryStringValue(HKLM, Key, 'UninstallString', Uninstaller)", find);
        Assert.Contains("RegQueryStringValue(HKCU, Key, 'UninstallString', Uninstaller)", find);
        Assert.Contains("FileePlusGuid + '}_is1'", find);
        // Its engines become Bingling's (the folder the app moves too), unless Bingling has its own already.
        var keepEngines = Body(script, "procedure KeepFileePlusEngines;");
        Assert.Contains(@"ExpandConstant('{localappdata}\' + FileePlusFolder)", keepEngines);
        Assert.Contains(@"ExpandConstant('{localappdata}\Bingling')", keepEngines);
        Assert.Contains("not DirExists(Target)", keepEngines);
        Assert.Contains($"FileePlusFolder = '{AppIdentity.LegacyFolder}';", script);
        // Before anything is installed, so its uninstaller can't delete the new shortcuts.
        var prepare = Body(script, "function PrepareToInstall(var NeedsRestart: Boolean): String;");
        Assert.StartsWith("RemoveFileePlus;", prepare.Trim());
    }

    [Fact]
    public void Setup_only_force_ends_a_copy_by_its_full_path()
    {
        var script = Script;
        // The original Filee has the same image name as Filee Plus (Filee.exe): taskkill /IM would end it too.
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/IM", script);
        Assert.Contains("Where-Object { $_.Path -eq ' + PsQuote(Exe) + ' }", script);
        Assert.Contains("Stop-Process -Force", script);
        Assert.Contains(@"KillProcessAt(ExpandConstant('{app}\{#AppExe}'));", script);
    }

    private const string BinglingGuid = "EB9635DC-3FFF-43EE-B49C-7BF81348CC0B";
    private const string FileePlusGuid = "A10409B5-6A21-4C67-9A0C-84B49B4629AF";
    private const string OriginalFileeGuid = "C8686E06-8327-40A2-B0F7-3DEDD61D9248";

    /// <summary>The statements between a routine's first <c>begin</c> and its closing <c>end;</c> at column 0.</summary>
    private static string Body(string script, string header)
    {
        var start = script.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, header);
        var begin = script.IndexOf("\nbegin", start, StringComparison.Ordinal);
        var end = script.IndexOf("\nend;", begin, StringComparison.Ordinal);
        return script[(begin + "\nbegin".Length)..end];
    }
}
