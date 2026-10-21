# Filee Plus 묶음 2 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 원본 Filee와 차별점이 되는 남은 도구 셋(배경 추가, 가리기, 그리기)을 편집 창에 넣고, Shift + 드래그를 기본으로 끄고, 묶음 1에서 미룬 작은 문제를 고친다. 끝나면 1.1.0 설치 파일을 만든다.

**Architecture:** 세 도구는 자르기와 같은 길을 탄다. 창에서 받은 `ImageEdit`를 Tool 프리셋 복사본에 넣어 `ConversionService.Start`로 보내고, 저장 처리는 `ImageEncoder.ApplyOptions` 안에서 한다. 새로 SkiaSharp `ImageEditRenderer`(Filee.Engines)를 만든다. 저장할 때는 Magick 픽셀 → SKBitmap → 렌더러 → Magick으로 처리하고, 미리보기는 같은 렌더러가 Avalonia의 Skia 캔버스에 그린다. 그래서 미리보기와 결과가 같다. 그리기는 파일마다 내용이 달라서 작업에 "파일별 편집"을 싣는다.

**Tech Stack:** C# / .NET 10, Avalonia 12, CommunityToolkit.Mvvm, Magick.NET 14.17, SkiaSharp 3.119(이미 있음), xunit v3, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-10-19-filee-plus-design.md`. 2절(도구 칸), 3절(편집 창 공통, 3.2 배경 추가, 3.3 가리기, 3.4 주석), 1절의 "묶음 2 변경 (2026-10-21)".

## Global Constraints

- 새 외부 라이브러리 금지. SkiaSharp, Magick.NET은 이미 있다.
- 주석은 영어로 쓴다. 새 파일 맨 위에 목적 주석을 달고, public 멤버에 `///` 문서를 단다(CONTRIBUTING.md).
- UI 문자열은 코드에 쓰지 않는다. 새 키는 `src/Filee.App/Assets/i18n/{en,ko,es,hi,pl,zh-CN}.json` 6개에 모두 넣는다. es/hi/pl/zh-CN은 번역한다.
- `Filee.Core`에는 UI·OS 코드를 넣지 않는다.
- 이름은 그대로 둔다: `Filee.*`, `Filee.exe`, 표시 이름 `AppIdentity.Name`("Filee Plus"). 본뜬 맥 앱의 이름은 코드·UI·문서 어디에도 쓰지 않는다.
- 새 코드는 되도록 새 파일에 둔다.
- 설정 스키마 번호(`AppSettings.CurrentSchemaVersion = 22`)는 올리지 않는다. 새 설정 값은 모두 기본값을 가진다.
- 창을 드롭 처리 안에서 열지 않는다. 항상 `Dispatcher.UIThread.Post`로 연다.
- 커밋 전에 매번 확인한다.
  - `dotnet format Filee.slnx --verify-no-changes`
  - 해당 테스트 프로젝트를 하나씩 돌린다: `dotnet test tests/Filee.<X>.Tests -c Debug`. 전체 slnx를 한 번에 돌리면 이 PC는 메모리가 모자란다.
- 커밋 메시지 끝에는 세션의 Co-Authored-By 줄과 Claude-Session 줄을 붙인다. 작성자 이메일은 저장소 설정(noreply)을 그대로 쓴다.
- 기준선 실패는 우리 탓이 아니다.
  - Engines `CadTests.Korean_text_in_pre_2007_files_is_decoded`, `MediaTests.Progress_rises_while_ffmpeg_works`
  - 부하가 걸릴 때만 가끔 실패: App `GlassTests`, `MemoryTrimmerTests`, `DonutGroupTests` 오른쪽 클릭

## 스펙과 다르게 정한 것 (리뷰 때 확인)

1. **Shift + 드래그는 꺼진 채로 기본 목록에 남는다.** 사용자가 2026-10-21에 정했다. 홈 화면 안내와 트레이 툴팁은 이미 "켜진 첫 드래그 동작"을 읽으므로 Ctrl로 바뀐다.
2. **이미 설치된 1.0.0 사용자에게 새 도구를 넣는 방법.** 스키마 번호를 올리지 않는다. 대신 불러올 때마다 "없는 내장 도구 프리셋만 추가"하는 단계를 둔다(Task 2).
   - 라이브러리에 그 id가 아예 없을 때만 추가하고, `tools` 그룹에 빈자리가 있으면 스펙 순서 자리에 끼운다.
   - 사용자가 그룹에서 뺀 도구는 프리셋이 남아 있으므로 다시 들어오지 않는다.
3. **그리기는 파일별 편집을 작업 하나에 싣는다.** `ConversionService.Start(files, preset, perFile)`를 쓰고, 알림 카드는 하나다. 아무것도 그리지 않은 파일은 목록에서 빠진다.
4. **한국어 이름.** 주석 도구는 "그리기"(링 이름 "그리기"), 배경 추가는 "배경 넣기"(링 이름 "배경")다. "주석"보다 알아듣기 쉽다. 파일 이름 꼬리는 스펙대로 `{name} Annotated` / `{name} with background` / `{name} Redacted`다.
5. **사용자 그림 배경의 파일 경로는 기억하지 않는다.** 다음에 열면 마지막 그라데이션이나 단색으로 시작한다. 지워진 파일을 가리키지 않게 하려는 것이다.
6. **미리보기는 `ICustomDrawOperation`으로 그린다.** 미리보기용 줄인 그림(긴 변 2048)을 SKImage로 들고, 화면 사각형에 렌더러로 그린다. 묶음 1의 자르기 캔버스는 그대로 둔다. 확대·이동 계산만 공용 기반 클래스로 옮긴다.

## Review Focus

1. **가리기가 새는 경우.** 모자이크 칸이 너무 작아 읽히거나, EXIF 안의 작은 미리보기 그림에 원본이 남는 경우다. → Task 3: 칸이 고르다는 테스트, 결과에 EXIF·썸네일이 없다는 테스트.
2. **EXIF로 돌아간 휴대폰 사진.** 화면에서 그린 상자와 선이 저장한 결과에서 다른 곳에 찍히면 안 된다. → Task 3·4에 방향 6 JPG 테스트.
3. **투명 PNG(로고)에 배경 넣기.** 투명한 곳으로 배경이 보여야 하고, 검은 바탕이 되면 안 된다. → Task 3에 투명 PNG 테스트.
4. **한글 글자.** 맑은 고딕이 없는 PC에서도 네모(□)가 아니라 글자가 그려져야 한다. → Task 4에 글꼴 대체 테스트.
5. **여러 파일 그리기에서 되돌리기.** Ctrl+Z는 지금 보고 있는 파일만 되돌린다. 저장 결과에서 파일끼리 그림이 섞이면 안 된다. → Task 8에 테스트.

---

### Task 1: Shift 기본 끄기와 묶음 1에서 미룬 문제

**Files:**
- Modify:
  - `src/Filee.Core/Settings/TriggerGesture.cs` (`Defaults()`)
  - `src/Filee.Core/Conversion/JobQueue.cs` (only-if-smaller 단계, 최종 출력 할당기)
  - `src/Filee.App/ViewModels/JobViewModel.cs` (알림 썸네일)
  - 감시 폴더 서비스(원본 옮기기). 감시 규칙을 처리하는 곳을 찾는다.
  - `src/Filee.App/Views/Pages/ToolbarPage.axaml:73-99` (`extchip` 스타일)
- Test:
  - `tests/Filee.Core.Tests/TriggerDefaultsTests.cs`
  - `tests/Filee.Core.Tests/JobQueueTests.cs`
  - App 알림 테스트
  - 감시 폴더 테스트

**값**
- Windows `Defaults()` 순서: `[Ctrl + 드래그, Ctrl+Alt+Space, Shift + 드래그(Enabled = false)]`. 다른 OS는 그대로다.
- 이미 저장된 동작 목록은 건드리지 않는다(이전 단계 없음).

- [ ] **Step 1: 실패하는 테스트**
  - `Windows_defaults_keep_shift_drag_but_switched_off`
    - `Count == 3`
    - `d[0]`은 (Drag, Ctrl, Enabled)
    - `d[1].Kind == KeyChord`
    - `d[2]`는 (Drag, Shift, `Enabled == false`)
    - Windows가 아니면 건너뛴다.
  - `Only_if_smaller_never_deletes_a_file_that_was_there_before`
    - 출력 충돌 정책을 Overwrite로 둔다.
    - 출력 경로에 이미 파일이 있고, 결과가 원본보다 크다.
    - 기대: 결과 파일이 남고 상태는 Done이다. 묶음 1 전과 같은 동작이다.
  - `A_skipped_output_leaves_no_empty_folder`
    - 출력이 하위 폴더로 가고, 작아지지 않아 건너뛴다.
    - 기대: 그 하위 폴더가 없다. 원본 폴더는 남는다.
  - `Toast_thumbnail_of_an_animated_png_reads_one_frame`
    - `ImageReader.Read(firstFrameOnly: true)` 경로를 쓰는지 확인한다. apng도 같은 경로다.
  - `Watch_folder_moves_the_original_when_compress_skips_it`
    - 감시 폴더에서 "원본 옮기기"를 켜고, 결과가 작아지지 않아 건너뛴다.
    - 기대: 원본이 처리된 원본 폴더로 옮겨지고, 다음 시작 때 다시 처리되지 않는다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3: 구현**
  - `FinalAllocator`가 넘겨줄 때 이미 있던 경로를 기억한다. `TryDiscard`는 그 경로를 지우지 않고, 그 파일은 그대로 결과로 둔다.
  - 건너뛰어 지운 출력의 부모 폴더가 원본 폴더가 아니고, 이번 작업에서 만든 폴더이며, 비어 있으면 지운다(재귀 삭제 금지).
  - 알림 썸네일의 apng 경로도 첫 장면만 읽는다.
  - 감시 폴더는 "작아지지 않아 건너뜀"을 처리 완료로 본다.
  - `extchip`의 hover 대비를 4.5:1 이상으로 올린다. 밝게·어둡게·피치를 모두 맞춘다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 커밋한다.

---

### Task 2: 편집 모델, 세 프리셋, 파일별 편집 작업

**Files:**
- Modify:
  - `src/Filee.Core/Presets/ImageEdit.cs`
  - `src/Filee.Core/Presets/Preset.cs:243` (`TurnsOrMirrors`)
  - `src/Filee.Core/Settings/ImageToolSettings.cs`
  - `src/Filee.Core/Presets/BuiltInData.cs:264-306`
  - `src/Filee.Core/Settings/UserDataStore.cs` (`EnsureConsistency` 또는 `Load`)
  - `src/Filee.Core/Conversion/{ConversionJob,JobQueue}.cs`
  - `src/Filee.App/Services/ConversionService.cs:62-66`
  - `src/Filee.App/Controls/DonutIcons.cs`
  - `src/Filee.App/Styles/Icons.axaml`
  - i18n 6개
- Test:
  - `tests/Filee.Core.Tests/{ImageToolPresetTests,JobQueueTests,UserDataStoreTests}.cs`
  - `tests/Filee.App.Tests/{DonutIconTests,LocalizationTests,ImageToolDonutTests}.cs`. 3칸으로 고정된 값을 6으로 바꾼다.

**Interfaces (Produces)**

```csharp
// ImageEdit.cs: every position and size is a fraction (0-1) of the oriented, cropped picture; widths and sizes are
// fractions of its short side.
public enum RedactMode { Black, Mosaic }
public enum AnnotationKind { Pen, Highlighter, Arrow, Line, Rectangle, Ellipse, Text, Number }
public enum StrokeLevel { Thin, Normal, Thick }
public enum BackgroundKind { Gradient, Solid, Image }
public sealed class EditPoint { public double X { get; set; } public double Y { get; set; } }
public sealed class EditRect { public double X, Y, Width, Height; /* properties */ public EditRect Clone(); }
public sealed class RedactEdit { public RedactMode Mode { get; set; } public List<EditRect> Boxes { get; set; } = []; public RedactEdit Clone(); }
public sealed class Annotation
{
    public AnnotationKind Kind { get; set; }
    public string Color { get; set; } = "#FF3B30";
    public StrokeLevel Width { get; set; } = StrokeLevel.Normal;
    public List<EditPoint> Points { get; set; } = []; // Pen/Highlighter: path; Arrow/Line: from, to; Rectangle/Ellipse: corners; Text/Number: anchor
    public string? Text { get; set; }   // Text only
    public int Number { get; set; }     // Number only
    public Annotation Clone();
}
public sealed class BackgroundEdit
{
    public BackgroundKind Kind { get; set; } = BackgroundKind.Gradient;
    public string Value { get; set; } = "peach";   // gradient id or #RRGGBB
    public string? ImagePath { get; set; }          // Kind == Image
    public double Padding { get; set; } = 0.08;     // 0-0.40
    public double Corner { get; set; } = 0.03;      // 0-0.25
    public int Shadow { get; set; } = 40;           // 0-100
    public string Aspect { get; set; } = "auto";    // auto, 1:1, 16:9, 9:16, 4:5, 4:3
    public BackgroundEdit Clone();
}
// ImageEdit gains: RedactEdit? Redact; List<Annotation>? Annotations; BackgroundEdit? Background;
// bool Draws => Redact is { Boxes.Count: > 0 } || Annotations is { Count: > 0 } || Background is not null;
// Clone() copies all four.
```

- `ImageOptions.TurnsOrMirrors`는 `Edit?.Crop is not null || Edit?.Draws == true`에도 참이다. `JobQueue.Unturned`는 이미 `Edit = null`로 비우니 그대로 둔다.
- `ConversionJob`에 `IReadOnlyDictionary<string, ImageEdit>? EditsByFile`을 추가한다. `JobQueue`는 파일마다 `job.Preset` 복사본의 `Image.Edit`을 그 파일 몫으로 바꿔서 돌린다.
  - 키는 원본 전체 경로이고, 대소문자를 구분하지 않는다.
  - 몫이 없는 파일은 작업에 넣지 않는다. `Start`가 거른다.
- `ConversionService.Start(IReadOnlyList<string> files, Preset preset, IReadOnlyDictionary<string, ImageEdit>? perFile = null)`
  - Tool 프리셋은 `Image.Edit`나 `perFile` 중 하나가 있어야 돈다.
- `ImageToolSettings`에 마지막 설정을 추가한다. 모두 기본값을 가진다.
  - `Background`(`BackgroundEdit`, `ImagePath`는 저장하지 않음)
  - `RedactMode RedactMode = Black`
  - `string PenColor = "#FF3B30"`
  - `StrokeLevel PenWidth = Normal`
  - `AnnotationKind LastTool = Pen`
- 내장 프리셋(`ImageToolPresets()`):

  | id | NameKey | Tool | TargetFormat | 이름 | 이미지 옵션 |
  |---|---|---|---|---|---|
  | `add-background` | `preset.add_background` | `AddBackground` | `png` | `{name} with background` | Quality 95 |
  | `redact` | `preset.redact` | `Redact` | `*` | `{name} Redacted` | Quality 95, `KeepMetadata = false` |
  | `annotate` | `preset.annotate` | `Annotate` | `*` | `{name} Annotated` | Quality 95 |

  - `tools.SubPresetIds = ["crop","add-background","redact","annotate","compress-image","strip-metadata"]`(6칸)
- `UserDataStore`의 "없는 내장 도구 추가" 단계는 스키마와 상관없이 불러올 때마다 돈다. 위 "스펙과 다르게 정한 것 2"의 규칙을 따른다.
- 아이콘은 Material Design Icons 공식 SVG의 path를 그대로 옮긴다.
  - `Icon.Donut.AddBackground` = `image-frame`
  - `Icon.Donut.Redact` = `eye-off-outline`
  - `Icon.Donut.Annotate` = `draw`
- i18n:
  - `preset.add_background` = "배경 넣기", `.short` = "배경"
  - `preset.redact` = "가리기", `.short` = "가리기"
  - `preset.annotate` = "그리기", `.short` = "그리기"
  - 다른 언어는 번역한다.

- [ ] **Step 1: 실패하는 테스트**
  - `Tools_group_has_six_tools_in_spec_order`
  - `Three_new_tool_presets_have_their_targets_and_names`: 표의 값을 그대로 확인한다.
  - `Edit_with_drawing_counts_as_a_transform`
  - `Image_edit_round_trips_through_json`
    - 네 가지 편집을 모두 채운 `Preset`을 저장했다가 불러와도 값이 같다.
  - `A_library_from_1_0_0_gains_the_new_tools_once`
    - `tools`에 3칸만 있는 presets.json에 새 프리셋 3개가 들어가고, 그룹이 6칸이 된다.
    - 그룹에서 `redact`를 빼고 다시 불러와도 다시 들어오지 않는다.
  - `Job_with_per_file_edits_gives_each_file_its_own_edit`
    - 가짜 변환기가 파일마다 받은 `Edit`를 기록한다.
  - `Every_tool_ring_slice_has_an_icon`: 새 id 3개를 `InlineData`에 추가한다.
  - `Tools_ring_names_exist`: 새 id 3개를 추가한다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 커밋한다.

---

### Task 3: 렌더러 기초, 가리기, 배경 넣기 (저장 경로까지)

**Files:**
- Create:
  - `src/Filee.Engines/Magick/ImageEditRenderer.cs` (public)
  - `src/Filee.Engines/Magick/EditBridge.cs` (Magick ↔ SKBitmap)
- Modify: `src/Filee.Engines/Magick/ImageEncoder.cs:236` (자르기 바로 뒤)
- Test: `tests/Filee.Engines.Tests/ImageEditTests.cs` (새 파일, `IClassFixture<EngineFixture>`, `ImageToolTests`의 도우미 방식)

**Interfaces (Produces)**

```csharp
public static class ImageEditRenderer
{
    /// <summary>Output size and where the picture sits in it (pixels), for a w×h picture and an optional background.</summary>
    public static (SKSizeI Output, SKRect Picture) Layout(int width, int height, BackgroundEdit? background);
    /// <summary>Draws the whole result into <paramref name="output"/> (any scale): background, then the picture,
    /// its redaction and its annotations. Preview and save both call this.</summary>
    public static void Draw(SKCanvas canvas, SKImage picture, SKRect output, ImageEdit edit, int fullShortSide);
    /// <summary>Renders at full size into a new bitmap (save path).</summary>
    public static SKBitmap Render(SKBitmap picture, ImageEdit edit);
}
```

`fullShortSide`는 원래 크기 그림의 짧은 변(픽셀)이다. 모자이크 칸의 8px 하한을 미리보기에서도 똑같이 지키는 데 쓴다.

**값**
- 순서: 가리기 → 그리기 → 배경(스펙 3절: 방향 → 자르기 → 가리기 → 주석 → 배경).
- 가리기
  - 검은 상자: `#000000`으로 꽉 채운다.
  - 모자이크 칸 = `max(8px, 짧은 변/40)`. 상자 안을 칸마다 평균색으로 채운다. 칸 격자는 그림의 왼쪽 위에 맞춘다.
- 배경 넣기
  - 여백 p = `Padding × 그림 짧은 변`. `auto` 결과 크기는 `(w + 2p, h + 2p)`다.
  - 고정 비율일 때는 `(w + 2p, h + 2p)`를 담는 가장 작은 그 비율의 캔버스를 만들고, 그림을 가운데 둔다.
  - 모서리 반지름은 `Corner × 그림 짧은 변`이다.
  - 그림자
    - 흐림 sigma = `Shadow/100 × 0.03 × 그림 짧은 변`
    - 아래로 `sigma × 0.5`만큼 내린다.
    - 검정, 불투명도 `0.45 × Shadow/100`
  - 그라데이션은 135°다(id: 시작 → 끝).
    - `peach` #FFD3A5→#FD6585
    - `sunset` #FF9A8B→#FF6A88
    - `gold` #F6D365→#FDA085
    - `mint` #D4FC79→#96E6A1
    - `sky` #89F7FE→#66A6FF
    - `ocean` #2E3192→#1BFFFF
    - `lavender` #A18CD1→#FBC2EB
    - `night` #232526→#414345
  - 단색:
    - 흰색 #FFFFFF, 검정 #000000, 회색 #8E8E93
    - 피치 #FFD8BE, 하늘 #BEE3FF, 민트 #BFF0DC
  - 사용자 그림은 캔버스를 꽉 채우도록 자른다. 파일을 못 읽으면 흰색으로 그린다.
  - 투명한 그림은 배경 위에 그대로 합성한다.
- 저장 경로: `ApplyOptions`에서 자르기 다음에 `edit.Draws`가 참이면 아래 순서로 처리한다.
  1. RGBA8888(unpremul)로 SKBitmap에 담는다.
  2. `Render`로 그린다.
  3. 같은 크기면 픽셀만 되돌리고, 크기가 바뀌면(배경) 새 MagickImage에 담는다. 새로 담을 때는 ICC·EXIF 프로필을 옮긴다.
  4. 가리기가 있으면 `KeepMetadata`와 상관없이 `Strip()`해서 EXIF 미리보기도 지운다. ICC는 기존 규칙대로 남긴다.

- [ ] **Step 1: 실패하는 테스트**
  - `Black_box_turns_every_pixel_in_it_black`
  - `Mosaic_cells_are_uniform_and_at_least_8px`
    - 400×300 노이즈 그림을 쓴다. 칸 = max(8, 7.5) = 8px.
    - 칸 하나 안의 모든 픽셀이 같아야 한다.
  - `Redacted_output_has_no_exif_and_no_thumbnail`
    - EXIF와 썸네일이 있는 JPG를 쓴다.
  - `Redact_on_a_turned_phone_photo_hits_the_visible_spot`
    - 방향 6 JPG를 쓴다.
    - 보이는 그림의 왼쪽 위 1/4을 가리면, 결과의 왼쪽 위가 검정이다.
  - `Auto_background_adds_padding_on_every_side`
    - 결과 크기는 (w + 2p, h + 2p)이고, PNG다.
  - `Fixed_ratio_background_is_exactly_16_by_9`
  - `Outside_a_rounded_corner_is_background`
    - 그림 모서리 바로 바깥 픽셀이 배경색이다.
  - `Transparent_logo_shows_the_background_through`
    - 투명 PNG를 쓴다. 투명했던 픽셀이 배경색이다.
  - `Preview_scale_matches_full_size`
    - 25% 크기로 `Draw`한 결과와 100%로 `Render`해서 25%로 줄인 결과의 평균 차이가 채널당 6 이하다.
    - 가리기(모자이크)와 배경을 모두 넣는다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 커밋한다.

---

### Task 4: 렌더러, 그리기 개체와 한글 글자

**Files:**
- Modify: `src/Filee.Engines/Magick/ImageEditRenderer.cs`
- Create: `src/Filee.Engines/Magick/AnnotationFonts.cs`
  - 글꼴을 고르는 순서: 맑은 고딕 → `SKFontManager.MatchCharacter('가')` → 기본 글꼴.
  - `Cad/CadFonts.cs`의 방식을 참고한다.
- Test: `tests/Filee.Engines.Tests/ImageEditTests.cs`

**값** (모두 그림 짧은 변 S에 대한 비율)

| 굵기 | 선 | 글자 크기 | 번호 스티커 지름 |
|---|---|---|---|
| Thin | 0.004·S | 0.030·S | 0.050·S |
| Normal | 0.008·S | 0.045·S | 0.065·S |
| Thick | 0.016·S | 0.070·S | 0.090·S |

- **펜:** 둥근 끝과 둥근 이음으로 점들을 잇는다.
- **형광펜:** 선 굵기는 3배이고 불투명도는 0.4다. 한 획 안에서 겹치는 곳은 진해지지 않게 레이어 하나로 그린다.
- **화살표, 선:** 화살표 머리는 길이 4×선 굵기, 너비 3×선 굵기의 꽉 찬 삼각형이다.
- **사각형, 원:** 테두리만 그린다.
- **글자:** `Points[0]`은 글자 상자의 왼쪽 위다. 여러 줄을 허용한다(`\n`).
- **번호:** 색으로 채운 원 안에 흰 숫자를 그린다. 원 색의 밝기가 0.6보다 크면 숫자를 검정으로 그린다.
- **기본 색 8개:**
  - 빨강 #FF3B30, 주황 #FF9500, 노랑 #FFCC00, 초록 #34C759
  - 파랑 #007AFF, 보라 #AF52DE, 검정 #000000, 흰색 #FFFFFF

- [ ] **Step 1: 실패하는 테스트**
  - `Red_line_changes_the_pixels_it_crosses`
  - `Korean_text_draws_glyphs_not_boxes`
    - "가나다"를 그린 영역과 "□□□"(U+25A1)를 그린 영역의 픽셀이 다르다.
    - 글자 영역의 픽셀이 빈 그림과도 다르다.
  - `Arrow_has_a_head_at_its_end`
  - `Number_sticker_uses_black_digits_on_yellow`
  - `Highlighter_overlap_inside_one_stroke_does_not_darken`
  - `Annotation_on_a_turned_phone_photo_lands_where_drawn`
  - `Preview_scale_matches_full_size_with_annotations`: Task 3의 같은 테스트에 개체 8종을 더한다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 커밋한다.

---

### Task 5: 편집 창 뼈대를 도구 넷으로 넓히기

자르기 동작은 바뀌면 안 된다. 기존 `ImageToolWindowTests`와 `ImageToolDonutTests`가 그대로 통과해야 한다.

**Files:**
- Modify:
  - `src/Filee.App/ViewModels/ImageToolViewModel.cs`: 공용 부분과 자르기를 나눈다.
  - `src/Filee.App/Views/ImageToolWindow.axaml(.cs)`
  - `src/Filee.App/Controls/ImageCanvas.cs`
  - `src/Filee.App/Services/WindowService.cs:223`
  - `src/Filee.App/Services/RadialController.cs:354-391` (`OpenTool`)
- Create:
  - `src/Filee.App/ViewModels/Tools/CropToolViewModel.cs`
  - `src/Filee.App/Controls/PictureView.cs`: 확대·이동·`ImageRect`·`Fit`·`ShowActualSize`·Space와 가운데 버튼으로 이동을 `ImageCanvas`에서 옮긴다.
  - `src/Filee.App/Controls/EditPreviewDrawOperation.cs`: `ICustomDrawOperation`. `ISkiaSharpApiLeaseFeature`로 SKCanvas를 빌려 `ImageEditRenderer.Draw`를 부른다.
- Test: 기존 테스트와 `tests/Filee.App.Tests/ImageToolShellTests.cs`

**Interfaces (Produces)**
- `abstract partial class ImageToolViewModel`
  - 공용 멤버: `Tool`, `Files`, `Index`, `Count`, `HasMany`, `Title`, `PositionText`, `FooterText`, `Preview`, `PreviewImage`(SKImage), `FullShortSide`
  - 공용 명령: `Next`, `Previous`, `Reset`, `Undo`, `Redo`, `Save`, `Cancel`
  - `ToolResult? Result`, `event Finished`
  - `static ImageToolViewModel Create(ImageToolKind tool, IReadOnlyList<string> files, UserDataStore store, ILocalizer loc, ILogger? log = null)`
  - 도구가 채우는 곳: `protected abstract ToolResult? BuildResult()`, `protected abstract void ResetTool()`, 되돌리기 스냅샷.
- `public sealed record ToolResult(ImageEdit? Shared, IReadOnlyDictionary<string, ImageEdit>? PerFile)`
- `WindowService.EditImageAsync(...)`는 `Task<ToolResult?>`를 돌려준다(virtual 유지).
- `OpenTool`은 `Shared`가 있으면 기존대로 돌리고, `PerFile`이 있으면 `conversions.Start(perFile.Keys.ToList(), preset, perFile)`로 돌린다.
- 창의 오른쪽 패널은 도구별 `UserControl`이다(`Views/Tools/<Name>Panel.axaml`). 이 작업에서는 자르기만 연결하고, 나머지 셋은 각자의 작업에서 연결한다.
- 키 처리
  - 공용 키(Enter, Esc, Ctrl+Z/Y, Ctrl+0/1, ←/→, Space)는 창이 처리한다.
  - 도구 키(자르기 1~7 등)는 `ImageToolViewModel.HandleKey(Key, KeyModifiers)`(virtual, 처리하면 true)로 넘긴다.
  - 글자 입력 칸에 포커스가 있으면 도구 키는 넘기지 않는다.

- [ ] **Step 1: 실패하는 테스트**
  - `Create_returns_the_crop_view_model_for_crop`
  - `Tool_result_with_per_file_edits_starts_one_job_with_them`
    - `FakeToolWindows`가 `PerFile`을 돌려준다.
    - 작업 하나, 파일 2개, 파일마다 다른 `Edit`.
  - `Draw_operation_renders_the_edit_into_the_canvas`
    - 헤드리스 렌더로 확인한다. 검은 상자 하나를 넣은 `ImageEdit`를 그리면, 화면의 그 자리 픽셀이 검정이다.
  - 기존 자르기 테스트가 모두 통과한다. 생성자 호출은 `Create`로 바꾼다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 커밋한다.

---

### Task 6: 배경 넣기 창

**Files:**
- Create:
  - `src/Filee.App/ViewModels/Tools/BackgroundToolViewModel.cs`
  - `src/Filee.App/Views/Tools/BackgroundPanel.axaml(.cs)`
- Modify: 창의 패널 연결, i18n 6개
- Test: `tests/Filee.App.Tests/BackgroundToolTests.cs`

**동작** (스펙 3.2)
- 미리보기는 결과 전체(배경 포함)를 `EditPreviewDrawOperation`으로 그린다. 그림 위에서 끄는 동작은 없고, 확대와 이동만 된다.
- 패널 구성
  - **배경:** 그라데이션 8개, 단색 6개, 사용자 색, 사용자 그림. 견본은 둥근 칩으로 둔다.
    - 사용자 색: 기존 테마 페이지의 색 입력 방식(#RRGGBB)을 쓴다.
    - 사용자 그림: `IStorageProvider`로 이미지 파일 하나를 고른다.
  - **슬라이더:** 여백(0~40%), 모서리(0~25%), 그림자(0~100).
    - 옆에 보고 있는 파일 기준 픽셀 값을 보여 준다(예: "609px"). 그림자는 숫자만 보여 준다.
  - **비율 칩:** 자동, 1:1, 16:9, 9:16, 4:5, 4:3
- 설정 하나를 모든 파일에 쓴다(`Shared`). 처음 열면 `ImageToolSettings.Background`로 시작한다. 저장하면 그 값을 기억한다(`ImagePath`는 빼고).
- 슬라이더 한 번 끌기를 되돌리기 한 단계로 친다.
- i18n
  - `tool.background.title` "배경 넣기"
  - `.fill` "배경", `.padding` "여백", `.corner` "모서리", `.shadow` "그림자", `.ratio` "비율", `.auto` "자동"
  - `.custom_color` "사용자 색", `.custom_image` "그림 고르기…"
  - 견본 이름(접근성용): `tool.background.swatch.<id>`

- [ ] **Step 1: 실패하는 테스트**
  - `Opens_with_the_remembered_background`
  - `Padding_slider_shows_pixels_of_the_viewed_file`
    - 1000×800 그림, 8%일 때 "64px"(짧은 변 800 × 0.08).
  - `Save_returns_a_shared_background_edit_and_remembers_it_without_the_image_path`
  - `Undo_restores_the_previous_padding`
  - 렌더 스크린샷 `tool-background-ko.png`를 남긴다(눈으로 확인용).
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 스크린샷을 본 뒤 커밋한다.

---

### Task 7: 가리기 창

**Files:**
- Create:
  - `src/Filee.App/ViewModels/Tools/RedactToolViewModel.cs`
  - `src/Filee.App/Views/Tools/RedactPanel.axaml(.cs)`
  - `src/Filee.App/Controls/RedactCanvas.cs` (`PictureView`를 상속)
- Modify: 창의 패널 연결, i18n 6개
- Test: `tests/Filee.App.Tests/RedactToolTests.cs`

**동작** (스펙 3.3)
- 빈 곳을 끌면 새 상자가 생긴다. 상자를 클릭하면 고르고, 끌면 옮겨지고, 8개 손잡이로 크기를 바꾼다. 손잡이 계산은 `ImageCanvas.Resize`를 다시 쓴다.
- Delete나 Backspace로 고른 상자를 지운다. 끄는 동작 한 번이 되돌리기 한 단계다.
- 상자는 그림 밖으로 나가지 않게 자른다. 0.5% 미만으로 작은 상자는 만들지 않는다.
- 미리보기에 가리기 결과(검정이나 모자이크)를 렌더러로 그린다. 고른 상자에는 테두리와 손잡이를 덧그린다.
- 패널: 방식(검은 상자 / 모자이크) 두 칸 토글과 "상자 N개" 표시. 단축키는 B(검은 상자), M(모자이크).
- 모든 파일에 같은 상자를 쓴다(`Shared`, 스펙). 상자가 0개면 저장 버튼이 꺼진다.
- 아래쪽에 "결과에서 사진 정보(EXIF)도 지워져요"라는 안내를 상시 보여 준다.
- i18n
  - `tool.redact.title` "가리기"
  - `.mode` "방식", `.black` "검은 상자", `.mosaic` "모자이크"
  - `.count` "상자 {0}개"
  - `.hint` "그림 위를 끌어서 가릴 곳을 고르세요"
  - `.metadata_note` "결과에서 사진 정보(EXIF)도 지워져요"

- [ ] **Step 1: 실패하는 테스트**
  - `Dragging_on_the_picture_adds_a_box`
  - `Delete_removes_the_selected_box`
  - `Box_is_clamped_inside_the_picture`
  - `Mode_key_M_switches_to_mosaic`
  - `Save_is_off_without_boxes`
  - `Save_returns_shared_redact_edit_and_remembers_mode`
  - 렌더 스크린샷 `tool-redact-ko.png`를 남긴다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 스크린샷을 본 뒤 커밋한다.

---

### Task 8: 그리기 창

**Files:**
- Create:
  - `src/Filee.App/ViewModels/Tools/AnnotateToolViewModel.cs`
  - `src/Filee.App/Views/Tools/AnnotatePanel.axaml(.cs)`
  - `src/Filee.App/Controls/AnnotateCanvas.cs`
- Modify: 창의 패널 연결, i18n 6개
- Test: `tests/Filee.App.Tests/AnnotateToolTests.cs`

**동작** (스펙 3.4)
- 도구와 단축키(글자 입력 중이 아닐 때):

  | 도구 | 키 |
  |---|---|
  | 선택 | V |
  | 펜 | P |
  | 형광펜 | H |
  | 화살표 | A |
  | 선 | L |
  | 사각형 | R |
  | 원 | E |
  | 글자 | T |
  | 번호 | N |

  - 패널에 도구 9칸(아이콘 + 툴팁), 색 8개와 사용자 색, 굵기 3단을 둔다. `[` `]` 키로 굵기를 바꾼다.
- 끄는 동안 개체를 렌더러로 그린다. 손을 떼면 개체 하나가 생기고, 그게 되돌리기 한 단계다.
- Shift를 누르면 선과 화살표는 45° 단위로 꺾이고, 사각형과 원은 정사각형과 정원이 된다.
- **선택(V)**
  - 클릭한 개체를 고른다. 위에 있는 것이 먼저 잡히고, 선 근처 판정 거리는 화면 6px이다.
  - 끌면 옮기고, Delete로 지운다. 고른 개체에는 점선 테두리를 그린다.
- **글자(T)**
  - 클릭한 곳에 입력 칸(TextBox)이 겹쳐 뜬다.
  - Enter는 확정, Shift+Enter는 줄바꿈이다. Esc나 빈 글자는 만들지 않고 닫는다.
  - 글자 개체를 더블클릭하면 다시 고친다.
- **번호(N):** 클릭할 때마다 그 파일에 있는 가장 큰 번호 + 1을 붙인다.
- **파일마다 따로 그린다.** 되돌리기 스택도 파일마다 따로 둔다.
  - 아래쪽 안내는 "표시한 {0}개만 저장돼요"이고, 표시한 파일이 하나라도 있을 때만 보인다.
  - 한 장도 그리지 않았으면 저장 버튼이 꺼진다.
  - `BuildResult`는 그린 파일만 담은 `PerFile`을 돌려준다.
- Esc를 누를 때 그린 게 있으면 한 번 묻는다("그린 내용을 버리고 닫을까요?"). 기존 확인 창 방식을 쓴다.
- 마지막 도구, 색, 굵기를 기억한다.
- i18n
  - `tool.annotate.title` "그리기"
  - 도구 이름 9개: `tool.annotate.tool.<kind>`
  - `.color` "색", `.width` "굵기", `.thin` "가늘게", `.normal` "보통", `.thick` "굵게"
  - `.saved_count` "표시한 {0}개만 저장돼요"
  - `.discard_title` "그린 내용을 버리고 닫을까요?"
  - `.custom_color` "사용자 색"

- [ ] **Step 1: 실패하는 테스트**
  - `Pen_drag_adds_one_pen_annotation`
  - `Shift_snaps_a_line_to_45_degrees`
  - `Shift_makes_a_square`
  - `Number_tool_counts_per_file`
    - 파일 1에 두 번, 파일 2에 한 번 붙이면 1, 2와 1이 된다.
  - `Undo_only_touches_the_current_file`
  - `Text_tool_commits_on_enter_and_edits_on_double_click`
  - `Select_and_delete_removes_the_annotation`
  - `Save_returns_per_file_edits_for_marked_files_only`
  - `Esc_with_drawings_asks_once`
  - `Tool_keys_are_ignored_while_typing_text`
  - 렌더 스크린샷 `tool-annotate-ko.png`를 남긴다. 펜, 화살표, 한글 글자, 번호 2개를 넣는다.
- [ ] **Step 2:** 테스트를 돌려 실패를 확인한다.
- [ ] **Step 3:** 구현한다.
- [ ] **Step 4:** 테스트를 돌려 통과를 확인하고 스크린샷을 본 뒤 커밋한다.

---

### Task 9: 1.1.0, 설치 파일, 실제 PC 확인

이 작업은 최종 검토와 그 수정이 끝난 뒤에 한다.

**Files:** `Directory.Build.props`(`Version` 1.1.0), 필요하면 `README.ko.md`의 기능 목록

- [ ] **Step 1:** `dotnet format --verify-no-changes`를 돌린다. 테스트 프로젝트 5개를 Release로 하나씩 돌린다(빌드는 한 번, `--no-build`). 기준선 말고는 새 실패가 없어야 한다.
- [ ] **Step 2:** 휴대용 PowerShell 7로 `build/build-installer.ps1`을 돌린다. `Releases/FileePlus-1.1.0-win-Setup.exe`와 체크섬 파일이 나와야 한다.
- [ ] **Step 3:** 사용자에게 설치 파일 경로와 확인 목록을 준다.
  - 이미 1.0.0을 깔았던 PC에서 도구 링에 새 칸 셋이 생긴다.
  - 홈 화면 안내가 "Ctrl"로 바뀐다. 설정 › 동작에 Shift + 드래그가 꺼진 채 있다.
  - 배경 넣기: 그라데이션·단색·사용자 그림, 투명 로고, 16:9
  - 가리기: 검은 상자와 모자이크. 결과 파일 속성에 위치나 카메라 정보가 없다.
  - 그리기: 펜, 화살표, 한글 글자, 번호. 여러 장에서 따로 그려진다.
  - 휴대폰 사진(HEIC/JPG 세로)에서 그린 자리가 결과와 같다.
- [ ] **Step 4:** 발견한 문제는 각각 고쳐서 커밋한다. 릴리스(태그 푸시)는 사용자에게 묻고 한다.
