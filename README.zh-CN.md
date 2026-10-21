<h1 align="center">Bingling</h1>

<p align="center">
  <b>拖动文件时按下 <kbd>Ctrl</kbd>，光标周围会打开一个圆环，放到想要的格子里就完成了。</b><br>
  转换格式、裁剪、遮挡、标注，都不用离开资源管理器。
</p>

<p align="center">
  <a href="../../releases/latest"><b>下载</b></a> ·
  <a href="#使用方法">使用方法</a> ·
  <a href="#安装">安装</a> ·
  <a href="#命令行">命令行</a> ·
  <a href="#开发">开发</a>
</p>

<p align="center"><a href="README.md">English</a> · <a href="README.ko.md">한국어</a> · <b>简体中文</b></p>

## 使用方法

1. 在资源管理器中拖动文件。
2. 拖动时按下 <kbd>Ctrl</kbd>，光标周围会打开圆环，只显示适合当前文件的格子。
3. 把文件放到想要的格子上，结果会保存在原文件旁边。

除了圆环，也可以用资源管理器右键菜单（“用 Bingling 转换”）、键盘快捷键、拖放窗口、监视文件夹和[命令行](#命令行)。

## 功能

- **格式转换**：图片、PDF、Word、Excel、PowerPoint、HWP、电子书、音视频、压缩包、字体、CAD、3D 模型等。
  可以一次转换多个文件，需要多个步骤的转换也会自动衔接。
  部分格式需要额外的引擎，只在需要时才下载（[引擎](docs/ENGINES.md)）。
- **图片工具**，集中在圆环的“工具”格里：
  - 裁剪
  - 添加背景：渐变、纯色、自己的图片或图片本身的颜色
  - 遮挡：黑框或马赛克，结果中的照片信息（EXIF）也会一并清除
  - 标注：画笔、荧光笔、箭头、形状、文字、编号
  - 压缩
  - 清除元数据
- **蜜桃玻璃主题**：柔和通透的玻璃质感，有浅色和深色两种模式。
- **结果卡片**：转换完成后会弹出卡片。点“复制”可以把结果粘贴到聊天或文档里，点图片则打开结果。
- **六种语言**：简体中文、English、한국어、Español、Polski、हिन्दी
- **只在你的电脑上**：不会上传任何文件。

## 安装

适用于 Windows 10 和 11。

1. 从[最新发布](../../releases/latest)下载 `Bingling-<版本>-win-Setup.exe` 并运行。
2. 安装程序目前还没有代码签名，Windows SmartScreen 可能会显示“Windows 已保护你的电脑”。
   点击**更多信息**，再点击**仍要运行**。
3. 安装时可以选择资源管理器菜单、登录时启动、`bingling` 命令（默认不勾选）和额外的引擎。

有新版本时 Bingling 会提示，更新时保留设置和引擎。
如果你在用旧名称 Filee Plus 的 1.1.0，直接安装 Bingling 即可：安装程序会卸载 Filee Plus，并把设置和已下载的引擎转移过来。

## 命令行

安装时如果勾选了，`bingling` 命令和应用使用相同的引擎与预设。

```
bingling convert photo.heic --to jpg
bingling convert *.png --to webp --quality 80 -o converted
bingling watch D:\Inbox --to pdf --move-originals
```

## 开发

需要 .NET 10 SDK。

```bash
dotnet run --project src/Filee.App
dotnet test tests/Filee.Core.Tests
```

测试项目请逐个运行（`tests/Filee.App.Tests`、`Filee.Platform.Tests`、`Filee.Cli.Tests`、`Filee.Engines.Tests`）。
[架构](docs/ARCHITECTURE.md) · [引擎](docs/ENGINES.md) · [添加转换器](docs/ADDING-A-CONVERTER.md) ·
[添加语言](docs/ADDING-A-LANGUAGE.md) · [贡献指南](CONTRIBUTING.md)

代码项目沿用原作的名称（`Filee.*`）。程序是 `Bingling.exe`，设置在 `%APPDATA%\Bingling` 中。

## 致谢

Bingling 基于 KnifeLemon 的 [Filee](https://github.com/KnifeLemon/Filee) 1.12.0（MIT 许可证）制作。
如果你喜欢原作，也请支持原作者：[GitHub Sponsors](https://github.com/sponsors/KnifeLemon)

## 许可证

MIT，© 2026 Beomsuk Kim (LikeU)。基于 Filee，© 2026 Myeonggu Kim (KnifeLemon) 和 Filee 贡献者。
完整条款见 [LICENSE](LICENSE)，所含第三方组件及其许可证列在 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
