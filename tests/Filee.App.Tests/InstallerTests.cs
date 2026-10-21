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
        // Ended first, engines renamed before its uninstaller would delete them, then removed; what a removal that
        // stopped half way left behind is cleaned up after it, and its command folder leaves PATH once it is gone.
        var end = remove.IndexOf("EndFileePlus(Folder);", StringComparison.Ordinal);
        var keep = remove.IndexOf("KeepFileePlusEngines;", StringComparison.Ordinal);
        var uninstall = remove.IndexOf("Uninstalled := Installed and RunFileePlusUninstaller(Uninstaller);", StringComparison.Ordinal);
        var cleanUp = remove.IndexOf("CleanUpFileePlus(Folder, Uninstalled or not FileExists(Folder + FileePlusExe));", StringComparison.Ordinal);
        var path = remove.IndexOf("RemoveFromPath(Folder + 'cli');", StringComparison.Ordinal);
        Assert.True(end > 0 && keep > end && uninstall > keep && cleanUp > uninstall && path > cleanUp, remove);
        Assert.Contains("if not FileExists(Folder + 'cli\\filee.cmd') then", remove);
        // Asked to quit (it saves), then ended by its path; its command line too.
        var ending = Body(script, "procedure EndFileePlus(const Folder: String);");
        Assert.True(ending.IndexOf("'--quit'", StringComparison.Ordinal) < ending.IndexOf("KillProcessAt(Folder + FileePlusExe);", StringComparison.Ordinal));
        Assert.Contains("KillProcessAt(Folder + FileePlusCliExe);", ending);
        Assert.Contains("'/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'", Body(script, "function RunFileePlusUninstaller(const Uninstaller: String): Boolean;"));
        // Its uninstall entry, by its own id only; per machine first, then per user.
        var find = Body(script, "function FindFileePlus(var Uninstaller: String): Boolean;");
        Assert.Contains("RegQueryStringValue(HKLM, FileePlusUninstallKey, 'UninstallString', Uninstaller)", find);
        Assert.Contains("RegQueryStringValue(HKCU, FileePlusUninstallKey, 'UninstallString', Uninstaller)", find);
        Assert.Contains("FileePlusGuid + '}_is1'", Body(script, "function FileePlusUninstallKey: String;"));
        // Its engines become Bingling's (the folder the app moves too); only the engines when Bingling has that folder.
        var keepEngines = Body(script, "procedure KeepFileePlusEngines;");
        Assert.Contains(@"ExpandConstant('{localappdata}\' + FileePlusFolder)", keepEngines);
        Assert.Contains(@"ExpandConstant('{localappdata}\Bingling')", keepEngines);
        Assert.Contains(@"Source := Source + '\engines';", keepEngines);
        Assert.Contains("not DirExists(Target)", keepEngines);
        Assert.Contains($"FileePlusFolder = '{AppIdentity.LegacyFolder}';", script);
        // After Bingling's files are in place, so cancelling Setup leaves Filee Plus as it was; before the Explorer menu,
        // PATH and the first start of Bingling, which moves the settings over.
        var post = script[script.IndexOf("ssPostInstall:", StringComparison.Ordinal)..];
        Assert.True(post.IndexOf("RemoveFileePlus;", StringComparison.Ordinal) < post.IndexOf("UpdateExplorerMenu;", StringComparison.Ordinal));
        Assert.True(post.IndexOf("RemoveFileePlus;", StringComparison.Ordinal) < post.IndexOf("StartBingling;", StringComparison.Ordinal));
        Assert.DoesNotContain("RemoveFileePlus;", Body(script, "function PrepareToInstall(var NeedsRestart: Boolean): String;"));
    }

    [Fact]
    public void Setup_cleans_up_a_Filee_Plus_whose_removal_stopped_half_way()
    {
        var script = Script;
        // Without an uninstall entry, its default folder, startup entry or menu package still show it is there.
        var remove = Body(script, "procedure RemoveFileePlus;");
        Assert.Contains(@"Result := AddBackslash(ExpandConstant('{autopf}\') + FileePlusName);",
            Body(script, "function FileePlusDir(var Uninstaller: String): String;"));
        Assert.Contains("Folder := FileePlusDir(Uninstaller);", remove);
        Assert.Contains("if not Installed and not HasFileePlusLeftovers(Folder) then", remove);
        var traces = Body(script, "function HasFileePlusLeftovers(const Folder: String): Boolean;");
        Assert.Contains("DirExists(RemoveBackslash(Folder))", traces);
        Assert.Contains("RegValueExists(HKCU, RunKey, FileePlusName)", traces);
        Assert.Contains("IsPackageRegistered(FileePlusMenuFamily)", traces);
        // Removed the way its uninstaller would, by the names of Filee Plus only.
        var cleanUp = Body(script, "procedure CleanUpFileePlus(const Folder: String; FilesGone: Boolean);");
        Assert.Contains("RunPowerShell('Get-AppxPackage -Name ' + PsQuote(FileePlusMenuName) + ' | Remove-AppxPackage', Error);", cleanUp);
        Assert.Contains("RegDeleteValue(HKCU, RunKey, FileePlusName);", cleanUp);
        Assert.Contains(@"RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\*\shell\' + FileePlusFolder);", cleanUp);
        Assert.Contains("RegDeleteKeyIncludingSubkeys(HKLM, FileePlusUninstallKey);", cleanUp);
        Assert.Contains($"FileePlusName = '{AppIdentity.LegacyName}';", script);
        Assert.Contains("FileePlusMenuName = 'FileePlus.ExplorerMenu';", script);
        Assert.Contains("FileePlusMenuFamily = 'FileePlus.ExplorerMenu_c73vxh346rtay';", script);
        Assert.Contains(@"RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';", script);
        // A failed uninstaller in a folder of another name: Filee Plus stays whole, nothing of it is removed.
        var keepWhole = cleanUp.IndexOf("CompareText(ExtractFileName(RemoveBackslash(Folder)), FileePlusName) <> 0", StringComparison.Ordinal);
        Assert.True(keepWhole >= 0 && keepWhole < cleanUp.IndexOf("Remove-AppxPackage", StringComparison.Ordinal), cleanUp);
        Assert.Contains("if not FilesGone and DirExists(RemoveBackslash(Folder))", cleanUp);
        // After its uninstaller ran, or once Filee.exe is gone, what is left in its folder is not its own: only an empty
        // folder goes (also when Setup runs again later). Otherwise
        // only the files and folders it installed go, the uninstall entry with the folder.
        Assert.Contains("if FilesGone then\r\n    Gone := RemoveDir(RemoveBackslash(Folder)) or not DirExists(RemoveBackslash(Folder))", cleanUp.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Contains("Gone := DeleteFileePlusFiles(RemoveBackslash(Folder));", cleanUp);
        Assert.True(cleanUp.IndexOf("if Gone then", StringComparison.Ordinal) < cleanUp.IndexOf("RegDeleteKeyIncludingSubkeys(HKLM, FileePlusUninstallKey);", StringComparison.Ordinal));
        Assert.DoesNotContain("DelTree(Folder, True, True, True)", script);
        var own = Body(script, "function IsFileePlusFile(const Name: String): Boolean;");
        Assert.Contains("';.exe;.dll;.json;.pdb;.xml;.msix;'", own);
        Assert.Contains("Pos('unins', Lowercase(Name)) = 1", own);
        var files = Body(script, "function DeleteFileePlusFilesIn(const Folder: String; var Pending: Integer): Boolean;");
        Assert.Contains("else if not IsFileePlusFile(Find.Name) then\r\n        Result := False", files.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Contains("RestartReplace(Path, '');", files);
        var folder = Body(script, "function DeleteFileePlusFiles(const Folder: String): Boolean;");
        Assert.Contains("DelTree(Sub, True, True, True)", folder);
        Assert.Contains("CompareText(Find.Name, 'engines') = 0", folder);
        Assert.Contains(@"DeleteFile(Sub + '\filee.cmd');", folder);
        Assert.Contains("Log('Kept ' + Folder + ': it holds files Filee Plus did not install');", folder);
        // The folder goes at the next restart only when nothing but its own files in use is left there.
        Assert.Contains("else if Pending = 0 then", folder);
        // The original Filee's package, startup entry, right-click key and folder are never named.
        Assert.DoesNotContain("'Filee.ExplorerMenu", script);
        Assert.DoesNotContain("Filee.ExplorerMenu_", script);
        Assert.DoesNotContain(@"\shell\Filee'", script);
        Assert.DoesNotContain(@"{autopf}\Filee'", script);
        Assert.DoesNotContain(@"{autopf}\Filee\", script);
        Assert.DoesNotContain("RunKey, 'Filee'", script);
    }

    [Fact]
    public void Bingling_is_never_installed_into_the_folder_of_Filee_Plus_that_Setup_removes()
    {
        var script = Script;
        // The folder page refuses that folder and any folder inside it, and says why.
        var next = Body(script, "function NextButtonClick(CurPageID: Integer): Boolean;");
        Assert.Contains("if CurPageID <> wpSelectDir then", next);
        Assert.Contains("if IsSameOrInside(WizardDirValue, Folder) then", next);
        Assert.Contains("MsgBox(FmtMessage(CustomMessage('DirFormerVersion'), [RemoveBackslash(Folder)]), mbError, MB_OK);", next);
        Assert.Contains("Result := False;", next);
        Assert.Contains("Folder := FileePlusDir(Uninstaller);", next);
        foreach (var language in new[] { "en", "ko", "zhcn" })
            Assert.Matches($@"(?m)^{language}\.DirFormerVersion=.*%1", script);
        Assert.Contains("Pos(Uppercase(AddBackslash(Folder)), Uppercase(AddBackslash(Path))) = 1",
            Body(script, "function IsSameOrInside(const Path, Folder: String): Boolean;"));
        // A folder given on the command line (/DIR=) is caught before anything of Filee Plus is touched.
        var remove = Body(script, "procedure RemoveFileePlus;");
        var guard = remove.IndexOf("if IsSameOrInside(ExpandConstant('{app}'), Folder) then", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < remove.IndexOf("EndFileePlus(Folder);", StringComparison.Ordinal), remove);
    }

    [Fact]
    public void A_command_folder_leaves_PATH_by_the_same_routine_for_Bingling_and_Filee_Plus()
    {
        var script = Script;
        Assert.Contains("procedure RemoveFromPath(const Folder: String);", script);
        Assert.Contains("RemoveFromPath(CliFolder);", Body(script, "procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);"));
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
