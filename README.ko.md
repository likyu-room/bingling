<h1 align="center">빙글링 (Bingling)</h1>

<p align="center">
  <b>파일을 끌다가 <kbd>Ctrl</kbd>을 누르면 커서 둘레에 링이 열려요. 원하는 칸에 놓기만 하면 끝.</b><br>
  형식 변환부터 자르기, 가리기, 그리기까지 탐색기를 떠나지 않고 해요.
</p>

<p align="center">
  <a href="../../releases/latest"><b>다운로드</b></a> ·
  <a href="docs/manual/ko.md">사용 설명서</a> ·
  <a href="#쓰는-법">쓰는 법</a> ·
  <a href="#설치">설치</a> ·
  <a href="#명령줄">명령줄</a> ·
  <a href="#개발">개발</a>
</p>

<p align="center"><a href="README.md">English</a> · <b>한국어</b> · <a href="README.zh-CN.md">简体中文</a></p>

## 쓰는 법

1. 탐색기에서 파일을 끌어요.
2. 끄는 중에 <kbd>Ctrl</kbd>을 누르면 커서 둘레에 링이 열려요. 끌고 있는 파일에 맞는 칸만 나와요.
3. 원하는 칸 위에 놓아요. 변환한 파일은 원본 옆에 저장돼요.

링 대신 탐색기 우클릭 메뉴(“빙글링으로 변환”), 키보드 단축키, 창에 끌어다 놓기, 자동 변환 폴더, [명령줄](#명령줄)도 쓸 수 있어요.

## 주요 기능

- **형식 변환**: 이미지, PDF, 워드·엑셀·파워포인트, 한글(HWP), 전자책, 동영상·오디오, 압축 파일, 글꼴, CAD, 3D 모델 등.
  여러 파일을 한꺼번에 변환하고, 여러 단계가 필요한 변환도 알아서 이어 줘요.
  일부 형식은 추가 엔진이 필요하고, 그때만 내려받아요([엔진](docs/ENGINES.md)).
- **사진 도구**: 링의 “도구” 칸에 모여 있어요.
  - 자르기
  - 배경 넣기: 그라데이션, 단색, 내 사진, 사진 색
  - 가리기: 검은 상자 또는 모자이크. 사진 정보(EXIF)도 함께 지워요.
  - 그리기: 펜, 형광펜, 화살표, 도형, 글자, 번호
  - 압축
  - 사진 정보 지우기

  편집 창에서 **복사**(<kbd>Ctrl</kbd>+<kbd>C</kbd>)를 누르면 파일을 만들지 않고 결과를 바로 붙여 넣을 수 있어요.
- **피치 글래스 테마**: 은은하게 비치는 유리 느낌의 테마예요. 밝은 모드와 어두운 모드가 있어요.
- **알림 카드**: 변환이 끝나면 카드가 떠요. “복사”를 누르면 결과를 대화창이나 문서에 붙여 넣을 수 있고, 사진을 누르면 결과가 열려요.
- **여섯 가지 언어**: 한국어, English, 简体中文, Español, Polski, हिन्दी
- **내 컴퓨터 안에서만**: 파일을 어디에도 올리지 않아요.

## 설치

Windows 10/11용이에요.

1. [릴리스 페이지](../../releases/latest)에서 `Bingling-<버전>-win-Setup.exe`를 받아 실행해요.
2. 설치 파일에 아직 코드 서명이 없어서 Windows SmartScreen이 “Windows의 PC 보호” 창을 띄울 수 있어요.
   **추가 정보**를 누른 다음 **실행**을 누르세요.
3. 모든 사용자용(관리자 권한 필요)으로 설치하거나, 회사 PC처럼 관리자 권한이 없으면 내 계정에만 설치할 수 있어요.
4. 설치할 때 탐색기 메뉴, 로그인 시 자동 실행, `bingling` 명령, 추가 엔진을 고를 수 있어요. `bingling` 명령은 기본으로 꺼져 있어요.

새 버전이 나오면 알려 주고, 설정과 엔진은 그대로 둔 채 업데이트돼요.
이전 이름인 Filee Plus 1.1.0을 쓰고 있다면 빙글링을 설치하세요. 설치 프로그램이 Filee Plus를 지우고, 설정과 내려받은 엔진은 빙글링으로 옮겨요.

**설치 없이 쓰기(포터블)**: `Bingling-<버전>-win-Portable.zip`을 받아 원하는 폴더에 풀고 `Bingling.exe`를 실행해요.
업데이트는 새 ZIP을 같은 폴더에 덮어 풀면 돼요. 자세한 건 [사용 설명서](docs/manual/ko.md#설치-없이-쓰기포터블)에 있어요.

## 명령줄

설치할 때 고르면 `bingling` 명령도 써요. 앱과 같은 엔진과 프리셋을 써요.

```
bingling convert photo.heic --to jpg
bingling convert *.png --to webp --quality 80 -o converted
bingling watch D:\Inbox --to pdf --move-originals
```

## 개발

.NET 10 SDK가 필요해요.

```bash
dotnet run --project src/Filee.App
dotnet test tests/Filee.Core.Tests
```

테스트는 프로젝트별로 하나씩 돌려요(`tests/Filee.App.Tests`, `Filee.Platform.Tests`, `Filee.Cli.Tests`, `Filee.Engines.Tests`).
[구조](docs/ARCHITECTURE.md) · [엔진](docs/ENGINES.md) · [변환기 추가](docs/ADDING-A-CONVERTER.md) ·
[언어 추가](docs/ADDING-A-LANGUAGE.md) · [기여 안내](CONTRIBUTING.md)

코드의 프로젝트 이름(`Filee.*`)은 원작 그대로예요. 실행 파일은 `Bingling.exe`, 설정 폴더는 `%APPDATA%\Bingling`이에요.

## 원작

빙글링은 KnifeLemon의 [Filee](https://github.com/KnifeLemon/Filee)(MIT 라이선스) 1.12.0을 바탕으로 만들었어요.

## 라이선스

MIT, © 2026 Beomsuk Kim (LikeU). Filee 기반, © 2026 Myeonggu Kim (KnifeLemon), Filee 기여자.
전문은 [LICENSE](LICENSE)에, 포함된 서드파티 구성요소와 각각의 라이선스는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 있어요.
