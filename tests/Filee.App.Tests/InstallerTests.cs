// Keeps the installer script (installer/Filee.iss) and the app in step: the options Setup starts Filee with, the
// Explorer menu package it registers and the engine folders it looks into.

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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileePlus", "engines"),
            EngineEnvironment.DownloadRoot);
        Assert.Contains(@"ExpandConstant('{localappdata}\FileePlus\engines\')", Script);
    }

    [Fact]
    public void Script_has_the_fork_identity_so_it_installs_next_to_the_original_Filee()
    {
        var script = Script;
        Assert.Contains($"#define AppName \"{AppIdentity.Name}\"", script);
        Assert.Contains("#define AppGuid \"A10409B5-6A21-4C67-9A0C-84B49B4629AF\"", script);
        Assert.Contains($"OutputBaseFilename={AppIdentity.FilePrefix}-{{#AppVersion}}-win-Setup", script);
        // The original Filee's installer id, upstream address and per-user folders must not appear anywhere.
        Assert.DoesNotContain("C8686E06-8327-40A2-B0F7-3DEDD61D9248", script);
        Assert.DoesNotContain("KnifeLemon", script);
        Assert.DoesNotContain(@"{localappdata}\Filee\", script);
        Assert.DoesNotContain(@"{localappdata}\Filee'", script);
    }

    [Fact]
    public void Setup_shows_the_name_in_the_install_language_and_clears_the_1_1_0_shortcuts()
    {
        var script = Script;
        Assert.Contains("en.ShortcutName=Bingling", script);
        Assert.Contains("ko.ShortcutName=빙글링", script);
        Assert.Contains("zhcn.ShortcutName=Bingling", script);
        Assert.Contains(@"Name: ""{autoprograms}\{cm:ShortcutName}""", script);
        Assert.Contains(@"Name: ""{autodesktop}\{cm:ShortcutName}""", script);
        // An update over 1.1.0 removes the shortcuts that carried its old name.
        Assert.Contains($@"Type: files; Name: ""{{autoprograms}}\{AppIdentity.LegacyName}.lnk""", script);
        Assert.Contains($@"Type: files; Name: ""{{autodesktop}}\{AppIdentity.LegacyName}.lnk""", script);
        // The old name appears only in those two lines and the comment above them, never in a message or a title.
        Assert.Equal(3, Regex.Matches(script, Regex.Escape(AppIdentity.LegacyName)).Count);
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
    public void Update_over_1_1_0_keeps_the_settings_folder_and_the_install_id()
    {
        // Same id: Setup updates the installed copy in place. Same folder name: settings, engines and the lock carry over.
        Assert.Equal("FileePlus", AppIdentity.Folder);
        Assert.Contains("#define AppGuid \"A10409B5-6A21-4C67-9A0C-84B49B4629AF\"", Script);
        Assert.Equal("Bingling", AppIdentity.Name);
        Assert.Equal("Bingling", AppIdentity.FilePrefix);
    }

    [Fact]
    public void Setup_only_force_ends_the_Filee_that_runs_from_its_own_install_folder()
    {
        var script = Script;
        // The original Filee has the same image name (Filee.exe): taskkill /IM would end it too.
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/IM", script);
        Assert.Contains("Where-Object { $_.Path -eq ' + PsQuote(ExpandConstant('{app}\\{#AppExe}')) + ' }", script);
        Assert.Contains("Stop-Process -Force", script);
    }
}
