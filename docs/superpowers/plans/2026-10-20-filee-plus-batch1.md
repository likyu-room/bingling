# Filee Plus 묶음 1 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 원본 Filee와 같이 깔려도 부딪히지 않는 "Filee Plus"를 만들고, Shift + 드래그, 도구 칸(자르기·압축·메타데이터 지우기), 자르기 창, 피치 글래스 테마, 가운데 파일 크기를 넣는다. 사용자 추가 요청으로 디자인도 올린다: 앱 전체 피치 글래스, 도넛 칸 아이콘, 움직임 다듬기, 알림 카드 새 디자인(Task 10~13).

**Architecture:** 도구는 기존 변환 경로를 그대로 탄다. Tool 프리셋은 창에서 받은 `ImageEdit`를 프리셋 복사본에 넣어 `ConversionService.Start`로 보내고, 실제 처리는 `ImageEncoder.ApplyOptions` 한 곳에서 한다. 자르기를 "회전처럼 이미지를 바꾸는 옵션"(`TurnsOrMirrors`)으로 취급해서, 기존 경로 선택·EXIF 미리보기 삭제·JPEG 품질 상한 규칙을 그대로 물려받는다. 유리 효과는 창을 띄우기 직전에 찍은 화면을 흐리게 해서 `DonutMenu`가 칸 모양대로 오려 그린다.

**Tech Stack:** C# / .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Magick.NET, SkiaSharp 3.119(이미 있음), xunit, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-10-19-filee-plus-design.md` (묶음 1 = 1절 표의 0·1·2·3·7번)

## Global Constraints

- 새 외부 라이브러리 금지. SkiaSharp, Magick.NET은 이미 있다.
- 주석은 영어. 파일 맨 위에 목적 주석, public 멤버에 `///` 문서 (CONTRIBUTING.md).
- UI 문자열은 코드에 쓰지 않는다. 새 키는 `src/Filee.App/Assets/i18n/{en,ko,es,hi,pl,zh-CN}.json` 6개에 모두 넣는다(`LocalizationResourceTests`가 검사). es/hi/pl/zh-CN은 번역해서 넣는다.
- `Filee.Core`에는 UI·OS 코드 금지.
- 프로젝트·네임스페이스 이름 `Filee.*`와 실행 파일 이름 `Filee.exe`는 그대로 둔다.
- 표시 이름 "Filee Plus", 폴더 이름 `FileePlus`, 테마 이름 "피치 글래스 / Peach Glass". 본뜬 맥 앱의 이름은 코드·UI·문서 어디에도 쓰지 않는다.
- 새 코드는 되도록 새 파일에. 기존 파일은 스펙 1절의 연결 지점만 고친다.
- 설정 스키마 번호(`AppSettings.CurrentSchemaVersion = 22`)는 올리지 않는다. 새 설정 값은 모두 기본값을 가진다.
- 커밋 전마다 `dotnet format --verify-no-changes`와 해당 테스트 프로젝트를 돌린다. 커밋 메시지 끝에 세션의 Co-Authored-By / Claude-Session 줄.
- 창을 드롭 처리 안에서 열지 않는다(탐색기가 멈춘다). 항상 `Dispatcher.UIThread.Post`.
- `RadialWindow.OnDragOver`는 계속 COPY만 답한다(Shift = 이동이지만 원본을 지킨다). 이 동작은 건드리지 않는다.

## 스펙과 다르게 정한 것 (리뷰 때 확인)

1. **도구 링은 묶음 1에서 3칸**(자르기, 압축, 메타데이터 지우기)이다. 배경 추가·가리기·주석 프리셋은 묶음 2에서 만들면서 링에 넣는다. 미완성 칸을 보여 주지 않기 위해서다.
2. **자르기는 Magick `Crop`으로 한다.** SkiaSharp `ImageEditRenderer`는 그릴 것(가리기·주석·배경)이 생기는 묶음 2에서 만든다. 자르기 미리보기는 그림 위에 상자만 그리므로 "미리보기 = 결과"는 같은 `CropMath`(Core)를 써서 지킨다.
3. **피치 글래스의 강조색은 밝게·어둡게 모두 `#FF7A1A` 하나**다(어둡게용 `#FF8A3D`는 눈으로 맞출 때 다시 본다). 칸 색은 스펙 표대로 밝게/어둡게 따로 둔다.
4. **가운데 크기 표시는 새 i18n 키 없이** 기존 제목 뒤에 `" · 1.2 MB"`를 붙인다. 구분점과 단위(KB/MB/GB)는 번역이 필요 없다. 크기를 못 읽으면(파일 없음) 붙이지 않는다.
5. **여러 프레임 이미지**(움직이는 GIF, 여러 쪽 TIFF/WEBP)는 Magick `Ping`으로 프레임 수를 확인해서 도구 칸을 끈다(스펙대로). 확인은 도넛을 채울 때(`RadialViewModel.Load`) 한 번만 한다.

## Review Focus

1. **Shift를 누르고 마우스를 가만히 있을 때.** 지금 `DetectLateModifier`는 마우스가 움직여야만 불린다. 본뜬 맥 앱의 사용자는 끌다가 멈추고 Shift를 누른다. → Task 3에 키 입력으로도 열리는 테스트.
2. **EXIF로 돌아간 휴대폰 사진 자르기.** 상자는 화면에 보이는 그림 기준인데 픽셀은 돌아가 있다. → Task 5에 방향 6 JPG 자르기 테스트.
3. **크기가 다른 여러 파일을 고정 비율로 자르기.** 다른 파일에서 상자가 그림 밖으로 나가면 안 된다. → Task 4 `CropMath.Refit` 테스트.
4. **압축했는데 더 커지는 경우.** 품질 70으로 저장된 JPG를 "품질 80"으로 다시 저장하면 커진다. → Task 5에 원본 품질 상한 테스트.
5. **유리 효과에서 화면 찍기가 실패하거나 창이 이미 떠 있을 때.** 검은 도넛이 나오면 안 된다. → Task 9에 `null` 캡처·검은 캡처 단색 대체 테스트.

---

### Task 1: 빌드 준비와 기준선

**Files:** 없음(확인만)

- [ ] **Step 1:** `dotnet --list-sdks`에 `10.0.1xx`가 있는지 확인. 없으면 `winget install --id Microsoft.DotNet.SDK.10 -e`.
- [ ] **Step 2:** `pwsh build/fetch-engines.ps1 -Only rhwp,pandoc` (엔진 테스트용, 실패해도 진행. 엔진 없는 테스트는 건너뛴다).
- [ ] **Step 3:** `dotnet build Filee.slnx -c Debug` → 경고 외 오류 0.
- [ ] **Step 4:** `dotnet test Filee.slnx` → 결과(통과/실패/건너뜀 수)를 작업 메모(work_state)에 적는다. 저장소에는 파일로 남기지 않는다. 이미 실패하는 테스트가 있으면 이름을 적어 두고, 이후 작업에서 새로 깨진 것과 구분한다.
- [ ] **Step 5:** `dotnet format --verify-no-changes` 통과 확인.

---

### Task 2: 포크 정체성

**Files:**
- Create: `src/Filee.Core/AppIdentity.cs`
- Modify: `Directory.Build.props:17-24`, `src/Filee.Core/Settings/UserDataStore.cs:44`, `src/Filee.Engines/Infrastructure/EngineEnvironment.cs:30`, `src/Filee.Core/Conversion/JobQueue.cs:81`, `src/Filee.App/Services/SingleInstance.cs:40`, `src/Filee.App/Services/TrayService.cs:37`, `src/Filee.Platform.Windows/WindowsPlatformServices.cs:16-17,250`, `src/Filee.Platform.Windows/Elevation.cs:121`, `src/Filee.Platform.Windows/ExplorerMenuPackage.cs` (이름·ClassId·표시 이름), `src/Filee.ExplorerMenu/FileeExplorerMenu.c:18`(CLSID), `src/Filee.App/Services/UpdateService.cs:69-84`, About 페이지(`Views/Pages/AboutPage.axaml` + ViewModel), `installer/Filee.iss:25-58`, `build/build-installer.ps1:132-150`, `.github/workflows/release.yml:102`, `README.md`/`README.ko.md` 맨 위
- Test: 기존 `tests/Filee.Core.Tests/DataDirectoryTests.cs`, `tests/Filee.App.Tests/{SingleInstanceTests,UpdateServiceTests,InstallerTests,SystemIntegrationTests}.cs` 의 고정값 갱신 + 새 테스트

**Interfaces:**
- Produces: `Filee.Core.AppIdentity` — `public const string Name = "Filee Plus"; public const string Folder = "FileePlus"; public const string BasedOn = "Filee 1.12.0";`

**값 (그대로 쓴다)**

| 항목 | 새 값 |
|---|---|
| `Directory.Build.props` `Version` / `Product` | `1.0.0` / `Filee Plus` |
| `FileeRepositoryUrl` | 빈 값 `""` (포크 생기면 채움) |
| 설정 폴더 / 엔진 폴더 / 임시 폴더 | `%APPDATA%\FileePlus`, `%LocalAppData%\FileePlus\engines`, `%TEMP%\FileePlus\<job>` |
| 뮤텍스 이름 접두사 | `FileePlus-` |
| 시작 프로그램 값 이름, 트레이 툴팁, 메시지 상자 제목 | `AppIdentity.Name` |
| 오른쪽 메뉴 키 | `Software\Classes\*\shell\FileePlus` |
| 보내기 바로 가기 | `Filee Plus.lnk` |
| Win11 메뉴 패키지 이름 / 패밀리 이름 | `FileePlus.ExplorerMenu` / `FileePlus.ExplorerMenu_c73vxh346rtay` (해시는 Publisher에서만 나오므로 그대로) |
| Win11 메뉴 CLSID (C#·C·manifest 모두) | `{27B50CD9-598E-4AB1-8BBD-AC36085FB4DE}` |
| Inno `AppName` / `AppGuid` | `Filee Plus` / `A10409B5-6A21-4C67-9A0C-84B49B4629AF` |
| 설치 파일 / 포터블 / 체크섬 | `FileePlus-<ver>-win-Setup.exe`, `FileePlus-<ver>-win-Portable.zip`, `FileePlus-<ver>-SHA256SUMS.txt` |
| `UpdateService.WebsiteUrl` | `LatestReleaseUrl`로 대체(const 삭제하거나 `=> LatestReleaseUrl`) |

- [ ] **Step 1: 실패하는 테스트**

`tests/Filee.App.Tests/UpdateServiceTests.cs`에 추가:
```csharp
[Fact]
public void No_repository_means_no_update_check()
{
    Assert.False(UpdateService.CanCheck(""));
    Assert.True(UpdateService.CanCheck("https://github.com/someone/filee-plus"));
}
```
`tests/Filee.Core.Tests/DataDirectoryTests.cs`의 기본 폴더 기대값을 `FileePlus`로 바꾸고, `SingleInstanceTests`의 `"Filee-"` 접두사 기대값을 `"FileePlus-"`로 바꾼다.

- [ ] **Step 2:** `dotnet test tests/Filee.App.Tests --filter "UpdateService|SingleInstance"` 와 `dotnet test tests/Filee.Core.Tests --filter DataDirectory` → FAIL.
- [ ] **Step 3: 구현.** `AppIdentity` 만들고 표의 모든 곳을 바꾼다. `UpdateService`: `internal static bool CanCheck(string repositoryUrl)` (빈 값·공백이면 false)를 만들고, `RepositoryUrl`의 KnifeLemon 대체값을 지운다. 확인 타이머와 "업데이트 확인" 버튼은 `CanCheck(RepositoryUrl)`가 false면 아무것도 하지 않는다. 피드백·형식 요청 링크(`NewIssueUrl` 쓰는 곳)는 같은 조건으로 숨긴다. About 페이지에 `AppIdentity.BasedOn` 문구와 "KnifeLemon의 Filee(MIT)를 바탕으로 함" 줄을 새 i18n 키 `about.based_on`(6개 언어)로 추가. 후원 창은 건드리지 않는다. README 두 개 맨 위에 같은 출처 문장과 SmartScreen "추가 정보 → 실행" 안내.
- [ ] **Step 4:** `grep -rn '"Filee"\|\\\\Filee"\|shell\\\\Filee\|Filee-\$' src build installer .github` 로 남은 정체성 문자열이 없는지 확인(네임스페이스·`Filee.exe`·폰트 `fonts:Filee`·Linux/macOS 파일은 제외). 깨진 `InstallerTests`/`SystemIntegrationTests` 고정값을 새 값으로 갱신.
- [ ] **Step 5:** `dotnet build` 후 `dotnet test Filee.slnx` → 기준선 대비 새 실패 0. `pwsh build/build-installer.ps1 -CheckOnly` → "installer/Filee.iss compiles".
- [ ] **Step 6:** 커밋 `feat: Filee Plus identity (own data folder, installer id, menus, updater off until the fork exists)`

---

### Task 3: Shift + 드래그 기본값

**Files:**
- Modify: `src/Filee.Core/Settings/TriggerGesture.cs:77-87`, `src/Filee.App/Services/Triggers/GestureDetector.cs` (`MouseMove`, `KeyDown`), `src/Filee.App/Services/Triggers/TriggerService.cs:128`
- Test: `tests/Filee.Core.Tests/`(기존 TriggerGesture 테스트 파일이 있으면 거기, 없으면 `TriggerDefaultsTests.cs`), `tests/Filee.App.Tests/GestureDetectorTests.cs`

**Interfaces:**
- Produces: `TriggerGesture.Defaults()` Windows 결과 순서 = Shift 드래그, Ctrl 드래그, Ctrl+Alt+Space.

- [ ] **Step 1: 실패하는 테스트**
```csharp
// Core
[Fact]
public void Windows_defaults_open_the_donut_with_shift_drag_first()
{
    Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows defaults");
    var d = TriggerGesture.Defaults();
    Assert.Equal(3, d.Count);
    Assert.Equal((TriggerKind.Drag, ModifierKeys.Shift), (d[0].Kind, d[0].Modifiers));
    Assert.Equal((TriggerKind.Drag, ModifierKeys.Ctrl), (d[1].Kind, d[1].Modifiers));
    Assert.Equal(TriggerKind.KeyChord, d[2].Kind);
}

// App (GestureDetectorTests)
private static readonly TriggerGesture ShiftDrag = new() { Kind = TriggerKind.Drag, Modifiers = ModifierKeys.Shift, Button = TriggerMouseButton.Left };

[Fact]
public void Shift_pressed_after_the_drag_started_fires_on_the_next_move()
{
    var detector = Create(ShiftDrag, CtrlDrag);
    var fired = new List<ModifierKeys>();
    detector.DragStarted += (g, _, _) => fired.Add(g.Modifiers);
    detector.MouseDown(TriggerMouseButton.Left, 0, 0, ModifierKeys.None);
    detector.MouseMove(30, 0, ModifierKeys.None, TriggerMouseButton.Left);
    detector.MouseMove(31, 0, ModifierKeys.Shift, TriggerMouseButton.Left);
    Assert.Equal([ModifierKeys.Shift], fired);
}

[Fact]
public void Shift_pressed_while_the_dragging_mouse_stands_still_fires()
{
    var detector = Create(ShiftDrag);
    var at = (0, 0); var started = 0;
    detector.DragStarted += (_, x, y) => { started++; at = (x, y); };
    detector.MouseDown(TriggerMouseButton.Left, 0, 0, ModifierKeys.None);
    detector.MouseMove(30, 5, ModifierKeys.None, TriggerMouseButton.Left);
    detector.KeyDown("LeftShift", ModifierKeys.Shift);
    Assert.Equal(1, started);
    Assert.Equal((30, 5), at);
    detector.MouseMove(32, 5, ModifierKeys.Shift, TriggerMouseButton.Left);
    Assert.Equal(1, started); // once per drag
}

[Fact]
public void Shift_without_a_held_button_opens_nothing()
{
    var detector = Create(ShiftDrag);
    var started = 0;
    detector.DragStarted += (_, _, _) => started++;
    detector.MouseMove(30, 5, ModifierKeys.None);
    detector.KeyDown("LeftShift", ModifierKeys.Shift);
    Assert.Equal(0, started);
}
```
(xunit 버전에 `Assert.SkipUnless`가 없으면 프로젝트의 기존 OS별 건너뛰기 방식을 쓴다.)

- [ ] **Step 2:** 두 테스트 프로젝트를 필터로 돌려 FAIL 확인.
- [ ] **Step 3: 구현.** `Defaults()`: Windows에서만 맨 앞에 `new() { Kind = Drag, Modifiers = Shift, Button = Left }` 추가(macOS·Linux는 그대로). `GestureDetector`: `MouseMove`에서 마지막 `heldButton`을 `_lastHeldButton`에 저장(`MouseUp`에서 None으로). `KeyDown` 맨 앞에서 `_armed is null && _lastHeldButton != None`이면 `DetectLateModifier(_lastX, _lastY, modifiers, _lastHeldButton)`를 시도하고, 발동하면 `_dragActive = true` 후 `DragStarted` 발생, 키 조합 검사는 건너뛴다. `TriggerService` 키 처리: 눌린 키가 LeftShift/RightShift(LeftControl/RightControl, LeftAlt/RightAlt도)이면 해당 modifier를 `Map(mask)` 결과에 OR 한다(SharpHook 마스크가 방금 누른 modifier를 포함하지 않을 수 있어서).
- [ ] **Step 4:** 테스트 PASS, `GestureDetectorTests` 전체 PASS.
- [ ] **Step 5:** 커밋 `feat: Shift + drag opens the donut, also when Shift comes while the mouse stands still`

---

### Task 4: 프리셋 모델, 내장 도구 프리셋, 자르기 좌표 계산

**Files:**
- Create: `src/Filee.Core/Presets/ImageEdit.cs` (`ImageToolKind`, `ImageEdit`, `CropRect`, `CropMath`), `src/Filee.Core/Settings/ImageToolSettings.cs`
- Modify: `src/Filee.Core/Presets/Preset.cs` (`PresetKind`, `Preset.Tool`, `IsConversion`, `Clone`, `ImageOptions.Edit/Optimize/TurnsOrMirrors/Clone`), `src/Filee.Core/Presets/BuiltInData.cs` (`ImageToolPresets`, `CreateProfiles` images), `src/Filee.Core/Settings/AppSettings.cs` (`ImageTools`), `src/Filee.Core/Conversion/JobQueue.cs:465-471` (`Unturned`), 프리셋 고르는 곳들: `UserDataStore.cs:142`(`FindConversion`), `Filee.App/ViewModels/Pages/PresetsPageViewModel.cs:47`, `WatchFoldersPageViewModel.cs:32`, `Filee.Cli/Cli.cs:235,343`, `Filee.App/Services/WindowService.cs:191`
- Test: `tests/Filee.Core.Tests/ImageToolPresetTests.cs`, `tests/Filee.Core.Tests/CropMathTests.cs`

**Interfaces:**
- Produces:
  - `enum PresetKind { Convert, Group, Tool }` (Tool은 맨 뒤에 추가: 기존 숫자값 유지)
  - `enum ImageToolKind { Crop, AddBackground, Redact, Annotate }`
  - `Preset.Tool` : `ImageToolKind` (`LenientEnumJsonConverter`), `Clone`에 포함
  - `Preset.IsConversion => Kind == PresetKind.Convert` (`[JsonIgnore]`)
  - `sealed class CropRect { double X, Y, Width, Height; double? Aspect; Clone() }` — 방향을 바로잡은 그림 기준 0~1 비율. `Aspect`는 고정 비율(가로/세로 픽셀 비), 자유면 null
  - `sealed class ImageEdit { CropRect? Crop; Clone() }` (묶음 2에서 필드 추가)
  - `ImageOptions.Edit` : `ImageEdit?` (`Clone`에서 깊은 복사), `ImageOptions.Optimize` : `bool`
  - `ImageOptions.TurnsOrMirrors` = 기존 조건 `|| Edit?.Crop is not null` (문서 주석도 "turns, mirrors or crops"로)
  - `static class CropMath`:
    - `static double? RatioOf(string aspectId, int width, int height)` — `"free"`→null, `"original"`→width/height, `"1:1"`,`"16:9"`,`"9:16"`,`"4:3"`,`"3:4"`
    - `static readonly IReadOnlyList<string> AspectIds = ["free","original","1:1","16:9","9:16","4:3","3:4"]`
    - `static CropRect Initial(double? aspect, int width, int height)` — 비율이 있으면 그 비율의 가장 큰 가운데 상자, 없으면 전체(0,0,1,1)
    - `static CropRect Refit(CropRect crop, int width, int height)` — 스펙 3.1 "여러 파일" 규칙
    - `static (int X, int Y, int Width, int Height) ToPixels(CropRect crop, int width, int height)` — `Refit` 후 반올림, 최소 1px, 그림 안
  - `sealed class ImageToolSettings { string CropAspect = "original"; Clone() }`, `AppSettings.ImageTools` (기본 `new()`)
  - 내장 프리셋 id: `crop`, `compress-image`, `strip-metadata`, 그룹 `tools`

- [ ] **Step 1: 실패하는 테스트**
```csharp
public class ImageToolPresetTests
{
    private static Preset P(string id) => BuiltInData.CreatePresets().Single(p => p.Id == id);

    [Fact]
    public void Tools_group_fans_out_crop_compress_and_strip_metadata()
    {
        var tools = P("tools");
        Assert.Equal(PresetKind.Group, tools.Kind);
        Assert.Equal(["crop", "compress-image", "strip-metadata"], tools.SubPresetIds);
        Assert.Equal(PresetKind.Tool, P("crop").Kind);
        Assert.Equal(ImageToolKind.Crop, P("crop").Tool);
        Assert.Equal("{name} Cropped", P("crop").Output.FileNamePattern);
    }

    [Fact]
    public void Compress_and_strip_metadata_are_ordinary_same_format_presets()
    {
        var c = P("compress-image"); var s = P("strip-metadata");
        Assert.True(c.IsConversion && s.IsConversion);
        Assert.Equal((BuiltInData.SameAsSource, 80, false, true, "{name} Compressed"),
            (c.TargetFormat, c.Image.Quality, c.Image.KeepMetadata, c.Image.Optimize, c.Output.FileNamePattern));
        Assert.Equal((BuiltInData.SameAsSource, 95, false, true, "{name} Clean"),
            (s.TargetFormat, s.Image.Quality, s.Image.KeepMetadata, s.Image.Optimize, s.Output.FileNamePattern));
    }

    [Fact]
    public void The_image_donut_has_the_tools_slice_and_still_fits()
    {
        var images = BuiltInData.CreateProfiles().Single(p => p.Id == "images");
        Assert.Contains("tools", images.PresetIds);
        Assert.True(images.PresetIds.Count <= 12);
    }

    [Fact]
    public void Tool_presets_are_not_conversions_and_survive_a_round_trip()
    {
        var crop = P("crop");
        Assert.False(crop.IsConversion);
        crop.Image.Edit = new ImageEdit { Crop = new CropRect { X = .1, Y = .2, Width = .5, Height = .4, Aspect = 1 } };
        var copy = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(crop))!;   // use the store's JsonSerializerOptions if it has its own
        Assert.Equal((PresetKind.Tool, ImageToolKind.Crop, .5, 1.0), (copy.Kind, copy.Tool, copy.Image.Edit!.Crop!.Width, copy.Image.Edit.Crop.Aspect));
        Assert.NotSame(crop.Image.Edit, crop.Clone().Image.Edit);
    }

    [Fact]
    public void A_store_offers_no_tool_preset_where_a_conversion_is_picked()
    {
        // UserDataStore on a temp folder (see DataDirectoryTests for how tests create one)
        Assert.Null(store.FindConversion("crop"));
        Assert.NotNull(store.FindConversion("compress-image"));
    }
}

public class CropMathTests
{
    [Fact]
    public void Initial_box_is_the_largest_centred_box_of_the_ratio()
    {
        var r = CropMath.Initial(1.0, 400, 200);
        Assert.Equal((0.25, 0.0, 0.5, 1.0), (r.X, r.Y, r.Width, r.Height));
        Assert.Equal((0.0, 0.0, 1.0, 1.0), Tuple(CropMath.Initial(null, 400, 200)));
    }

    [Fact]
    public void Fixed_ratio_keeps_centre_and_width_and_recomputes_height_on_another_size()
    {
        var crop = new CropRect { X = .25, Y = 0, Width = .5, Height = 1, Aspect = 1 }; // square on 400×200
        var r = CropMath.Refit(crop, 200, 400);            // portrait image
        Assert.Equal(100, r.Width * 200, 3);               // 50 % of 200 px
        Assert.Equal(100, r.Height * 400, 3);              // square: 100 px high
        Assert.Equal(.5, r.Y + r.Height / 2, 3);           // same centre
    }

    [Fact]
    public void A_box_that_would_leave_the_image_is_pushed_back_inside()
    {
        var crop = new CropRect { X = .8, Y = .8, Width = .4, Height = .4, Aspect = 16.0 / 9 };
        var r = CropMath.Refit(crop, 1000, 1000);
        Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.Width <= 1 + 1e-9 && r.Y + r.Height <= 1 + 1e-9);
        Assert.Equal(16.0 / 9, r.Width * 1000 / (r.Height * 1000), 3);
    }

    [Fact]
    public void Free_boxes_keep_their_fractions_and_pixels_stay_inside()
    {
        var crop = new CropRect { X = .1, Y = .1, Width = .9, Height = .9 };
        Assert.Equal((.1, .1, .9, .9), Tuple(CropMath.Refit(crop, 300, 100)));
        var px = CropMath.ToPixels(crop, 3, 3);
        Assert.True(px.Width >= 1 && px.X + px.Width <= 3 && px.Y + px.Height <= 3);
    }

    [Theory]
    [InlineData("free", null)] [InlineData("original", 2.0)] [InlineData("16:9", 16.0 / 9)] [InlineData("3:4", 0.75)]
    public void Ratio_ids_map_to_width_over_height(string id, double? expected) =>
        Assert.Equal(expected, CropMath.RatioOf(id, 400, 200));
}
```
- [ ] **Step 2:** `dotnet test tests/Filee.Core.Tests --filter "ImageToolPreset|CropMath"` → 컴파일 실패(FAIL).
- [ ] **Step 3: 구현.**
  - 위 Interfaces 그대로. `Refit` 알고리즘: Aspect null이면 그대로(값만 0~1로 자름). 있으면 `cx = X+W/2, cy = Y+H/2, w = W`, `h = w*width/(Aspect*height)`; `h > 1`이면 `h = 1, w = Aspect*height/width`; `w > 1`이면 `w = 1`로 다시 계산; `X = clamp(cx-w/2, 0, 1-w)`, `Y = clamp(cy-h/2, 0, 1-h)`.
  - 내장 프리셋은 `ImageToolPresets()` 끝에 추가. `crop`: `Kind = Tool, Tool = Crop, TargetFormat = SameAsSource, Image = { Quality = 95 }, Output = "{name} Cropped"`, `NameKey = "preset.crop"`. `compress-image`/`strip-metadata`: 기존 `Tool(...)` 도우미를 쓰되 `compress-image`는 품질을 80으로 덮어쓴다. 둘 다 `KeepMetadata = false, Optimize = true`. NameKey `preset.compress_image`, `preset.strip_metadata`. 그룹 `tools`: `NameKey = "preset.group_tools"`. 링에서 짧은 이름 키 `*.short`도 기존 회전 프리셋처럼 만든다(`loc.RingName`이 쓰는 형식을 따른다).
  - 이미지 프로필: `"half-size"` 앞에 `"tools"` 를 넣는다(11칸).
  - 프리셋 고르는 곳의 `!p.IsGroup` → `p.IsConversion` (위 Files 목록). 그룹의 자식 후보(`RadialViewModel:112`, `UserDataStore:188`, `GroupEditorViewModel:33,140`)는 Tool도 허용해야 하므로 `!IsGroup` 그대로 둔다.
  - `ConversionService.Start` 는 `preset.IsGroup || (preset.Kind == PresetKind.Tool && preset.Image.Edit is null)`이면 null.
  - `JobQueue.Unturned`에서 `copy.Image.Edit = null;`도 한다(경로 중 한 단계만 자르게).
  - `WindowService.EditPresetAsync`: Tool 프리셋은 편집 창 없이 `false`를 돌려준다(도넛 우클릭 편집에서).
  - i18n 6개 파일: `preset.crop`, `preset.crop.short`, `preset.compress_image`, `preset.compress_image.short`, `preset.strip_metadata`, `preset.strip_metadata.short`, `preset.group_tools` (ko: 자르기/자르기, 압축/압축, 메타데이터 지우기/메타데이터, 도구).
- [ ] **Step 4:** Core 테스트 PASS. `dotnet test Filee.slnx` → 기존 테스트 중 이미지 프로필 칸 수를 고정한 것(`RenderTests.Donut_toolbar_renders_for_images`의 `10`)은 Task 7에서 고치므로 여기서 실패하면 11로 갱신.
- [ ] **Step 5:** 커밋 `feat: tool presets (crop, compress, strip metadata) and crop geometry`

---

### Task 5: 엔진 — 자르기와 최적화 저장

**Files:**
- Modify: `src/Filee.Engines/Magick/ImageEncoder.cs` (`ApplyOptions` :223-320)
- Test: `tests/Filee.Engines.Tests/ImageToolTests.cs` (기존 파일에 추가, `fx.ConvertAsync` 사용)

**Interfaces:**
- Consumes: `CropMath.ToPixels`, `ImageOptions.Edit`, `ImageOptions.Optimize` (Task 4)

- [ ] **Step 1: 실패하는 테스트** (`Marked()`와 `IsRed()`는 파일에 이미 있다)
```csharp
private static Preset Crop(double x, double y, double w, double h, double? aspect = null)
{
    var p = Tool("crop").Clone();
    p.Image.Edit = new ImageEdit { Crop = new CropRect { X = x, Y = y, Width = w, Height = h, Aspect = aspect } };
    return p;
}

[Fact]
public async Task Crop_keeps_the_box_and_the_format()
{
    // 40×20 PNG, red 10×10 top-left. Box = left quarter, full height → 10×20, red at (0,0).
    var job = await fx.ConvertAsync([Write(Marked(), "a.png")], Crop(0, 0, .25, 1));
    Assert.Equal(JobState.Completed, job.State);
    Assert.Equal("a Cropped.png", Path.GetFileName(job.Outputs.Single()));
    using var r = new MagickImage(job.Outputs.Single());
    Assert.Equal((10u, 20u), (r.Width, r.Height));
    Assert.True(IsRed(r, 0, 0));
    Assert.Equal((0, 0), (r.Page.X, r.Page.Y)); // no leftover canvas offset
}

[Fact]
public async Task Crop_of_a_turned_phone_photo_cuts_what_the_user_sees()
{
    // Stored 40×20 with orientation 6 (shown 20×40, red top-right). Box = top half as seen → 20×20 with red at the top right.
    var job = await fx.ConvertAsync([WritePhone()], Crop(0, 0, 1, .5));
    using var r = new MagickImage(job.Outputs.Single());
    Assert.Equal((20u, 20u), (r.Width, r.Height));
    Assert.True(IsRed(r, 18, 1));
    Assert.Contains(r.Orientation, new[] { OrientationType.TopLeft, OrientationType.Undefined });
}

[Fact]
public async Task Compress_never_raises_jpeg_quality_and_keeps_the_photo_upright()
{
    var source = WritePhone(quality: 70);              // orientation 6, quality 70, EXIF present
    var job = await fx.ConvertAsync([source], Tool("compress-image"));
    var output = job.Outputs.Single();
    Assert.Equal("phone Compressed.jpg", Path.GetFileName(output));
    using var r = new MagickImage(output);
    Assert.True(r.Quality <= 70);
    Assert.Equal((20u, 40u), (r.Width, r.Height));     // turned upright
    Assert.Null(r.GetExifProfile());
}

[Fact]
public async Task Strip_metadata_removes_exif_and_keeps_orientation_visually()
{
    var job = await fx.ConvertAsync([WritePhone()], Tool("strip-metadata"));
    using var r = new MagickImage(job.Outputs.Single());
    Assert.Null(r.GetExifProfile());
    Assert.Equal((20u, 40u), (r.Width, r.Height));
    Assert.True(IsRed(r, 18, 1));
}

[Fact]
public async Task Compressing_a_png_stays_lossless_and_not_larger()
{
    var source = Write(Noise(256, 256), "n.png", compressionLevel: 1); // little compression on purpose
    var job = await fx.ConvertAsync([source], Tool("compress-image"));
    var output = job.Outputs.Single();
    Assert.True(new FileInfo(output).Length <= new FileInfo(source).Length);
    using var a = new MagickImage(source); using var b = new MagickImage(output);
    Assert.Equal(0, a.Compare(b, ErrorMetric.Absolute));
}
```
`Write`, `WritePhone(quality = 90)`, `Noise` 도우미는 이 파일의 기존 "phone" 테스트 준비 코드를 함수로 빼서 만든다(EXIF 방향 6, 빨간 사각형 위치는 기존 테스트와 같게).

- [ ] **Step 2:** `dotnet test tests/Filee.Engines.Tests --filter ImageToolTests` → FAIL.
- [ ] **Step 3: 구현** (`ApplyOptions`)
  - `Turn(image, options)` 바로 다음, EXIF 미리보기 삭제 앞에: `options.Edit?.Crop`이 있으면 `CropMath.ToPixels(crop, (int)image.Width, (int)image.Height)`로 `image.Crop(new MagickGeometry(x, y, (uint)w, (uint)h))` 후 `image.ResetPage()`. (Task 4에서 `TurnsOrMirrors`가 자르기를 포함하므로 `turned`가 true가 되어 EXIF 미리보기도 지워진다.)
  - JPEG 품질 상한 조건 `options.TurnsOrMirrors && ...` 를 `(options.TurnsOrMirrors || options.Optimize) && ...` 로.
  - `switch (targetFormat)`에 `case "png" when options.Optimize: image.Settings.SetDefine(MagickFormat.Png, "compression-level", 9);` (필터는 Magick 기본 적응형). `"webp"`에서 `Optimize`이고 원본이 WebP면 품질 상한도 JPEG처럼 적용.
- [ ] **Step 4:** 테스트 PASS. `ImageToolTests` 전체와 `ImageFormatTests` PASS(기존 동작 그대로).
- [ ] **Step 5:** 커밋 `feat: crop and optimised re-encoding in the image engine`

---

### Task 6: 도넛 — 도구 칸, 도구 쓸 수 있는 파일, 가운데 파일 크기, Tool 실행 연결

**Files:**
- Create: `src/Filee.App/Services/ImageToolSupport.cs`, `src/Filee.Core/Formats/ByteSize.cs`
- Modify: `src/Filee.App/Controls/DonutMenu.cs` (`DonutItem.IsAccent`, `ToolBrush` 속성, 칸 그리기), `src/Filee.App/Controls/DonutMenu.Groups.cs`(하위 링 칸도 같은 강조), `src/Filee.App/ViewModels/RadialViewModel.cs` (`Load`), `src/Filee.App/Services/RadialController.cs` (`Convert`), `src/Filee.App/Services/WindowService.cs` (`EditImageAsync` 자리만, 본문은 Task 7), `src/Filee.App/Styles/Soft.axaml:209-219`
- Test: `tests/Filee.Core.Tests/ByteSizeTests.cs`, `tests/Filee.App.Tests/ImageToolDonutTests.cs`, `tests/Filee.App.Tests/RenderTests.cs:463-478` 갱신

**Interfaces:**
- Consumes: `Preset.Kind == PresetKind.Tool`, `Preset.Tool` (Task 4)
- Produces:
  - `ByteSize.Format(long bytes)` → 1024 단위, `"512 B"`, `"1.2 KB"`, `"1.2 MB"`, `"4.0 GB"` (소수 한 자리, `CultureInfo.InvariantCulture`)
  - `DonutItem.IsAccent` (`bool`, init)
  - `DonutMenu.ToolBrushProperty` (`IBrush?`, 기본 `null` = `AccentBrush` 사용)
  - `ImageToolSupport.Reason(IReadOnlyList<string> files, ILocalizer loc) : string?` — null이면 도구 사용 가능
  - `WindowService.EditImageAsync(ImageToolKind tool, IReadOnlyList<string> files, PixelPoint cursor) : Task<ImageEdit?>` (Task 7이 구현)

- [ ] **Step 1: 실패하는 테스트**
```csharp
// Core
[Theory]
[InlineData(0, "0 B")] [InlineData(512, "512 B")] [InlineData(1258291, "1.2 MB")] [InlineData(4509715660, "4.2 GB")]
public void Sizes_read_like_explorer(long bytes, string text) => Assert.Equal(text, ByteSize.Format(bytes));

// App (headless, TestServices / AppHost like RenderTests)
[AvaloniaFact]
public void Image_donut_shows_the_tools_slice_as_an_accent_group_with_three_tools()
{
    TestServices.EnsureInitialized("ko");
    var files = TestImages.Png(2);                      // real 8×8 PNGs in a temp folder
    var vm = AppHost.Get<RadialViewModel>();
    vm.Load(files, clickMode: true);
    var tools = vm.Items.Single(i => i.Id == "tools");
    Assert.True(tools.IsAccent && tools.IsEnabled);
    Assert.Equal(3, tools.Children!.Count);
    Assert.Matches(@"^PNG 2개 · \d+ B$", vm.CenterTitle);
}

[AvaloniaFact]
public void Tools_are_off_for_an_animated_gif_and_for_mixed_files()
{
    TestServices.EnsureInitialized("ko");
    var vm = AppHost.Get<RadialViewModel>();
    vm.Load([TestImages.AnimatedGif()], clickMode: true);
    var crop = vm.Items.Single(i => i.Id == "tools").Children!.Single(c => c.Id == "crop");
    Assert.False(crop.IsEnabled);
    Assert.False(string.IsNullOrEmpty(crop.DisabledReason));
    vm.Load([TestImages.Png(1)[0], TestImages.Text()], clickMode: true);
    // mixed: the fallback profile has no tools slice, or its crop is disabled
    Assert.DoesNotContain(vm.Items.SelectMany(i => i.Children ?? []), c => c.Id == "crop" && c.IsEnabled);
}

[AvaloniaFact]
public void Compress_stays_available_for_an_animated_gif()
{
    TestServices.EnsureInitialized("ko");
    var vm = AppHost.Get<RadialViewModel>();
    vm.Load([TestImages.AnimatedGif()], clickMode: true);
    Assert.True(vm.Items.Single(i => i.Id == "tools").Children!.Single(c => c.Id == "compress-image").IsEnabled);
}
```
`TestImages`는 이 테스트 파일 안의 작은 정적 도우미(Magick.NET으로 임시 폴더에 쓴다). `RenderTests.Donut_toolbar_renders_for_images`는 기대값을 `11`칸, 없는 파일이라 크기 없이 `"JPG 3개"` 그대로.

- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현**
  - `ImageToolSupport.Reason`: 모든 파일이 `FormatRegistry.Detect(path)?.Category == Image`이고 벡터가 아니고(`svg`, `svgz`, `eps`, `ai`, `emf`, `wmf` 등은 `ImageEncoder.Writable`/Magick 읽기 목록 기준으로 판단, 기준은 "MagickImageConverter가 그 형식을 읽어 같은 형식으로 쓴다"), `new MagickImageCollection().Ping(path)` 프레임 수가 1이어야 한다. 아니면 새 키 `donut.tool_single_images`("한 장짜리 사진만 편집할 수 있어요") 반환. Ping 예외는 사용 불가로 처리.
  - `RadialViewModel.Load`: `ItemOf`에서 `p.Kind == PresetKind.Tool`이면 `reason = toolReason ??= ImageToolSupport.Reason(files, loc)`(Load마다 한 번 계산; 파일이 들어오기 전 `ResetForDrag`에서는 계산하지 않는다). Tool·Group 칸의 Caption은 기존 그룹 규칙 그대로. 그룹 `tools`(자식 중 Tool이 하나라도 있는 그룹)는 `IsAccent = true`. 가운데 제목: 기존 문자열 뒤에 `" · " + ByteSize.Format(total)` — `total`은 `File.Exists`인 파일 길이 합(폴더는 건너뜀), 합이 0이면 붙이지 않는다.
  - `DonutMenu`: 칸 그리기에서 `item.IsAccent`이면 바탕 위에 `WithOpacity(ToolBrush ?? AccentBrush, 0.30)`을 한 겹 더 그린다(일반 칸·하위 링 모두). `ToolBrushProperty`를 `AffectsRender` 목록에 넣는다. `Soft.axaml` 기본 스타일은 `ToolBrush`를 설정하지 않는다(Task 8에서 피치 스타일만 설정).
  - `RadialController.Convert`: `preset.Kind == PresetKind.Tool`이면
    ```csharp
    var cursor = /* window centre in screen pixels: _window.Position + side/2 */;
    _converted = true;
    Close(animate: false);
    Dispatcher.UIThread.Post(async () =>
    {
        if (await windows.EditImageAsync(preset.Tool, files, cursor) is not { } edit) return;
        var run = preset.Clone();
        run.Image.Edit = edit;
        conversions.Start(files, run);          // shown in the toast
    });
    return;
    ```
    `files`는 람다 전에 지역 변수로 복사한다(`viewModel.Files`가 바뀔 수 있다).
  - `WindowService.EditImageAsync`는 이 Task에서 `Task.FromResult<ImageEdit?>(null)`만 돌려주는 자리로 둔다.
  - i18n 6개: `donut.tool_single_images`.
- [ ] **Step 4:** 테스트 PASS, `RenderTests`·`DonutGroupTests`·`RadialControllerTests` PASS. 스크린샷 `donut-images-ko.png`에서 도구 칸 색이 다른지 눈으로 확인.
- [ ] **Step 5:** 커밋 `feat: tools slice on the image donut, file size in the centre`

---

### Task 7: 자르기 창

**Files:**
- Create: `src/Filee.App/Views/ImageToolWindow.axaml(.cs)`, `src/Filee.App/ViewModels/ImageToolViewModel.cs`, `src/Filee.App/Views/Tools/CropPanel.axaml(.cs)`, `src/Filee.App/Controls/ImageCanvas.cs` (그림 + 자르기 상자 그리기·끌기)
- Modify: `src/Filee.App/Services/WindowService.cs` (`EditImageAsync` 구현)
- Test: `tests/Filee.App.Tests/ImageToolWindowTests.cs`, `RenderTests`에 스크린샷 1개

**Interfaces:**
- Consumes: `CropMath.*`, `CropRect`, `ImageEdit`, `ImageToolSettings.CropAspect`, `UserDataStore.SaveSettings()`
- Produces:
  - `ImageToolViewModel(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc)`
    - `int Index`, `int Count`, `string Title`("자르기 · 사진.jpg"), `string PositionText`("3개 중 1"), `string FooterText`("3개 파일에 함께 적용돼요", 1개면 빈 문자열)
    - `IReadOnlyList<Choice<string>> Aspects` (`CropMath.AspectIds` 순서), `Choice<string> Aspect`
    - `CropRect Crop` (보고 있는 파일 기준으로 `CropMath.Refit`된 값), `int PixelWidth`, `int PixelHeight` (편집 가능, 바꾸면 `Crop` 갱신)
    - `Bitmap? Preview` (방향을 바로잡고 화면 크기로 줄인 그림, `MagickImage` → PNG 메모리 → Avalonia `Bitmap`)
    - `IRelayCommand Next`, `Previous`, `Reset`, `Save`, `Cancel`, `Undo`, `Redo`
    - `ImageEdit? Result` (Save 때 `new ImageEdit { Crop = Crop.Clone() }`, Cancel은 null)
  - `ImageCanvas` : `Control` — `Source`(Bitmap), `Crop`(CropRect, TwoWay), `Aspect`(double?), `Zoom`/`Pan` 내부 상태

- [ ] **Step 1: 실패하는 테스트** (뷰모델 로직만; 마우스 끌기는 스크린샷과 실제 PC 확인)
```csharp
[AvaloniaFact]
public void Opens_with_the_last_aspect_as_the_largest_centred_box()
{
    var store = TestStore();                      // store.Settings.ImageTools.CropAspect = "1:1"
    var vm = new ImageToolViewModel(ImageToolKind.Crop, [TestImages.Png(400, 200)], store, Loc("ko"));
    Assert.Equal("1:1", vm.Aspect.Value);
    Assert.Equal((200, 200), (vm.PixelWidth, vm.PixelHeight));
    Assert.Equal(.25, vm.Crop.X, 3);
}

[AvaloniaFact]
public void Next_file_shows_the_same_box_refitted_and_save_returns_one_shared_edit()
{
    var vm = new ImageToolViewModel(ImageToolKind.Crop, [TestImages.Png(400, 200), TestImages.Png(200, 400)], TestStore(), Loc("ko"));
    vm.Aspect = vm.Aspects.Single(a => a.Value == "1:1");
    vm.NextCommand.Execute(null);
    Assert.Equal("2개 중 2", vm.PositionText);
    Assert.Equal(vm.PixelWidth, vm.PixelHeight);
    vm.SaveCommand.Execute(null);
    Assert.Equal(1.0, vm.Result!.Crop!.Aspect);
}

[AvaloniaFact]
public void Typing_a_pixel_width_keeps_a_fixed_ratio_and_save_remembers_the_aspect()
{
    var store = TestStore();
    var vm = new ImageToolViewModel(ImageToolKind.Crop, [TestImages.Png(400, 200)], store, Loc("ko"));
    vm.Aspect = vm.Aspects.Single(a => a.Value == "16:9");
    vm.PixelWidth = 160;
    Assert.Equal(90, vm.PixelHeight);
    vm.SaveCommand.Execute(null);
    Assert.Equal("16:9", store.Settings.ImageTools.CropAspect);
}

[AvaloniaFact]
public void Undo_restores_the_previous_box_and_cancel_returns_nothing()
{
    var vm = new ImageToolViewModel(ImageToolKind.Crop, [TestImages.Png(400, 200)], TestStore(), Loc("ko"));
    var before = vm.Crop.Clone();
    vm.Aspect = vm.Aspects.Single(a => a.Value == "free");
    vm.Crop = new CropRect { X = .1, Y = .1, Width = .2, Height = .2 };
    vm.UndoCommand.Execute(null);
    Assert.NotEqual(.2, vm.Crop.Width);
    vm.CancelCommand.Execute(null);
    Assert.Null(vm.Result);
}
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현** (스펙 3절·3.1절이 기준)
  - 창: `PresetEditorWindow`/`RenameToolWindow`를 본뜬다. 레이아웃은 스펙 3절 그림(위: 제목 + "n개 중 i" + ‹ ›, 가운데: `ImageCanvas`, 오른쪽: `CropPanel`, 아래: 안내 + 초기화/취소/저장). 커서가 있는 화면의 작업 영역 80% 이하, 커서 근처, `Topmost` 아님. 열리면 `Activate()` + `Focus()`(도넛 창은 활성화되지 않는 창이라서).
  - `ImageCanvas`: 그림을 창에 맞춰 그리고, 상자 밖을 50% 검정으로 어둡게, 8개 손잡이, 끄는 동안 삼등분 격자. 상자 밖에서 끌면 새 상자, 안에서 끌면 이동, 손잡이로 크기. 고정 비율이면 비율 유지, 자유 비율에서 Shift를 누르면 그때 비율 유지. 상자 안 더블클릭 = 저장. 휠(또는 Ctrl+휠) 확대, Space+끌기·가운데 버튼 끌기 이동. 모든 값은 `CropRect` 비율로 주고받는다.
  - 키: Enter 저장, Esc 닫기, Ctrl+Z / Ctrl+Shift+Z·Ctrl+Y, Ctrl+0 맞춤, Ctrl+1 실제 크기, ←/→ 파일 이동, 1~7 비율(글자 입력 칸에 포커스가 없을 때만).
  - 되돌리기: 끌기 하나, 비율 바꾸기 하나, 숫자 입력 하나가 각각 한 단계(`Stack<CropRect>`).
  - 미리보기 그림: `MagickImage` 읽기 → `AutoOrient()` → 긴 변 2048px 이하로 줄여 PNG로. 원본 크기(`PixelWidth` 계산용)는 방향을 바로잡은 원본 크기(`MagickImageInfo` + 방향 5~8이면 가로세로 바꿈). 파일 읽기는 `Task.Run`, 다음/이전 파일은 그때 읽는다.
  - `WindowService.EditImageAsync`: 창을 `Show()`하고 닫힐 때까지 기다려 `vm.Result`를 돌려준다.
  - i18n 6개(ko 기준): `tool.crop.title`("자르기"), `tool.position`("{1}개 중 {0}"), `tool.applies_to_all`("{0}개 파일에 함께 적용돼요"), `tool.reset`("초기화"), `tool.cancel`("취소"), `tool.save`("저장"), `tool.crop.aspect`("비율"), `tool.crop.free`("자유"), `tool.crop.original`("원본"), `tool.crop.size`("크기(px)").
- [ ] **Step 4:** 테스트 PASS. `RenderTests`에 `Crop_window_renders(string language)`(ko, en)를 추가하고 스크린샷 확인.
- [ ] **Step 5:** 커밋 `feat: crop window`

---

### Task 8: 피치 글래스 스타일 (색과 설정)

**Files:**
- Modify: `src/Filee.Core/Settings/AppSettings.cs` (`ThemeStyle`, `ThemeSettings.Style`), `src/Filee.App/Services/ThemeService.cs` (`Apply`), `src/Filee.App/Styles/Soft.axaml` (테마 사전 + `c|DonutMenu[IsGlass=True]` 스타일), `src/Filee.App/Controls/DonutMenu.cs` (`IsGlassProperty`), `src/Filee.App/ViewModels/RadialViewModel.cs` (`IsGlass`), `src/Filee.App/Views/RadialWindow.axaml` (바인딩), `src/Filee.App/Views/Pages/ThemePage.axaml` + `ThemePageViewModel.cs`
- Test: `tests/Filee.App.Tests/RenderTests.cs` (스크린샷 2개), `tests/Filee.Core.Tests`(기본값)

**Interfaces:**
- Produces: `enum ThemeStyle { Classic, PeachGlass }`, `ThemeSettings.Style` 기본 `PeachGlass` (`LenientEnumJsonConverter`), `DonutMenu.IsGlassProperty` (`bool`), `RadialViewModel.IsGlass` (`[ObservableProperty]`, `ApplyAppearance`에서 설정)

- [ ] **Step 1: 실패하는 테스트**
```csharp
// Core
[Fact]
public void New_installs_start_with_peach_glass() => Assert.Equal(ThemeStyle.PeachGlass, new AppSettings().Theme.Style);

// App
[AvaloniaTheory]
[InlineData("light")] [InlineData("dark")]
public void Peach_glass_donut_uses_the_peach_palette(string mode)
{
    TestServices.EnsureInitialized("ko");
    // set Theme.Style = PeachGlass, Mode = Light/Dark, ThemeService.Apply(...)
    var vm = AppHost.Get<RadialViewModel>();
    vm.Load(TestImages.Png(1), clickMode: true);
    var window = new RadialWindow { DataContext = vm };
    window.Show(); Pump();
    Assert.True(window.DonutControl.IsGlass);
    var slice = ((ISolidColorBrush)window.DonutControl.SliceBrush!).Color;
    Assert.Equal(mode == "light" ? Color.Parse("#FFE3CC") : Color.Parse("#3A2418"), Color.FromRgb(slice.R, slice.G, slice.B));
    Save(window, $"donut-peach-{mode}.png");
    window.Close();
}
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현**
  - `Soft.axaml` Light/Dark 사전에 키 추가(스펙 4절 표): `PeachSliceBrush`(#FFE3CC / #3A2418, 불투명도는 `DonutMenu.SliceOpacity`가 아니라 `Opacity="0.7"`), `PeachHotBrush`(#FFC59A / #6B3A1F), `PeachCenterBrush`(#FFF6EE 0.8 / #2A1A10 0.8), `PeachTextBrush`(#3B2416 / #FFE9D9), `PeachToolBrush`(#F2552C / #FF6A3D). 스타일 `c|DonutMenu[IsGlass=True]`가 `SliceBrush`, `CenterBrush`, `Foreground`, `ToolBrush`를 이 키로 바꾼다. `AccentBrush`는 ThemeService가 쓴 값을 그대로 쓴다(칸 강조는 `PeachHotBrush`가 더 잘 보이면 `AccentBrush` 대신 쓴다 — 스크린샷 보고 결정).
  - `ThemeService.Apply`: `theme.Style == PeachGlass`이면 accent 대신 `#FF7A1A`를 쓴다(나머지 계산은 같다).
  - `RadialViewModel.ApplyAppearance`: `IsGlass = store.Settings.Theme.Style == ThemeStyle.PeachGlass`. `RadialWindow.axaml`의 `DonutMenu`에 `IsGlass="{Binding IsGlass}"`.
  - 설정 › 모양: 맨 위 카드에 "스타일" ComboBox(Filee 기본 / 피치 글래스). 피치 글래스일 때 강조색 카드는 `IsEnabled=False`와 안내 한 줄. 미리보기 도넛도 `IsGlass` 바인딩.
  - i18n 6개: `theme.style`("스타일"), `theme.style.Classic`("Filee 기본"), `theme.style.PeachGlass`("피치 글래스"), `theme.style_accent_note`("피치 글래스는 귤색 강조색을 써요").
- [ ] **Step 4:** PASS. 스크린샷 두 장(밝게·어둡게)을 눈으로 확인하고 필요하면 색만 조정.
- [ ] **Step 5:** 커밋 `feat: Peach Glass style (palette, settings)`

---

### Task 9: 유리 효과 (화면 찍기, 흐리게, 오려 그리기)

**Files:**
- Create: `src/Filee.Core/Platform/ScreenCapture.cs`, `src/Filee.App/Services/GlassBackdrop.cs`, `src/Filee.App/Controls/DonutMenu.Glass.cs`
- Modify: `src/Filee.Core/Platform/IPlatformServices.cs` (기본 멤버), `src/Filee.Platform.Windows/WindowsPlatformServices.cs` + `NativeMethods.cs` (GDI), `src/Filee.App/Services/RadialController.cs` (`PlaceAndShow`, `FilesEntered`, `ShowForFiles`), `src/Filee.App/Controls/DonutMenu.cs` (`Render`의 칸·가운데·빈 링), `src/Filee.App/Controls/DonutMenu.Groups.cs` (`DrawSubRing`)
- Test: `tests/Filee.App.Tests/GlassTests.cs`, `tests/Filee.Platform.Tests/`(Windows에서만: 실제 캡처 크기)

**Interfaces:**
- Produces:
  - `public sealed record ScreenCapture(int Width, int Height, byte[] Bgra)` (Core, 위에서 아래 순서, 한 줄 = Width*4 바이트)
  - `IPlatformServices.ScreenCapture? CaptureScreen(int x, int y, int width, int height) => null;`
  - `GlassBackdrop.Create(ScreenCapture capture, double scaling) : Bitmap?` — 흐리게 한 Avalonia 비트맵, 전부(거의) 검정이면 null. 걸린 시간(ms)을 `out` 또는 반환 튜플로 내서 로그
  - `DonutMenu.GlassBackdropProperty` (`IImage?`) — null이면 단색 대체

- [ ] **Step 1: 실패하는 테스트**
```csharp
[Fact]
public void A_black_capture_gives_no_glass()
{
    var black = new ScreenCapture(64, 64, new byte[64 * 64 * 4]);
    Assert.Null(GlassBackdrop.Create(black, 1));
}

[AvaloniaFact]
public void Glass_shows_the_screen_behind_blurred_and_falls_back_to_flat_peach()
{
    TestServices.EnsureInitialized("ko");           // style PeachGlass
    var vm = AppHost.Get<RadialViewModel>();
    vm.Load(TestImages.Png(1), clickMode: true);
    var window = new RadialWindow { DataContext = vm };
    window.Show(); Pump();
    var side = (int)window.DonutControl.Bounds.Width;
    window.DonutControl.GlassBackdrop = GlassBackdrop.Create(HalfRedHalfBlue(side, side), 1);
    Pump();
    var withGlass = Snapshot(window);               // RenderTargetBitmap of the window
    var left = PixelAtRingLeft(withGlass); var right = PixelAtRingRight(withGlass);
    Assert.True(left.R > left.B && right.B > right.R, $"{left} / {right}");
    Save(window, "donut-glass.png");

    window.DonutControl.GlassBackdrop = null;
    Pump();
    var flat = Snapshot(window);
    Assert.Equal(PixelAtRingLeft(flat), PixelAtRingRight(flat), new ColorTolerance(6));
    window.Close();
}
```
`HalfRedHalfBlue`, `Snapshot`, `PixelAtRing*`, `ColorTolerance`는 테스트 파일 안 도우미(링 가운데 반지름, 왼쪽·오른쪽 9시·3시 지점).
Windows 플랫폼 테스트: `CaptureScreen(0, 0, 32, 16)` 결과가 null이 아니고 `Bgra.Length == 32*16*4`.

- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현**
  - Windows `CaptureScreen`: `GetDC(0)` → `CreateCompatibleDC` → `CreateDIBSection`(32bpp, 높이 음수 = 위에서 아래) → `BitBlt(..., SRCCOPY)` (**`CAPTUREBLT` 없이**) → 바이트 복사 → 모두 해제. 실패하면 null. `LibraryImport` 스타일은 `NativeMethods.cs`를 따른다.
  - `GlassBackdrop.Create`: 바이트를 `SKBitmap`(Bgra8888, Premul)로 → 평균 밝기가 거의 0이면 null → `SKImageFilter.CreateBlur(20*scaling, 20*scaling)`(가장자리는 `SKShaderTileMode.Clamp`)로 그려 새 비트맵 → Avalonia `WriteableBitmap`(Bgra8888)으로 복사. 600×600 기준 15ms를 넘으면 절반 크기로 줄여서 흐리게 하고 다시 키운다(한 번 넘으면 그 뒤로 계속 절반). 걸린 시간은 `log.LogDebug`.
  - `RadialController.PlaceAndShow`: `viewModel.IsGlass && !window.IsVisible`일 때만 `Show()` 직전에 `_capture = platform.CaptureScreen(position.X, position.Y, sidePx, sidePx)`; 창이 이미 보이면 `_capture = null`. `ShowForFiles`는 `PlayOpenAnimation()` 직전에, 드래그는 `FilesEntered`에서 `Load` 직후에 `donut.GlassBackdrop = _capture is null ? null : GlassBackdrop.Create(_capture, scaling)`. `Close`/`HideWindow`에서 `GlassBackdrop = null`, `_capture = null`.
  - `DonutMenu.Glass.cs`: `void DrawGlass(DrawingContext context, Geometry shape, Matrix shapeTransform)` — `GlassBackdrop`이 있고 `IsGlass`일 때 `ImageBrush`(`DestinationRect` = 컨트롤 전체 절대 좌표, `Stretch.Fill`, `Transform` = `shapeTransform`의 역행렬, `TransformOrigin` = 왼쪽 위)로 `shape`를 채운다. 칸이 커지고 돌아가는 애니메이션 중에도 비친 화면은 제자리에 있다. 그다음 기존 반투명 칸 색을 그린다(피치 사전의 0.7 불투명도가 유리 위 덮개). 안쪽 1px 밝은 테두리: `Pen(White 30%, 1)`로 shape를 한 번 더.
  - 호출 위치: `Render`의 각 칸(이미 `transform`이 있다), 빈 링(`n == 0`, 단위 행렬), `DrawCenter`(펄스 크기 반영), `DrawSubRing`의 하위 칸. 진행률 링(`RenderProgress`)은 유리 없이 그대로 둔다.
  - 캡처와 창 좌표가 같은지(`DonutMenu`가 창 (0,0)에 꽉 차는지)는 `TranslatePoint`로 확인하고, 다르면 그 차이만큼 `DestinationRect`를 옮긴다.
- [ ] **Step 4:** PASS. `donut-glass.png` 눈으로 확인.
- [ ] **Step 5:** 커밋 `feat: Peach Glass blurs the screen behind the donut`

---

### Task 10: 앱 전체 피치 글래스 (창 배경, 카드)

사용자 요청(2026-10-20 "디자인도 업그레이드"): 도넛만이 아니라 설정 창·편집 창·알림 카드도 같은 모양으로.

**Files:**
- Modify: `src/Filee.App/Services/Motion.cs` (`Apply`, 새 `SetPeach`), `src/Filee.App/Services/ThemeService.cs`, `src/Filee.App/Styles/Soft.axaml`, `src/Filee.App/Views/MainWindow.axaml`, `src/Filee.App/Views/ImageToolWindow.axaml`, `PresetEditorWindow.axaml`, `GroupEditorWindow.axaml` (`Classes="glassy"` 추가)
- Test: `tests/Filee.App.Tests/RenderTests.cs`

**Interfaces:**
- Produces: `Motion.SetPeach(bool on)` — 추적 중인 모든 창에 `peach` 클래스를 켜고 끈다(`reduced-motion`과 같은 방식). `Motion.Track`된 창은 처음부터 맞는 클래스를 가진다.

- [ ] **Step 1: 실패하는 테스트**
```csharp
[AvaloniaTheory]
[InlineData("ko", "light")] [InlineData("ko", "dark")]
public void Main_window_takes_the_peach_glass_look(string language, string mode)
{
    // EnsureInitialized + Theme.Style = PeachGlass, Mode, ThemeService.Apply
    var window = /* MainWindow as in Main_window_pages_render */;
    window.Show(); Pump();
    Assert.Contains("peach", window.Classes);
    Assert.Contains(WindowTransparencyLevel.Mica, window.TransparencyLevelHint);
    Save(window, $"main-peach-{mode}-{language}.png");
    // Classic: class and hint go away again
    // set Style = Classic, Apply → Assert.DoesNotContain("peach", window.Classes)
}
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현**
  - `ThemeService.Apply` 끝에서 `Motion.SetPeach(theme.Style == ThemeStyle.PeachGlass)`.
  - `Soft.axaml` Light/Dark에 `PeachPageBrush`(#FFF3E8 / #1E140E, 불투명도 0.78), `PeachSurfaceBrush`(#FFFFFF 0.72 / #2A1A10 0.72), `PeachBorderBrush`(#FFFFFF 0.55 / #FFFFFF 0.10) 추가.
  - 스타일: `Window.peach.glassy` → `TransparencyLevelHint = Mica, AcrylicBlur, None`, `Background = PeachPageBrush`. `Window.peach Border.card` → `Background = PeachSurfaceBrush`, `BorderBrush = PeachBorderBrush`, `BorderThickness = 1`. 알림 카드처럼 창 자체가 투명한 곳(`ToastWindow`, 알림 창들)은 `glassy`를 달지 않는다: 창 사각형 전체가 흐려지기 때문. 카드만 피치 색이 된다(카드 불투명도 0.95로 따로: `Window.peach:not(.glassy) Border.card`).
  - Windows 10에서 Mica가 없으면 Avalonia가 다음 값(AcrylicBlur, None)으로 내려간다. None일 때 배경이 너무 비치지 않도록 `ActualTransparencyLevel == None`이면 불투명 `PageBrush`를 쓴다(창 코드 비하인드 한 줄, 또는 `Window.peach.glassy` 스타일 안에서 처리).
- [ ] **Step 4:** PASS. 스크린샷 4장(설정 창 밝게·어둡게, 알림 카드, 프리셋 편집 창) 눈으로 확인.
- [ ] **Step 5:** 커밋 `feat: Peach Glass for settings, editor windows and toast cards`

---

### Task 11: 도넛 칸 아이콘

**Files:**
- Create: `src/Filee.App/Controls/DonutIcons.cs`
- Modify: `src/Filee.App/Styles/Icons.axaml` (새 아이콘), `src/Filee.App/Controls/DonutMenu.cs` (`DonutItem.Icon`, `DrawLabel`), `src/Filee.App/Controls/DonutMenu.Groups.cs` (하위 링), `src/Filee.App/ViewModels/RadialViewModel.cs` (`ItemOf`, 그룹 항목), `src/Filee.App/ViewModels/Pages/ThemePageViewModel.cs` (미리보기 항목)
- Test: `tests/Filee.App.Tests/DonutIconTests.cs`, `DonutLabelPlacementTests.cs`

**Interfaces:**
- Produces:
  - `DonutItem.Icon` : `Geometry?` (24×24 상자 기준 경로, init)
  - `static class DonutIcons { static Geometry? For(Preset preset) }` — 아래 표 순서로 고른다

| 조건 | 아이콘 (Material Design Icons 이름) |
|---|---|
| id `crop` | `crop` |
| id `compress-image` | `arrow-collapse-all` |
| id `strip-metadata` | `tag-remove-outline` |
| 그룹 `tools` | `tools` |
| 그룹 `rotate` / `rotate-left` / `rotate-right` / `rotate-180` | `rotate-right` (왼쪽은 `rotate-left`) |
| 그룹 `flip` / `flip-*` | `flip-horizontal` / `flip-vertical` |
| `merge-pdf`, `zip-all` 등 합치기 | `set-merge` |
| 대상 형식 분류: Image | `image-outline` |
| Document, Pdf | `file-document-outline` |
| Video | `filmstrip` |
| Audio | `music-note` |
| Archive / 폴더 풀기 | `folder-zip-outline` |
| 그 밖 | `null` (아이콘 없음) |

  경로 데이터는 pictogrammers.com의 공식 SVG에서 그대로 가져와 `Icons.axaml`에 `Icon.Donut.*` 키로 넣고(파일 맨 위 Apache-2.0 주석이 이미 덮는다), `DonutIcons`는 `Application.Current.FindResource`로 읽는다.

- [ ] **Step 1: 실패하는 테스트**
```csharp
[AvaloniaFact]
public void Every_slice_on_the_default_image_and_pdf_donuts_has_an_icon()
{
    TestServices.EnsureInitialized("ko");
    var store = AppHost.Get<UserDataStore>();
    foreach (var profileId in new[] { "images", "pdf" })
        foreach (var id in store.Profiles.Single(p => p.Id == profileId).PresetIds)
            Assert.NotNull(DonutIcons.For(store.FindPreset(id)!));
}

[Fact]
public void Icon_and_label_together_fit_inside_a_slice_of_a_full_donut()
{
    // DonutLabelPlacementTests style: 12 slices, smallest OuterRadius 100, HoleRatio 0.42
    // the icon block (18 px + 3 px gap) plus a two-line label stays inside the wedge's ring thickness;
    // when it would not, the icon is left out (assert the layout helper reports IconShown == false)
}
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현.** `DrawLabel`: 아이콘이 있고 공간이 되면(링 두께 − 글자 높이 ≥ 24px, 칸 폭 ≥ 28px) 글자 묶음 위에 18px 아이콘을 글자와 같은 색으로 그리고, 묶음 전체를 칸 가운데에 다시 맞춘다. 공간이 안 되면 아이콘 없이 지금처럼. 올려 둔 칸에서는 아이콘이 1.12배(Task 12의 hover 값에 맞춰). 하위 링 칸은 아이콘만(글자가 이미 짧다) 또는 지금처럼 글자만 — 공간 계산은 같은 도우미. 계산은 `internal static (bool IconShown, Rect Icon, Rect Text) LabelLayout(...)`로 빼서 테스트한다.
- [ ] **Step 4:** PASS. `donut-images-ko.png`, `donut-peach-light.png` 다시 보고 확인.
- [ ] **Step 5:** 커밋 `feat: icons on donut slices`

---

### Task 12: 움직임 다듬기

**Files:**
- Modify: `src/Filee.App/Controls/DonutMenu.cs` (상수 :88-92, `EaseOutBack`, hover spring, 닫기), `src/Filee.App/Controls/DonutMenu.Groups.cs` (하위 링 펼침), `src/Filee.App/Controls/DonutMenu.Progress.cs` (✓ 표시)
- Test: 기존 `DonutProgressTests`, `DonutGroupTests`, `DonutHighlightTests` + `RenderTests`에 프레임 스크린샷

**정할 값 (처음 값. 스크린샷과 실제 PC에서 눈으로 맞춘다)**

| 동작 | 지금 | 바꿀 값 |
|---|---|---|
| 열기 시간 / 칸 사이 간격 | 0.42s / 0.028s | 0.34s / 0.022s |
| 열기 튐(`EaseOutBack` c1) | 1.55 | 1.25 |
| 가운데 원 | 칸과 같이 | 0.85→1 크기로 0.06s 먼저 튀어나옴 |
| 올린 칸 튀어나옴(`PopOut`) / 확대 | 7px / 2.5% | 9px / 3.5%, 아이콘 1.12배 |
| 하위 링 펼침 | 한꺼번에 | 칸마다 0.03s 간격, 안쪽에서 바깥으로 6px 미끄러짐 |
| 닫기 | 지금 값 | 0.18s 축소(0.92배) + 페이드 |
| 완료 ✓ | 나타남 | 체크 선이 0.22s 동안 그려진 뒤 가운데가 한 번 0.06 튐 |

- `Motion.Enabled == false`면 모든 값이 바로 끝 상태(지금 규칙 그대로).
- 피치 글래스의 유리 배경(Task 9)은 칸이 커지는 동안에도 제자리(이미 역행렬로 고정).

- [ ] **Step 1:** 기존 애니메이션 테스트 목록을 돌려 기준을 잡는다: `dotnet test tests/Filee.App.Tests --filter "DonutProgress|DonutGroup|DonutHighlight"` → PASS.
- [ ] **Step 2: 실패하는 테스트** — `Open_animation_frames` 렌더 테스트: 열기 시작 후 0.05/0.15/0.34s 시점의 프레임을 저장하고, 0.34s 프레임의 모든 칸 `_open` 값이 1인지(내부 값은 `internal` 접근자로) 확인. 하위 링: 펼침 0.03s 시점에 첫 칸만 0보다 크고 마지막 칸은 0인지 확인.
- [ ] **Step 3:** 표의 값으로 구현. 프레임 시간은 기존 `_lastFrame` 방식 그대로.
- [ ] **Step 4:** 새 테스트 + Step 1 테스트 PASS. 프레임 스크린샷 확인.
- [ ] **Step 5:** 커밋 `feat: smoother donut motion`

---

### Task 13: 알림 카드 새 디자인 (결과 미리보기, 크기 변화)

**Files:**
- Modify: `src/Filee.App/ViewModels/JobViewModel.cs`, `src/Filee.App/Views/ToastWindow.axaml`
- Test: `tests/Filee.App.Tests/ToastWindowTests.cs`(기존 파일에 추가)

**Interfaces:**
- Consumes: `ByteSize.Format` (Task 6)
- Produces: `JobViewModel.Thumbnail` (`Bitmap?`, `[ObservableProperty]`), `JobViewModel.SizeChange` (`string?`, `[ObservableProperty]`)

- [ ] **Step 1: 실패하는 테스트**
```csharp
[AvaloniaFact]
public async Task A_finished_image_job_shows_a_thumbnail_and_how_much_smaller_it_got()
{
    // source: 2 MB-ish noisy PNG, output: smaller JPG — written by the test, job built the way existing toast tests build one
    var vm = /* JobViewModel for a completed job with that source and output */;
    await vm.LoadFinishDetailsAsync();      // the step the job's completion runs
    Assert.NotNull(vm.Thumbnail);
    Assert.Equal(56, vm.Thumbnail!.PixelSize.Height, 8);   // fits a 56 px square (×scaling)
    Assert.Matches(@"^[\d.]+ (KB|MB) → [\d.]+ (KB|MB) · \d+% 작아짐$", vm.SizeChange);
}

[AvaloniaFact]
public async Task A_finished_document_job_shows_no_thumbnail_but_the_size()
{
    // source .txt → output .pdf (fake files of known size)
    Assert.Null(vm.Thumbnail);
    Assert.NotNull(vm.SizeChange);
}
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3: 구현**
  - `JobViewModel`: 작업이 끝나 `HasOutputs`가 true가 되는 순간 한 번 `LoadFinishDetailsAsync()`(internal)를 부른다. `Task.Run` 안에서: 첫 출력이 이미지(`FormatRegistry.Detect` 분류 Image)면 Magick으로 읽어 `AutoOrient` 후 짧은 변이 56px(화면 배율 곱)이 되게 줄여 PNG → `Bitmap`. 원본 합과 출력 합(`File.Exists`인 것만)이 둘 다 0보다 크면 `SizeChange` = `loc.Format("toast.size_change", before, after, percent)` 또는 `toast.size_larger`. 실패해도 조용히 둔다(카드는 지금 모양 그대로).
  - 카드 레이아웃: 왼쪽 56×56 둥근 썸네일(없으면 형식 아이콘 원, Task 11의 `DonutIcons`와 같은 경로), 오른쪽 위 제목, 그 아래 `Status` 또는 끝난 뒤엔 `SizeChange`, 진행 막대는 지금처럼. 끝나면 오른쪽 위 작은 ✓ 배지(성공색).
  - i18n 6개: `toast.size_change`("{0} → {1} · {2}% 작아짐"), `toast.size_larger`("{0} → {1} · {2}% 커짐").
- [ ] **Step 4:** PASS. `ToastWindowTests`의 스크린샷 확인(Classic, Peach 둘 다).
- [ ] **Step 5:** 커밋 `feat: toast cards show the result and the size change`

---

### Task 14: 설치 파일 만들고 실제 PC에서 확인

**Files:** 없음(빌드·확인), 필요하면 `README.ko.md`의 설치 안내

- [ ] **Step 1:** `dotnet format --verify-no-changes` && `dotnet test Filee.slnx -c Release` → 기준선 대비 새 실패 0.
- [ ] **Step 2:** `pwsh build/build-installer.ps1` (Zig·Inno Setup이 없으면 스크립트 안내대로 설치) → `Releases/FileePlus-1.0.0-win-Setup.exe`, `FileePlus-1.0.0-SHA256SUMS.txt` 생성 확인.
- [ ] **Step 3:** 사용자에게 설치 파일 경로를 주고 아래 확인 목록을 함께 본다(스펙 7절 "실제 PC에서 확인할 목록" 중 묶음 1 해당분):
  - 원본 Filee와 같이 깔아도 설정·오른쪽 메뉴·트레이가 따로 논다.
  - 탐색기·바탕화면에서 이미지 끌기 → Shift(움직이며/멈춰서 둘 다) → 도넛. 놓은 뒤 원본이 그대로 있다.
  - 도구 › 자르기 → 창이 앞으로 나온다 → 저장 → `사진 Cropped.jpg`. HEIC, WEBP도.
  - 압축·메타데이터 지우기 결과 이름과 크기.
  - 피치 글래스 밝게/어둡게, 배율이 다른 모니터 두 대에서 유리가 제자리에 비친다.
  - 가운데 "1개 · JPG · 1.2 MB".
  - 설정 창이 반투명 유리(Windows 11 미카)로 보이고, 도넛 칸 아이콘이 잘리지 않고, 열고 닫는 움직임이 자연스럽다. 알림 카드에 결과 미리보기와 "1.2 MB → 400 KB · 67% 작아짐".
- [ ] **Step 4:** 발견한 문제는 각각 고쳐서 커밋하고, 묶음 2 계획은 사용자가 써 본 뒤에 쓴다.
