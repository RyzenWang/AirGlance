# AirGlance Microsoft Store 更新包说明：0.7.1

## 包信息

| 项目 | 值 |
| --- | --- |
| 应用版本 | `0.7.1` |
| Store 包版本 | `1.0.3.0`（上次 Store 包为 `1.0.2.0`，应用 0.7.0） |
| 架构 | `x64` |
| 文件 | `artifacts/msix-store/AirGlance_1.0.3.0_x64.msix` |
| 大小 | 21.58 MiB（22,632,024 字节） |
| SHA-256 | `4D9BC55C5932196480627A06D8A95FCE5FD401729D3A755E34D3335793881EA7` |
| 签名 | **未签名**；Microsoft Store 认证通过后由 Store 重新签名 |

包内确认包含（12 个文件）：

- `AirGlance.exe` — 0.7.1 NativeAOT，`ProductVersion` 为 `0.7.1+1212575`
- `airglance_avif.dll` — libavif + dav1d 的 Windows x64 解码器
- `libSkiaSharp.dll`、`libHarfBuzzSharp.dll`、`av_libglesv2.dll`
- `Assets/` 四个 logo、`LICENSE`、`THIRD-PARTY-NOTICES.md`
- `AppxManifest.xml` 版本 `1.0.3.0`、架构 `x64`、publisher identity 未变（`CN=D87D6846-5A2B-4D82-BDB7-8E739EB3F35B`），`.avif` 文件关联仍在

CI 另有一份同内容的包（`store.yml`，run 45573927991）：22,637,308 字节，SHA-256
`1043F6207DFBC9EEB4F830B4A002B55334C63F0723966B1598BDCD5F200F5E7C`。两份哈希不同是
因为 MakeAppx 打包不是逐字节确定的（时间戳与条目顺序），解包后的内容一致。CI 那份还带
WACK 报告 `wack-AirGlance_1.0.3.0_x64.xml`（`appcert.exe` 跑完并建议"提交前先看报告"）。

## 距离上次 Store 版本更新了什么

上次：`1.0.2.0` = 应用 0.7.0；本次：`1.0.3.0` = 应用 0.7.1。0.6.0/0.7.0 已有的查看器、
编辑、裁剪、导出副本、大 TIFF 与 AVIF 支持保持不变。

### 1. 再次启动会交给已开着的窗口

双击第二张图不再起第二个进程：拿到命名互斥体失败的那次启动会把路径通过本机命名管道交给
运行中的窗口，然后自己退出。互斥体与管道都以设置目录的哈希为键，所以隔离测试、基准测试和
便携副本不会互相干扰。仓库里也同步修正了隐私表述——原名写"没有 socket"，但 .NET 在 Unix 上
用 Unix domain socket 实现命名管道。

### 2. 动画 WebP 会播放

`DecodeAnimation` 里那条 `.gif` 扩展名判断是历史遗留，`SKCodec` 本来就能给出 WebP 的帧、
消隐/混合规则和延迟。现在由帧数决定，GIF 与动画 WebP 共用同一套合成器和帧预算；APNG 仍是单帧。

### 3. 带 eXIf 方向的 PNG 不再横躺

Skia 对 PNG 不报告 encoded origin，而解码路径只读那个值，所以方向信息被丢掉了。现在解码在
codec 没有给出 origin 时回退到托管解析器读到的 EXIF 方向。

### 4. HEIC / RAW 不再静默消失

打开一个全是 HEIC 的文件夹以前看起来像空文件夹。现在支持列表保持不变，但打开文件夹会说明
"已跳过 N 个文件：HEIC 和相机 RAW 需要额外的解码支持"，拖进去的文件会指出缺少哪种解码器，
而不是笼统的"格式不支持"。

### 5. 坏图不再吞掉上下页按钮

翻到一张损坏的图片时，错误卡片会出现，而导航箭头原本同时被"当前有图"和"没有浮层"两个条件
挡住，鼠标就没法离开那张图（方向键反而还能用）。现在导航跟着文件夹走：只要文件夹里有多于
一张图，箭头就显示、并且可以点。

### 6. BigTIFF 与 BMP/ICO 进入有测试的范围

LibTiff 本来就能读 BigTIFF，README 却把它写成"不在支持范围"。现在有 `bigtiff-lzw.tif` 夹具，
BMP、ICO 和 WebP 的 ICC/EXIF 也各自有了夹具（`tools/make-extra-fixtures.py` 生成）。

### 7. 内存与解码：十项性能改动

- TIFF 预览改为按 tile/strip 采样进目标尺寸，100 MP 的 TIFF 不再为一张小预览建全图光栅
- 全光栅预算降到 32 MP 并报 `TooLarge`；预览超过 128 MP 直接拒绝；codec 抛出的 OOM 被翻译成 `TooLarge`
- `Downscale` 改成箱式滤波（预计算列跨度），大比例缩小不再有摩尔纹
- 分析器的唯一色统计从"每像素一次 HashSet 插入"换成 2 MB 位图
- SVG 只解析一次、直接画进托管缓冲；`SvgGeometry` 改用 `XmlReader` 并限制文档大小
- AVIF 缓存最后一次打开的 payload 与解析结果
- 瓦片读盘移出 `Render`，未命中时先画下面的预览
- 全分辨率位图的构建移出 UI 线程
- 双画布按各自尺寸解码，不再固定 2400 × 1600

### 8. 其它

- 移除从未生效的高对比度主题
- 窗口标题里两个乱码字符修正，空路径状态不再出现 `AirGlance — AirGlance`
- AVIF `clap` 断言崩溃与 `clap` 解码失败两处修复
- 信息面板不再对 TIFF 声称做过色彩转换

## 提交清单

- [ ] Partner Center → 提交更新 → 上传 `AirGlance_1.0.3.0_x64.msix`
- [ ] 确认包版本显示 `1.0.3.0`、架构 `x64`、包标识 `RyzenWang.AirGlance`
- [ ] 商店列表沿用 0.7.0 的截图与描述；本次没有界面改版
- [ ] 提交认证

## 验证记录

- 把 MSIX 完整解包到临时目录后，用包内 `AirGlance.exe` 逐项 `--bench=decode`：
  `sample.avif`（47.7 ms，证明原生解码器随包生效）、`animated.webp`（2.1 ms）、
  `bigtiff-lzw.tif`（3.0 ms）、`sample.ico`（1.7 ms），退出码均为 0
- 只把 `AirGlance.exe` 单独解出来运行**不是**受支持的布局：原生 payload（
  `airglance_avif.dll`、`libSkiaSharp.dll` 等）是独立文件，缺了它们应用会直接失败
- 包内不含 `AppxSignature.p7x`，符合"未签名、由 Store 重签"的流程
- CI 的 `store.yml` 跑完 WACK 并产出报告；本地未安装 Windows App Certification Kit，所以没有本地 WACK 结论
