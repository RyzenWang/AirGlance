# 架构

[English](architecture.md) · **简体中文**

AirGlance 0.7.0 是一款桌面图像查看器。它一次打开一个文件夹，并一次显示一张图像。代码拆分为一个不依赖 UI 的核心库和一个 Avalonia 应用程序。

## 技术栈

| 组件 | 版本 | 用途 |
| --- | --- | --- |
| .NET | 10 | 两个项目的运行时和构建目标 |
| Avalonia | 11.3.22 | 应用窗口、输入、渲染、对话框 |
| CommunityToolkit.Mvvm | 8.4.2 | 可观察视图模型和命令 |
| SkiaSharp | 4.150.1 | 图像解码与编码 |
| Svg.Skia | 5.2.3 | SVG 解析与光栅化 |
| BitMiracle.LibTiff.NET | 2.4.660 | TIFF 第一页解码 |
| libavif + dav1d | 1.4.2 / 1.5.5 | Windows x64/arm64、macOS 和 Linux 上通过随包原生 shim 解码 AVIF |
| NativeAOT | .NET 10 | 在 Windows、macOS 和 Linux 上的发布 |

SkiaSharp 3.x 系列已被取代。4.150.1 是能够在 Windows、macOS 和 Linux 上与 Avalonia 11.3.22 的原生资产图统一的版本。所有项目都使用 `Directory.Packages.props` 中的版本；不要把 3.x 的 SkiaSharp 包混入该依赖图。

`AirGlance.App` 启用了 `AvaloniaUseCompiledBindingsByDefault`。每个数据绑定的 XAML 文件都声明 `x:DataType`，因此绑定错误会变成生成错误，而不是运行时警告。设置使用源生成的 `JsonSerializerContext`；没有基于反射的 JSON。

## 分层

```text
AirGlance.App (WinExe, Avalonia)
    references AirGlance.Core

AirGlance.Core (net10.0, SkiaSharp + Svg.Skia)
```

`AirGlance.Core` 不引用 Avalonia，也不依赖 UI 线程。它包含导航、解码、变换、导出、元数据读取器、设置和缩略图缓存。它标记为 `IsAotCompatible`。

`AirGlance.App` 拥有窗口、输入处理、UI 线程、本地化、主题、剪贴板访问、shell 集成和后台服务。它引用 `AirGlance.Core`；依赖关系从不反向指向。

这条规则是有意为之：

- 核心操作是同步的，并在工作可以被中止的地方接收 `CancellationToken`。由调用方决定在哪个线程上运行它们。
- App 代码不重新实现解码、排序、导航或设置逻辑。
- 视图模型不拥有窗口或控件。`MainWindow` 的代码隐藏处理平台层面的窗口和输入工作。
- 核心测试无需窗口即可运行。App 测试使用 Avalonia 的无头平台。

## 项目结构

### Core

`src/AirGlance.Core/Files`

- `SupportedFormats` 是扩展名的准入门槛：`.jpg`、`.jpeg`、`.jfif`、`.png`、`.webp`、`.avif`、`.bmp`、`.gif`、`.ico`、`.svg`、`.tif`、`.tiff`。扩展名检查不区分大小写。当无法询问编解码器时，`Describe` 会提供一个格式标签。只有对应 RID 的原生解码器存在时（`SupportsAvif`）才会列出 `.avif`；没有原生库的构建会隐藏该格式，而不是宣称支持一个打不开的扩展名。
- `NaturalStringComparer` 按数值比较连续数字，因此 `img2.jpg` 排在 `img10.jpg` 之前。数值相同但前导零更少的排在前面；其余并列项用序数比较打破平局。`NaturalPathComparer` 按文件名对完整路径排序。
- `FolderScanner.EnumerateImages` 只列出一个文件夹，不递归。隐藏文件和系统文件会被跳过，无法访问的条目会被忽略，只保留受支持的扩展名，结果按自然顺序排序。降序会反转该列表。
- `ImageSourceResolver` 将路径转换为一个文件夹、一个文件列表和一个索引。文件会在其所在文件夹中选中自身；文件夹从它的第一张图像开始。直接打开的隐藏文件即使被扫描跳过，仍会单独打开。对于直接打开的文件，如果文件夹扫描失败，则回退到该单个文件。

`src/AirGlance.Core/Navigation`

- `ImageNavigator` 持有文件列表和当前索引。`MoveNext` 和 `MovePrevious` 在两端环绕，并报告 `Wrapped`，以便 UI 显示提示消息。`GoTo` 会钳制到有效索引且不环绕。`Reset` 会替换列表。

`src/AirGlance.Core/Imaging`

- `ImageDecoder` 是位图解码路径。它使用 `FileShare.ReadWrite | FileShare.Delete` 打开文件，创建 `SKCodec`，读取 `SKCodec.EncodedOrigin`，并将报告的 EXIF 方向（1–8）应用到解码后的像素。对于预览，它向 `SKCodec.GetScaledDimensions` 请求一个屏幕尺寸的解码，然后如果最近的编解码器档位看起来会发虚，就把缩放提高一档或更多。像素被解码到一个 8 位 BGRA、预乘、使用 sRGB `SKImageInfo` 的缓冲区中；Skia 在该解码过程中会把内嵌的 ICC 配置文件转换为 sRGB。结果是带有源尺寸、解码缩放和 alpha 能力的 `DecodedBitmap`。AVIF 绕过 `SKCodec` 走 `LibAvifDecoder`：解第一帧为预乘 BGRA，应用 `irot`/`imir` 或 EXIF 方向，然后复用 PNG 忽略缩放时的同一套预览降采样。
- SVG 文件使用单独的分支，把 `SKSvg` 图片渲染成相同形状的 `DecodedBitmap`。
- TIFF 通过 LibTiff.NET 读取第一页，把 RGBA 光栅转换为 BGRA，应用 TIFF 方向标签，并把原始方向传给瓦片写入器。
- `ImageDecoder.DecodeAnimation` 逐帧解码 GIF，按帧处置和混合规则合成到同一画布，并返回带每帧延迟的 `DecodedAnimation`。播放由查看器负责；解码器把总帧内存限制在 2400 万像素。
- `DecodedBitmap` 携带像素缓冲区、stride、方向应用后的源宽/高、解码缩放和 `HasAlpha`。当缩放至少为 0.999 时，`IsFullResolution` 为 true。

- `ImageDecoder.DecodeFullRaw` 是瓦片缓存的解码路径。它解码到非托管内存（`NativeMemory.Alloc`），而不是托管的 `byte[]`，因此 100 MP 的缓冲区可以在瓦片文件写入后立即释放。它返回带有原始尺寸、方向应用后的源尺寸、EXIF 方向和 alpha 能力的 `DecodedImageBuffer`；它不执行方向拷贝。
- `LibAvifDecoder`（Windows x64/arm64、macOS、Linux）是 AVIF 桥接层。它通过 `LibraryImport` 调用 `airglance_avif.dll`、`libairglance_avif.dylib` 或 `libairglance_avif.so`，用 libavif 解析元数据，把第一帧解成调用方内存中的预乘 BGRA，应用 `clap` 裁剪，把 `irot`/`imir` 映射到现有的 `ImageOrientation`（缺失时回退 EXIF 方向），并限制解码像素量。原生 shim 静态链接 libavif 和 dav1d；Windows 使用静态 CRT。库由 `Directory.Build.targets` 复制到可执行文件旁，不提交到 git。Windows arm64 DLL 使用 LLVM 针对 ARM64 UCRT 交叉构建并做 PE 校验；没有记录 Windows arm64 硬件运行结果。
- `TiledImageSource`（App）将 512 px 的瓦片写入一个以临时文件为后端的缓存。每个瓦片是 512 × 512 的 BGRA，补齐到固定大小；最后一行/列是部分瓦片。方向和显示编辑在拷贝进瓦片时应用，因此不存在完整的已定向或已编辑中间缓冲区。瓦片文件在释放时删除；超过一天的文件会在启动时清理。只缓存 32 个瓦片位图，查看器绘制与视口相交的瓦片。
- 对于忽略所请求预览缩放的编解码器（尤其是 PNG），`ImageDecoder.Decode` 会在软件中对解码后的缓冲区降采样，以免适配预览保留整张图像。降采样采用最近邻，且仅在编解码器返回其完整尺寸时使用。
- `ImageTransform` 包含 EXIF 方向像素映射和显示编辑。`EditTransform` 存储一个规范状态：先镜像，再进行若干次顺时针四分之一圈旋转。任意顺序的旋转和翻转命令都会归约为八种状态。旋转或翻转会分配新的像素缓冲区，并在轴交换时更新源尺寸。
- `ImageExporter` 编码 JPEG、PNG 或 WebP；JPEG 和 WebP 使用质量 90，PNG 为无损。JPEG 会先绘制到白色位图上，因此透明部分会变成白色。PNG 和 WebP 保留 alpha。保存总是创建新文件。默认名是 `<name>-copy.<ext>`；如果该文件已存在，接下来的候选名是 `-copy-2`、`-copy-3`，依此类推。`WriteNewFile` 使用 `FileMode.CreateNew`，如果另一个进程先创建了同名文件，则用另一个后缀重试。
- `ExifReader` 读取 TIFF/EXIF 载荷，并返回信息面板所显示的字段。它处理 IFD0 和 Exif 子 IFD、RATIONAL 类型 5、带符号 RATIONAL 类型 10，以及以 SHORT 或 SSHORT 存储的 Pillow 风格配对。格式错误的元数据返回空详情；它绝不会阻止图像打开。
- `ImageHeaderReader` 在不解码像素的情况下读取元数据块：JPEG APP1 EXIF 和 APP2 ICC 块，PNG `eXIf` 和 `iCCP`，WebP `EXIF` 和 `ICCP`，以及 PNG IHDR 位深度。它将 EXIF 上限设为 1 MiB，ICC 上限设为 4 MiB。
- `IccProfile` 从 `desc`、`text` 或 `mluc` 标签读取描述。它将 `A98C`/`A98` 映射为 "Adobe RGB (1998)"，将 `sP3`/`P3` 映射为 "Display P3"。
- `ImageMetadata` 汇合文件信息、方向应用后的显示尺寸、格式名称、位深度、ICC 描述和 EXIF 字段。`ColorSpaceDisplay` 报告 `sRGB` 或 `<profile> → sRGB`。

`src/AirGlance.Core/Storage`

- `AppPaths` 每个进程解析一次设置和缓存文件夹。`AIRGLANCE_SETTINGS_DIR` 和 `AIRGLANCE_CACHE_DIR` 覆盖平台默认值；测试套件在大多数情况下使用构造函数注入，而不是环境变量。
- `AppSettings` 保存语言、主题、排序顺序、幻灯片间隔、记住缩放、缩放模式/因子、棋盘格、窗口大小和最大化状态。`Normalize` 修复无效值。
- `SettingsService` 加载 `settings.json`，回退到 `settings.json.bak`，再回退到默认值。保存会写入一个临时文件并将其移动到主文件之上，同时把之前的文件保留为 `.bak`。如果移动被拒绝（MSIX 重定向的应用数据可能出现这种情况），则就地写入。读写失败会被吞掉；偏好设置绝不会阻止查看器打开。
- `ThumbnailCache` 存储 160 px 的 PNG 条目。条目名称是完整图像路径、最后写入 ticks、文件长度和缩略图尺寸的 SHA-256，因此文件一旦更改就会得到新条目。它提供读取、写入、按年龄清理和清空。启动时会在后台任务中移除超过 30 天的条目，并且不等待该任务。

### App

`Program` 构建 Avalonia 应用，包含平台检测和 Inter 字体。在 Windows 上，它默认选择 Avalonia 的软件合成器；`AIRGLANCE_GPU_RENDER=1` 会切回 GPU 路径。macOS 和 Linux 保留平台默认值。

`App.OnFrameworkInitializationCompleted` 解析命令行，创建进程范围的 `AppServices`，在没有窗口的情况下处理 `--bench=decode` 和 `--bench=scan`，否则创建一个 `MainWindow`。在构造窗口之前，它会针对目标路径在线程池上启动 `StartupImageLoader.TryLoad`。加载器在窗口构建期间读取元数据并解码一个 1600 × 1000 的预览。如果路径仍然匹配，`MainWindowViewModel` 会消费一次该结果，因此第一帧通常不必等待解码。预加载失败会被忽略，由正常加载路径报告错误。

`MainWindow` 有意保持轻薄。它处理键盘映射、拖放、文件和文件夹选择器、上下文菜单、全屏、剪贴板写入、设置和帮助对话框，以及窗口/缩放持久化。它创建一个 `MainWindowViewModel`，后者拥有文件夹和当前图像。

窗口是一个图像优先的覆盖层：`ImageViewer` 填满画布，而顶部的文件夹/文件 chip、底部 dock、侧边导航、胶片条和信息抽屉都是独立的覆盖控件。dock 和侧边按钮在空闲 2.5 秒后淡出，并在指针移动时重新出现。胶片条和信息面板是互斥的抽屉，因此打开其中一个不会缩小或覆盖另一个。窗口订阅 `MainWindowViewModel.PropertyChanged`，以命令式方式应用可见性、文本和全屏状态；静态标签从 `LocalizationService.LanguageChanged` 刷新。

早先的 XAML 界面外壳占用了约 370 ms 的首窗口时间；用代码构建的覆盖树在 0.7.0 约为 46 ms（0.3.0 约为 14 ms）。胶片条和信息面板仍为 XAML，并在第一帧之后加载。上下文菜单在首次右键单击时构建，而不是在窗口构造期间构建。

鼠标输入按修饰键分工：滚轮在指针处缩放（不再累积翻页），`Shift + wheel` 平移。左/右方向键始终用于浏览；重新设计之后，当图像宽于窗口时，它们不再变成平移命令。

胶片条和信息面板是独立的视图（`FilmstripView`、`InfoPanelView`），在第一张图像绘制完成后的一帧加载。因此窗口会在这些面板存在之前就出现并显示像素；这符合“先窗口、后面板”的启动规则，并使它们的 XAML 不进入第一帧路径。上下文菜单同样在首次右键单击时构建，而不是在窗口构造期间构建。

分析抽屉（`AnalysisPanelView`）沿用同样的延迟规则：首次打开分析页签时才创建，且只在单图视图使用。`ImageAnalyzer` 在后台任务里分析已经解码好的预览像素，返回的结果不保留像素缓冲；`AnalysisViewModel` 按文件缓存最近几条结果并格式化成抽屉内容。抽屉只读，进入双画布或编辑模式时关闭，全屏时隐藏但保留状态。

`MainWindowViewModel` 持有：

- `ImageNavigator` 和可观察的 `Images` 胶片条；
- 当前路径、位图、元数据和 `EditTransform`；
- 加载版本，以及加载/预取/全分辨率取消令牌；
- 信息面板分区、瞬时消息、幻灯片和空/错误覆盖层。

双画布是 `MainWindow` 拥有的独立视图（`DualCanvasView`）。它创建两个 `PaneSlot` 会话，每个会话有自己的文件夹列表、文件索引、元数据、预览位图/动画和 `ImageViewer`。两侧都通过同一个 `ImageDecoder` 解码，并使用 2400 × 1600 的有界预览，因此进入双画布不需要第二份全分辨率源。视图拥有当前画布、分隔条、dock、固定 50/50 的左右分割胶片条、信息抽屉、画布右键菜单，以及每块画布顶部中央的上一张/下一张胶囊。翻页胶囊只作用于自己的画布；空画布或只有一张图时隐藏。当前画布使用 2 px 身份色边框和 30 × 30 徽标光环；非当前画布图片不变暗。`MainWindow` 隐藏单图界面外壳，把窗口控制按钮保留为根覆盖层。按 `D` 进入或退出，退出时把当前画布的路径交回 `MainWindowViewModel`。

`ImageViewer` 是一个自定义 `Control`，拥有缩放和平移。`MainWindow` 将呈现的位图和源尺寸转发给它，并把它的缩放和视口事件转发回视图模型。当附加了 `TiledImageSource` 时，查看器绘制相交的瓦片而不是预览位图；否则它绘制按图像矩形缩放的预览位图。

### App 服务

- `AppServices` 每个进程创建一次。它加载设置，应用已保存的语言和主题，打开缩略图缓存，并在线程池上启动 30 天清理。它在设置更改时保存设置，并在退出时再次保存。它还对外提供程序集版本，以及用于项目链接的 shell 辅助方法。它没有网络客户端。
- `LocalizationService` 维护一个约 150 组英/中键值对的目录（0.3.0 中为 153 组）。XAML 文本使用 `{DynamicResource L.*}`；代码中构建的文本使用 `T` 和 `Format`。应用语言会构建一个合并的 `ResourceDictionary`，移除之前的语言字典并添加新的字典。`LanguageChanged` 会刷新视图模型和窗口，因此切换英文/中文不需要重启应用。默认值遵循操作系统 UI 区域性：`zh*` 区域性选择简体中文，其他情况均为英文。
- `ThemeService` 规范化 `System`、`Light` 或 `Dark`，并设置 `Application.RequestedThemeVariant`。系统模式遵循 `PlatformSettings.ColorValuesChanged`；显式选择浅色或深色时忽略系统变化。没有高对比度配色：系统对比度偏好不再被读取，因为该配色从未真正加载（见优化清单），所以将主题移除，而不是留着一个静默失效的功能。
- `BenchmarkRecorder` 从操作系统的进程启动时间开始测量。启动模式记录首个窗口、第一帧、解码时间、图像百万像素和峰值工作集，然后在第一帧后退出。解码模式在完成一次 1600 × 1000 解码后退出。扫描模式计时文件夹枚举和前八个 160 px 缩略图，并报告 `scan_ms`、`thumbnail_ms`、`total_ms`、`files` 和 `peak_ws_mb`。所有模式都向 stdout 写入 `key=value` 行，失败时以 `error=<message>` 退出并返回代码 1。
- `StartupTrace` 只读取一次 `AIRGLANCE_STARTUP_TRACE`。当它为 `1` 时，会向 stderr 写入相对于进程启动时间的标记。正常运行时只付出读取环境变量的代价。
- `CommandLineOptions` 在窗口存在之前解析参数。它记录帮助、版本、基准模式、第一个可显示的目标以及被忽略的路径/选项。详情见 [命令行](#command-line)。

## 打开与加载流程

```text
CLI target (optional)
  -> StartupImageLoader.TryLoad (thread pool, before the window)
       -> metadata + 1600 × 1000 preview

CLI path, drop, or file picker
  -> ImageSourceResolver.Resolve (thread pool)
  -> ApplyFolder: navigator, filmstrip, thumbnail queue
  -> LoadCurrentAsync
       -> consume startup preload if it matches, otherwise ReadMetadata (thread pool, cached)
       -> preview target from the physical viewport
       -> ImageLoadService cache or decode (thread pool)
       -> version/path check
       -> BitmapFactory.FromDecoded (UI thread)
       -> ImageViewer.SetImage (UI thread)
       -> prefetch previous/next neighbours (thread pool)
```

`ImageLoadService` 是一个四条目 LRU，以路径和请求尺寸为键。预取的相邻图像是屏幕尺寸解码，而不是全分辨率解码。导航会取消之前的预取，因此快速连续按方向键不会持续解码已被放弃的相邻图像。

当可见缩放超过当前预览缩放的 15% 时，会请求全分辨率解码：

```text
percent / 100 > preview.Scale * 1.15
```

在 100% 缩放下，除非预览的缩放已经达到 99% 或更高（`preview.Scale >= 0.99`），否则该条件为真。全解码在 UI 线程之外运行，替换预览而不重置缩放，并在有显示编辑处于激活状态时重新应用该编辑。如果失败，预览仍然可用。该阈值是相对于预览而言的，并不是“只在 100% 时”的绝对规则：一张预览很小的超大图像可能在低于 100% 时就请求全解码。

信息面板由 `ImageDecoder.ReadMetadata` 构建，它读取头部以及 EXIF/ICC 块，但不解码完整图像。

## 查看器控件

`ImageViewer` 拥有相机，而不是视图模型：

- **适应窗口** 使用 `min(1, viewport / source)`，因此绝不会把图像放大到超过 100%。**适应宽度** 使用 `min(1, viewport width / source width)`。**实际大小** 正好是 100%。**手动** 被钳制在 1–3200%。
- 滚轮每步使用 1.25 的因子，以指针为锚点。Shift+滚轮每步水平平移 80 px。捏合手势在手势原点缩放。
- 双击在“实际大小”和“适应窗口”之间切换，并保持被点击的图像点位于指针之下。`+` 和 `-` 以 1.25 在中心缩放。`0`、`1` 和 `2` 分别选择适应窗口、实际大小和适应宽度。
- 平移会被钳制，使图像至少有 48 px 保持在视野内。适应窗口不能平移；适应宽度只能垂直平移。
- 棋盘格是由 8 × 8 DIP 方块构成的 16 × 16 DIP 瓦片，只在图像矩形背后绘制。它被裁剪到视口，并遵循棋盘格设置。
- 切换图像时，新位图可能会在 80 ms 内淡入。第一张图像、全分辨率替换以及旋转/翻转预览不使用淡入。
- 位图插值在低于 100% 时为高质量，在 100% 及以上时为最近邻。
- 坐标以 DIP 为单位。视口事件通过乘以 `RenderScaling` 报告物理像素；解码器将这些像素用作预览目标。

## 线程规则

| 工作 | 线程 |
| --- | --- |
| 目录枚举与排序 | 线程池 |
| 预览与全分辨率解码 | 线程池 |
| EXIF、ICC 和头部读取 | 线程池 |
| 缩略图解码、编码和磁盘缓存 | 一个串行后台队列 |
| 保存副本的编码与文件写入 | 线程池 |
| 剪贴板源文件读取 | 线程池 |
| `WriteableBitmap` 创建和像素拷贝 | UI 线程 |
| 可观察属性更新和查看器调用 | UI 线程 |

解码、目录枚举、元数据读取、缩略图生成和导出都在线程池上运行。只有最终的 `WriteableBitmap` 拷贝和可观察属性更新发生在 UI 线程上。

每次导航都会递增 `_loadVersion` 并取消之前的加载令牌。异步续体在呈现像素之前会同时检查捕获的版本和当前路径。过期的解码会被丢弃，绝不显示。预取和全分辨率令牌也会在导航时取消。

`BitmapFactory.FromDecoded` 创建一个使用 `Bgra8888`/`Premul` 的 `WriteableBitmap`，并按行执行一次批量拷贝。解码缓冲区和 Avalonia 位图使用相同的布局，因此不需要 PNG 往返。

缩略图由 `ThumbnailService` 在一个串行后台队列上按文件夹顺序生成。会先查询磁盘缓存；失败不会重试。每个完成的 PNG 都会在 UI 线程上转换为 Avalonia `Bitmap`。

## 内存行为

预览按窗口而不是按文件确定大小。目标是物理视口的宽度和高度，下限为 640 × 480，上限为 8192 × 8192，然后由 `SKCodec.GetScaledDimensions` 进一步缩减。因此，一张 100 MP 的照片在 1600 × 1000 的窗口中大约会解码出 1.6 MP 的预览，约 6.4 MB 的 BGRA 像素。

预览缓存最多保存四个条目。这是条目数量，而不是字节预算：四个 8K 尺寸的预览会比四个窗口尺寸的预览大得多。文件夹更改时会清空缓存。

全分辨率解码会分配整张图像。在 1 亿像素时：

- 一个 BGRA 缓冲区是 100,000,000 × 4 = 400 MB（十进制）；
- Avalonia `WriteableBitmap` 又是 400 MB；
- 旋转或翻转会在旧缓冲区得以释放之前再分配 400 MB；
- JPEG 导出会额外增加一个白底平坦化位图和编码输出。

因此，100 MP 的情况在替换预览时可能达到约 800 MB，在编辑或导出期间瞬时超过 1.2 GB。这些是根据像素格式得出的算术值，而不是实测的峰值工作集。全分辨率位图替换预览，前一个 Avalonia 位图会被释放。解码后的预览对象可能留在 LRU 中，直到被逐出。

0.1 中没有分块、流式或 mip 金字塔。100 MP 图像在 100% 下需要非常大的全分辨率解码以及足够的空闲内存；这是简单查看器的有意取舍。解码器会拒绝像素数超过 `int.MaxValue / 4`（约 536 百万像素）的图像，并将其报告为过大。

## 设置与本地数据

| 数据 | Windows | macOS | Linux |
| --- | --- | --- | --- |
| 设置 | `%APPDATA%\AirGlance\settings.json` | `~/Library/Application Support/AirGlance/settings.json` | `${XDG_CONFIG_HOME:-~/.config}/airglance/settings.json` |
| 缓存 | `%LOCALAPPDATA%\AirGlance\cache` | `~/Library/Caches/AirGlance` | `${XDG_CACHE_HOME:-~/.cache}/airglance` |
| 缩略图 | `%LOCALAPPDATA%\AirGlance\cache\thumbs` | `~/Library/Caches/AirGlance/thumbs` | `${XDG_CACHE_HOME:-~/.cache}/airglance/thumbs` |

`AIRGLANCE_SETTINGS_DIR` 和 `AIRGLANCE_CACHE_DIR` 会替换平台根目录，用于测试和便携运行。它们是完整路径，而不是文件夹名称。

设置 JSON 是缩进的、camelCase、UTF-8 无 BOM。主文件缺失或损坏时会回退到 `.bak` 文件，然后回退到规范化默认值。覆盖现有文件保存时，会把之前的文件保留为 `settings.json.bak`，写入 `settings.json.tmp`，并将其移动到主文件之上。移动期间的 `IOException` 会触发就地覆盖，这使 MSIX 重定向的应用数据保持为单个文件。

缩略图缓存在 30 天后使条目过期。清理在 `AppServices` 构造期间于后台启动。设置对话框可以报告缓存大小并清空该文件夹。

<a id="command-line"></a>

## 命令行

```text
AirGlance [options] [file or folder]
```

| 选项 | 行为 |
| --- | --- |
| `-h`, `--help`, `-?` | 在消息窗口中显示帮助文本 |
| `-v`, `--version` | 在消息窗口中显示版本 |
| `--bench=startup` | 打开目标，测量第一帧，然后退出 |
| `--bench=decode` | 以 1600 × 1000 解码目标，打印数字，然后退出 |
| `--bench=scan` | 枚举目标文件夹（或包含目标文件的文件夹）并解码前八个缩略图，然后退出 |
| `--` | 将之后的每个参数都视为路径 |

第一个可显示的文件或文件夹优先。之后的路径会被忽略。目录始终算作目标，即使其中不含图像。文件通常只有在存在且具有受支持扩展名时才计入。如果不存在可显示的路径，但指定了一个不受支持的文件，则该文件会成为目标，以便窗口解释为何无法打开它。未知选项会被忽略。

帮助和版本以对话框形式呈现，因为 Windows 构建是 GUI 可执行程序；它们不是控制台输出。

`--bench=startup` 需要一个目标，并从操作系统的进程启动时间开始测量。它会打印：

```text
first_window_ms=<ms>
first_frame_ms=<ms>
decode_ms=<ms>
image_mp=<megapixels>
peak_ws_mb=<megabytes>
```

`--bench=decode` 跳过窗口，以 1600 × 1000 解码文件夹的第一张图像（或指定的文件），并打印相同的键，其中窗口和第一帧的值为零。

`--bench=scan` 也跳过窗口。它枚举包含目标的文件夹，并以 160 × 160 解码前八张图像，然后打印：

```text
scan_ms=<ms>
thumbnail_ms=<ms>
total_ms=<ms>
files=<count>
peak_ws_mb=<megabytes>
```

基准测试失败会打印 `error=<message>` 并以代码 1 退出。`tools/bench.ps1` 会运行多次 `--bench=startup` 进程，报告中位数和 p95，并可将原始运行结果写为 JSON。该应用依赖 stdout 重定向，因为 Windows 构建没有控制台。

`AIRGLANCE_STARTUP_TRACE=1` 会向 stderr 打印需主动启用的启动标记，包括 `main`、`app-init`、`xaml-loaded`、`framework-init`、`services-ready`、`window-created`、`window-opened` 和 `image-initialized`。

`SingleInstance` 会占用一个命名互斥体并在命名管道上监听，两者都以设置目录的哈希作为键。拿不到互斥体的那次启动会把自己的路径写进管道然后退出；正在运行的实例在 UI 线程上打开它。作用域是设置目录，所以隔离的测试、基准测试或便携副本不会把文件转发进正常会话。没有托盘图标。

## NativeAOT 与裁剪

发布构建通过 NativeAOT 在 Windows、macOS 和 Linux 上发布。NativeAOT 不在操作系统家族之间交叉编译；Windows、macOS 和 Linux 各自在自己的 runner 上构建。原生 SkiaSharp、HarfBuzzSharp 和 Avalonia 库按运行时标识符拷贝。

AOT 约束由设计保证：

- 设置 JSON 使用 `SettingsJsonContext`，这是一个源生成的 `JsonSerializerContext`。没有反射序列化器。
- 所有数据绑定的 XAML（面板和对话框）都使用编译绑定和 `x:DataType`。
- XAML 会被编译。主窗口用 C# 构建；面板和对话框由编译后的 XAML 在代码中构造。没有任何窗口由运行时 XAML 加载器实例化。
- 没有 `Assembly.Load`，没有 `Activator.CreateInstance`，没有 `dynamic`，也没有运行时代码生成。
- `AppServices.Version` 读取 `AssemblyInformationalVersionAttribute`；这属于元数据反射，不是动态代码。

`AirGlance.App.csproj` 抑制分析器警告 `AVLN3001`。该警告期望通过运行时 XAML 加载器实例化的窗口具有公共无参构造函数。AirGlance 直接构造每个窗口并传入服务或视图模型，因此这种构造函数形态是有意为之。如果将来某个窗口通过运行时 XAML 加载器加载，应移除该抑制并改为添加无参构造函数。

AOT 发布配置还设置 `IlcOptimizationPreference=Speed`，禁用堆栈跟踪生成、EventSource 和元数据更新，并从发布目录中移除 PDB。

## 测试

- `tests/AirGlance.Core.Tests` 使用 xUnit 覆盖排序、文件夹扫描、导航、方向与编辑、解码、元数据、导出、设置和缩略图缓存。
- `tests/AirGlance.App.Tests` 使用 Avalonia.Headless 和 xUnit 覆盖窗口级键盘、导航、编辑、全屏、语言切换、信息行以及空/错误状态。
- 测试图像和 ICC 配置文件会从 `tests/Assets` 拷贝到测试输出。测试夹具以及每个夹具所验证的内容列在[格式支持矩阵](formats.zh-CN.md)中。

运行测试套件：

```text
dotnet test AirGlance.sln -c Release
```

`.github/workflows/build.yml` 在 Windows、Linux 和 macOS 上还原、构建并测试解决方案。`release.yml` 在版本标签上打包六个便携归档和校验和文件。`store.yml` 构建未签名的 MSIX 包并运行信息性 WACK 预检。发布流程及其当前限制见[交付](shipping.md)，启动、解码和内存测量见[性能说明](performance.md)。

## 相关文档

- [格式支持矩阵](formats.zh-CN.md)
- [交付 AirGlance 0.7.0](shipping.zh-CN.md)
- [第三方声明](../THIRD-PARTY-NOTICES.md)
- [变更日志](../CHANGELOG.md)
- [README](../README.zh-CN.md)
