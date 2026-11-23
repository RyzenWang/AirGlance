using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace AirGlance.App.Services;

/// <summary>
/// Runtime English / Chinese strings. XAML text uses DynamicResource keys such as
/// <c>L.OpenFile</c>; text built in code goes through <see cref="T"/> and
/// <see cref="Format"/>. Switching language replaces one merged dictionary, so no
/// window has to restart.
/// </summary>
public sealed class LocalizationService : IDisposable
{
    public const string English = "en";
    public const string ChineseSimplified = "zh-CN";

    private static readonly IReadOnlyDictionary<string, (string En, string Zh)> Catalog =
        new Dictionary<string, (string En, string Zh)>(StringComparer.Ordinal)
        {
            ["L.AppName"] = ("AirGlance", "AirGlance"),
            ["L.WindowTitleFormat"] = ("{0} — AirGlance", "{0} — AirGlance"),
            ["L.Minimize"] = ("Minimize", "最小化"),
            ["L.Maximize"] = ("Maximize", "最大化"),
            ["L.Restore"] = ("Restore", "还原"),
            ["L.Close"] = ("Close", "关闭"),
            ["L.Menu"] = ("Menu", "菜单"),

            ["L.EmptyTitle"] = ("Drop images or a folder here.", "把图片或文件夹拖进来。"),
            ["L.EmptyLimitations"] = ("HEIC, RAW and video still need additional decoders.", "HEIC、RAW 和视频仍需要额外的解码支持。"),
            ["L.EmptyFolderTitle"] = ("No images in this folder.", "这个文件夹里没有可显示的图片。"),
            ["L.EmptyFolderHint"] = ("Try another folder, or press Ctrl+Shift+O.", "换一个文件夹，或者按 Ctrl+Shift+O。"),
            ["L.DropHint"] = ("Release to open", "松开即可打开"),
            ["L.Loading"] = ("Loading…", "正在打开…"),

            ["L.CantOpenTitle"] = ("Can't open this file", "打不开这张图"),
            ["L.ErrorUnsupportedFormat"] = ("Unsupported format ({0}).", "格式不支持（{0}）。"),
            ["L.ErrorTooLarge"] = ("This image is too large to decode.", "图片尺寸过大，当前解码器无法生成预览。"),
            ["L.TooLargeHint"] = ("Try the preview image next to the original file.", "可以尝试打开原文件旁边的预览图。"),
            ["L.ErrorNotFound"] = ("The file no longer exists.", "文件已经不存在了。"),
            ["L.ErrorAccessDenied"] = ("Access denied. Check the file's permissions.", "没有权限读取这个文件。"),
            ["L.ErrorCorrupt"] = ("The file looks damaged or incomplete.", "文件似乎损坏或不完整。"),
            ["L.ErrorUnknown"] = ("The image could not be opened.", "无法打开这张图片。"),
            ["L.ErrorFolder"] = ("The folder could not be read.", "无法读取这个文件夹。"),
            ["L.ErrorNoImages"] = ("No images in this folder.", "这个文件夹里没有可显示的图片。"),
            ["L.DragUnsupported"] = ("That file type is not supported.", "不支持这种文件类型。"),

            ["L.InfoPanel"] = ("Info panel", "信息面板"),
["L.InfoPanelShort"] = ("Info", "信息"),
            ["L.Filmstrip"] = ("Filmstrip", "胶片条"),
["L.FilmstripSummary"] = ("{0} images · click to switch", "{0} 张 · 点击切换"),
            ["L.SwapPanes"] = ("Swap panes", "交换画布"),
            ["L.DualView"] = ("Dual view (D)", "双画布（D）"),
            ["L.NoFolder"] = ("No folder open", "还没有打开文件夹"),
            ["L.ImageCount"] = ("{0} images", "{0} 张"),
            ["L.ContextMenuNote"] = ("Right-click selects this canvas first; every item applies to this side only.", "右键会先选中该画布，菜单中的操作只作用于这一侧。"),
            ["L.CloseCompare"] = ("Exit compare", "退出对比"),
            ["L.LoadingFailed"] = ("Could not load this image", "无法加载这张图片"),
            ["L.Preview"] = ("Preview", "预览"),
            ["L.Region"] = ("Region", "区域"),
            ["L.LoadingRegion"] = ("Loading original region…", "正在加载原始区域…"),
            ["L.CachingRegion"] = ("Caching original region…", "正在缓存原始区域…"),
            ["L.FilmstripTarget"] = ("Targets canvas {0}", "作用于 {0} 画布"),
            ["L.DualEmptyTitle"] = ("Open this canvas", "打开这个画布"),
            ["L.DualEmptyDetail"] = ("Drop a folder or image here. Left and right panes are independent.", "把文件夹或图片拖到这里。左右画布各自独立。"),
["L.CollapseFilmstrip"] = ("Collapse filmstrip", "收起胶片条"),
            ["L.Settings"] = ("Settings", "设置"),
            ["L.Shortcuts"] = ("Keyboard shortcuts", "快捷键"),
            ["L.SectionFile"] = ("FILE", "文件"),
            ["L.SectionImage"] = ("IMAGE", "图片"),
            ["L.SectionColor"] = ("COLOR", "色彩"),
            ["L.SectionCamera"] = ("CAMERA", "相机"),
            ["L.InfoName"] = ("Name", "文件名"),
            ["L.InfoPath"] = ("Path", "完整路径"),
            ["L.InfoSize"] = ("Size", "文件大小"),
            ["L.InfoModified"] = ("Modified", "修改时间"),
            ["L.InfoDimensions"] = ("Dimensions", "尺寸"),
            ["L.InfoFormat"] = ("Format", "格式"),
            ["L.InfoBitDepth"] = ("Bit depth", "位深"),
            ["L.InfoFrames"] = ("Frames", "帧数"),
            ["L.InfoColorSpace"] = ("Color space", "色彩空间"),
            ["L.InfoIcc"] = ("ICC profile", "ICC 配置"),
            ["L.InfoCamera"] = ("Camera", "相机"),
            ["L.InfoLens"] = ("Lens", "镜头"),
            ["L.InfoExposure"] = ("Exposure", "曝光"),
            ["L.InfoAperture"] = ("Aperture", "光圈"),
            ["L.InfoIso"] = ("ISO", "ISO"),
            ["L.InfoFocal"] = ("Focal length", "焦距"),
            ["L.InfoTaken"] = ("Taken", "拍摄时间"),
            ["L.BitDepthFormat"] = ("{0}-bit", "{0} 位"),
            ["L.MegapixelsFormat"] = ("{0:0.0} MP", "{0:0.0} 百万像素"),
            ["L.SecondsFormat"] = ("{0} s", "{0} 秒"),

            ["L.WrappedFirst"] = ("Back to the first image.", "已回到第一张。"),
            ["L.WrappedLast"] = ("Back to the last image.", "已回到最后一张。"),
            ["L.ZoomLevel"] = ("Zoom level", "缩放比例"),
            ["L.SlideshowRunning"] = ("Slideshow — any key to stop.", "幻灯片 · 按任意键停止。"),
            ["L.SlideShowStarted"] = ("Slideshow started.", "幻灯片已开始。"),

            ["L.OpenFile"] = ("Open image…", "打开图片…"),
            ["L.OpenFolder"] = ("Open folder…", "打开文件夹…"),
            ["L.PreviousImage"] = ("Previous image", "上一张"),
            ["L.NextImage"] = ("Next image", "下一张"),
            ["L.ZoomIn"] = ("Zoom in", "放大"),
            ["L.ZoomOut"] = ("Zoom out", "缩小"),
            ["L.FitWindow"] = ("Fit window", "适应窗口"),
            ["L.FitWidth"] = ("Fit width", "适应宽度"),
            ["L.ActualSize"] = ("100%", "100%"),
            ["L.RotateClockwise"] = ("Rotate clockwise", "顺时针旋转"),
            ["L.RotateCounterClockwise"] = ("Rotate counter-clockwise", "逆时针旋转"),
            ["L.FlipHorizontal"] = ("Flip horizontally", "水平翻转"),
            ["L.FlipVertical"] = ("Flip vertically", "垂直翻转"),
            ["L.Edit"] = ("Edit", "编辑"),
            ["L.EditHint"] = ("Edit (Ctrl+E)", "编辑（Ctrl+E）"),
            ["L.EditTitle"] = ("Edit", "编辑"),
            ["L.EditSub"] = ("Changes stay in this session. The original file is never modified.", "修改只保留在本次会话，原文件不会被写入。"),
            ["L.EditTransform"] = ("TRANSFORM", "变换"),
            ["L.EditCrop"] = ("CROP", "裁剪"),
            ["L.EditExport"] = ("EXPORT", "导出"),
            ["L.EditReset"] = ("Reset", "重置"),
            ["L.EditResetCrop"] = ("Reset crop", "重置裁剪"),
            ["L.EditRotateLeft"] = ("Left", "左旋"),
            ["L.EditRotateRight"] = ("Right", "右旋"),
            ["L.EditFlipH"] = ("Flip H", "水平翻转"),
            ["L.EditFlipV"] = ("Flip V", "垂直翻转"),
            ["L.EditFree"] = ("Free", "自由"),
            ["L.EditOriginalRatio"] = ("Original", "原比例"),
            ["L.EditCropEnter"] = ("Crop", "裁剪"),
            ["L.EditCropSizeLabel"] = ("CROP SIZE", "裁剪尺寸"),
            ["L.EditCropTag"] = ("CROP  ·  Drag a corner  ·  Esc", "裁剪  ·  拖动角点  ·  Esc"),
            ["L.EditSafeTag"] = ("Original stays unchanged", "原图保持不变"),
            ["L.EditDone"] = ("Done", "完成"),
            ["L.EditCropOverlay"] = ("Crop overlay", "裁剪框"),
            ["L.EditOriginalUnchanged"] = ("Original stays unchanged", "原图保持不变"),
            ["L.EditCancel"] = ("Cancel", "取消"),
            ["L.EditExportCopy"] = ("Export copy…", "导出副本…"),
            ["L.EditExportCard"] = ("JPEG, PNG or WebP · resize · keep metadata", "JPEG、PNG 或 WebP · 调整尺寸 · 保留元数据"),
            ["L.EditUnavailable"] = ("Editing is unavailable for this image.", "当前图片无法编辑。"),
            ["L.EditNeedsFullResolution"] = ("This large TIFF has a preview only. Full-resolution decode is required before editing.", "这张大 TIFF 只有预览图，需要完整分辨率后才能编辑。"),
            ["L.EditDualUnavailable"] = ("Exit dual view before editing.", "请先退出双画布再编辑。"),
            ["L.EditNoImage"] = ("Open an image before editing.", "请先打开一张图片。"),
            ["L.EditCropSizeFormat"] = ("{0} × {1} · {2}", "{0} × {1} · {2}"),
            ["L.EditExportTitle"] = ("Export copy", "导出副本"),
            ["L.EditExportReady"] = ("Ready to save", "准备导出"),
            ["L.EditExportSub"] = ("A new file is written. The original photo is untouched.", "会写入一个新文件，原图不会被修改。"),
            ["L.EditFormat"] = ("FORMAT", "格式"),
            ["L.EditQuality"] = ("QUALITY", "质量"),
            ["L.EditResize"] = ("RESIZE", "调整尺寸"),
            ["L.EditOriginalPixels"] = ("Original pixels", "原始像素"),
            ["L.EditLongEdge"] = ("Long edge", "长边"),
            ["L.EditFitWithin"] = ("Fit within", "最大尺寸"),
            ["L.EditFileName"] = ("FILE NAME", "文件名"),
            ["L.EditKeepMetadata"] = ("Keep EXIF and ICC metadata where supported", "尽量保留 EXIF 和 ICC 元数据"),
            ["L.EditExportHintFormat"] = ("{0} · quality {1} · {2}", "{0} · 质量 {1} · {2}"),
            ["L.EditExportHintOriginal"] = ("original pixels", "原始像素"),
            ["L.EditExportHintLongEdge"] = ("long edge {0} px", "长边 {0} px"),
            ["L.EditExportHintFit"] = ("fit within {0} px", "最大 {0} px"),
            ["L.EditExporting"] = ("Exporting…", "正在导出…"),
            ["L.EditExportSucceeded"] = ("Saved to {0}", "已保存到 {0}"),
            ["L.EditExportFailed"] = ("Export failed.", "导出失败。"),
            ["L.EditChooseFolder"] = ("Choose folder…", "选择文件夹…"),
            ["L.EditOverwriteTitle"] = ("File exists", "文件已存在"),
            ["L.EditOverwriteBody"] = ("A file with this name already exists. Choose another name.", "同名文件已存在，请换一个文件名。"),
            ["L.EditFullscreen"] = ("Fullscreen", "全屏"),
            ["L.Fullscreen"] = ("Fullscreen", "全屏"),
["L.FullscreenHint"] = ("Esc exits fullscreen · move the mouse for controls", "Esc 退出全屏 · 移动鼠标显示控制"),
            ["L.ExitFullscreen"] = ("Exit fullscreen", "退出全屏"),
            ["L.Slideshow"] = ("Slideshow", "幻灯片"),
            ["L.StopSlideshow"] = ("Stop slideshow", "停止幻灯片"),
            ["L.CopyImage"] = ("Copy image", "复制图片"),
            ["L.CopyPath"] = ("Copy path", "复制路径"),
            ["L.ShowInFolder"] = ("Show in folder", "在文件夹中显示"),
            ["L.OpenWith"] = ("Open with…", "打开方式…"),
            ["L.DropOpen"] = ("Open image", "打开图片"),

            ["L.SettingsTitle"] = ("Settings", "设置"),
            ["L.Done"] = ("Done", "完成"),
            ["L.SectionAppearance"] = ("APPEARANCE", "外观"),
            ["L.Language"] = ("Language", "语言"),
            ["L.Theme"] = ("Theme", "主题"),
["L.SwitchToDark"] = ("Switch to dark", "切换到深色"),
["L.SwitchToLight"] = ("Switch to light", "切换到浅色"),
            ["L.ThemeSystem"] = ("Follow system", "跟随系统"),
            ["L.ThemeLight"] = ("Light", "浅色"),
            ["L.ThemeDark"] = ("Dark", "深色"),
["L.On"] = ("On", "开"),
["L.Off"] = ("Off", "关"),
            ["L.SectionViewing"] = ("VIEWING", "查看"),
            ["L.Checkerboard"] = ("Checkerboard behind transparency", "透明背景显示棋盘格"),
            ["L.RememberZoom"] = ("Remember zoom", "记住上次缩放"),
            ["L.SlideshowInterval"] = ("Slideshow interval", "幻灯片间隔"),
            ["L.SortOrder"] = ("Sort order", "排序"),
            ["L.SortName"] = ("Name, A to Z", "名称，正序"),
            ["L.SortNameDescending"] = ("Name, Z to A", "名称，倒序"),
            ["L.SectionCache"] = ("CACHE", "缓存"),
            ["L.CacheDescription"] = ("Filmstrip thumbnails only. Removed after 30 days.", "只保存胶片条缩略图，30 天后自动清理。"),
            ["L.CacheSizeFormat"] = ("{0} MB", "{0} MB"),
            ["L.ClearCache"] = ("Clear cache", "清空缓存"),
            ["L.CacheCleared"] = ("Cache cleared.", "缓存已清空。"),
            ["L.SectionAbout"] = ("ABOUT", "关于"),
            ["L.AboutVersion"] = ("Version {0}", "版本 {0}"),
            ["L.AboutLicense"] = ("MIT licensed.", "MIT 许可。"),
            ["L.AboutNoNetwork"] = ("No network. No telemetry.", "不联网，不收集数据。"),
            ["L.AboutThirdParty"] = ("Third-party notices", "第三方组件"),
            ["L.AboutGitHub"] = ("Project page", "项目主页"),

            ["L.SaveCopyTitle"] = ("Save a copy", "另存副本"),
            ["L.SaveAs"] = ("Save as", "保存为"),
            ["L.Format"] = ("Format", "格式"),
            ["L.EstimatedSize"] = ("Estimated size", "预估体积"),
            ["L.Estimating"] = ("Calculating…", "正在计算…"),
            ["L.SaveCopyHint"] = ("A new file is created. The original is never changed.", "会新建一个文件，原图不会被修改。"),
            ["L.Browse"] = ("Choose folder…", "选择文件夹…"),
            ["L.SaveCopyError"] = ("Choose a folder that exists.", "请选择一个存在的文件夹。"),
            ["L.EstimateFailed"] = ("Size unavailable", "无法估算"),
            ["L.EstimateFormat"] = ("about {0}", "约 {0}"),
            ["L.Save"] = ("Save", "保存"),
            ["L.Cancel"] = ("Cancel", "取消"),

            ["L.ShortcutsTitle"] = ("Keyboard shortcuts", "快捷键"),
            ["L.ShortcutGroupOpen"] = ("OPEN", "打开"),
            ["L.ShortcutGroupNavigate"] = ("NAVIGATE", "浏览"),
            ["L.ShortcutGroupZoom"] = ("ZOOM", "缩放"),
            ["L.ShortcutGroupView"] = ("VIEW", "显示"),
            ["L.ShortcutGroupEdit"] = ("EDIT AND SAVE", "编辑与保存"),
            ["L.ShortcutGroupDual"] = ("DUAL VIEW", "双画布"),
            ["L.ShortcutGroupApp"] = ("APPLICATION", "应用"),
            ["L.ShortcutNextPrev"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutArrows"] = ("Previous / next image", "上一张 / 下一张"),
            ["L.ShortcutPage"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutSpace"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutZoom"] = ("Zoom in / out at the pointer", "以光标为中心放大 / 缩小"),
            ["L.ShortcutWheel"] = ("Zoom at the pointer", "以光标为中心缩放"),
            ["L.ShortcutCtrlWheel"] = ("Pan horizontally", "横向平移"),
            ["L.ShortcutDrag"] = ("Drag to pan", "拖动平移"),
            ["L.ShortcutDual"] = ("Toggle dual view", "切换双画布"),
            ["L.ShortcutActivePane"] = ("Switch the active A/B pane", "切换当前 A/B 画布"),
            ["L.ShortcutSwap"] = ("Swap the A/B panes", "交换 A/B 画布"),
            ["L.ShortcutPaneMenu"] = ("Open the active pane menu", "打开当前画布菜单"),
            ["L.ShortcutRightClick"] = ("Open the pane menu", "打开画布菜单"),
            ["L.ShortcutSingleViewNote"] = ("Tab, X and Shift+F10 apply in dual view. Copy path, rotate, flip, save-copy and slideshow apply in single view.", "Tab、X 和 Shift+F10 用于双画布；复制路径、旋转、翻转、另存副本和幻灯片用于单画布。"),
            ["L.ShortcutDoubleClick"] = ("Toggle 100% and fit", "在 100% 与适应窗口之间切换"),
            ["L.ShortcutPresets"] = ("Fit window / 100% / fit width", "适应窗口 / 100% / 适应宽度"),
            ["L.ShortcutEdit"] = ("Edit image", "编辑图片"),
            ["L.ShortcutRotate"] = ("Rotate clockwise / counter-clockwise", "顺时针 / 逆时针旋转"),
            ["L.ShortcutFlip"] = ("Flip horizontal / vertical", "水平 / 垂直翻转"),
            ["L.ShortcutFullscreen"] = ("Fullscreen; Esc exits", "全屏；Esc 退出"),
            ["L.ShortcutEscape"] = ("Close menus/panels; exit fullscreen or dual view", "关闭菜单/面板；退出全屏或双画布"),
            ["L.ShortcutSlideshow"] = ("Start or stop the slideshow (single view)", "开始 / 停止幻灯片（单画布）"),
            ["L.ShortcutPanels"] = ("Info panel / filmstrip", "信息面板 / 胶片条"),
            ["L.ShortcutCopy"] = ("Copy image / copy path", "复制图片 / 复制路径"),
            ["L.ShortcutSaveCopy"] = ("Save a copy", "另存副本"),
            ["L.ShortcutOpen"] = ("Open image / folder", "打开图片 / 文件夹"),
            ["L.ShortcutLanguage"] = ("Switch English / Chinese", "切换中英文"),
            ["L.ShortcutShortcuts"] = ("This list", "这个列表"),
            ["L.ShortcutCloseList"] = ("Close this list", "关闭这个列表"),

            ["L.CopiedImage"] = ("Image copied.", "已复制图片。"),
            ["L.CopiedPath"] = ("Path copied.", "已复制路径。"),
            ["L.CopyFailed"] = ("Could not copy the image.", "无法复制图片。"),
            ["L.SaveSucceeded"] = ("Saved to {0}.", "已保存到 {0}。"),
            ["L.SaveFailed"] = ("The copy could not be saved.", "无法保存副本。"),
            ["L.NoImage"] = ("Open an image first.", "先打开一张图片。"),

            ["L.Analyze"] = ("Analyze", "分析"),
            ["L.AnalysisTitle"] = ("Analysis", "分析"),
            ["L.AnalysisHistogram"] = ("Histogram", "直方图"),
            ["L.AnalysisColor"] = ("Color", "色彩"),
            ["L.AnalysisQuality"] = ("Quality", "画质"),
            ["L.AnalysisData"] = ("Data", "数据"),
            ["L.AnalysisHistogramMenu"] = ("Histogram…", "直方图…"),
            ["L.AnalysisColorMenu"] = ("Color…", "色彩分析…"),
            ["L.AnalysisQualityMenu"] = ("Quality…", "画质分析…"),
            ["L.AnalysisDataMenu"] = ("Image data…", "图像数据…"),
            ["L.AnalysisEyedropper"] = ("Eyedropper", "取色器"),
            ["L.AnalysisLuma"] = ("Luminance", "亮度"),
            ["L.AnalysisHue"] = ("Hue", "色相"),
            ["L.AnalysisSaturation"] = ("Saturation", "饱和度"),
            ["L.AnalysisMean"] = ("Mean", "平均"),
            ["L.AnalysisMedian"] = ("Median", "中位"),
            ["L.AnalysisStdDev"] = ("Std. dev.", "标准差"),
            ["L.AnalysisShadows"] = ("Shadows", "阴影裁切"),
            ["L.AnalysisHighlights"] = ("Highlights", "高光裁切"),
            ["L.AnalysisToneZones"] = ("Tone zones", "影调分区"),
            ["L.AnalysisToneZonesRange"] = ("0 → 10", "0 → 10"),
            ["L.AnalysisZoneDark"] = ("0 shadows", "0 暗"),
            ["L.AnalysisZoneMid"] = ("5 midtones", "5 中"),
            ["L.AnalysisZoneBright"] = ("10 highlights", "10 亮"),
            ["L.AnalysisHardClipFormat"] = ("Hard clip: 0 = {0} · 255 = {1}", "硬裁切：0 级 {0} · 255 级 {1}"),
            ["L.AnalysisLowSatNote"] = (
                "Low-saturation pixels are excluded from hue. Luminance uses sRGB-encoded values, not linear.",
                "低饱和像素不计入色相。亮度按 sRGB 编码值统计，不转线性。"),
            ["L.AnalysisPalette"] = ("Palette", "主色板"),
            ["L.AnalysisByShare"] = ("By share", "按占比"),
            ["L.AnalysisAverageColor"] = ("Average color", "平均色"),
            ["L.AnalysisWarm"] = ("Warm", "偏暖"),
            ["L.AnalysisNeutral"] = ("Neutral", "中性"),
            ["L.AnalysisCool"] = ("Cool", "偏冷"),
            ["L.AnalysisEyedropperHint"] = ("Move over the image to read; click to pin.", "在画布上移动读取，单击钉住样本。"),
            ["L.AnalysisPreviewCoordinates"] = ("Preview coordinates", "预览坐标"),
            ["L.AnalysisPinnedSamples"] = ("Pinned samples", "钉住样本"),
            ["L.AnalysisPinnedCount"] = ("{0} / 4", "{0} / 4"),
            ["L.AnalysisPinHint"] = (
                "Click the canvas to pin a sample; click a pinned point again to remove it.",
                "在画布上单击钉住样本；再次单击已钉位置移除。"),
            ["L.AnalysisNoSample"] = (
                "Enable Eyedropper in the Analyze menu, then move over the image.",
                "在「分析」菜单打开取色器，然后在画布上移动读取。"),
            ["L.AnalysisNoSampleActive"] = (
                "Move over the image to read a pixel.",
                "在画布上移动以读取像素。"),
            ["L.AnalysisSharpness"] = ("Sharpness", "清晰度"),
            ["L.AnalysisNoise"] = ("Noise", "噪声"),
            ["L.AnalysisCompression"] = ("JPEG compression", "JPEG 压缩"),
            ["L.AnalysisExposure"] = ("Exposure", "曝光"),
            ["L.AnalysisSharp"] = ("Sharp", "清晰"),
            ["L.AnalysisSoft"] = ("Soft", "一般"),
            ["L.AnalysisBlurry"] = ("Blurry", "模糊"),
            ["L.AnalysisLow"] = ("Low", "低"),
            ["L.AnalysisMedium"] = ("Medium", "中"),
            ["L.AnalysisHigh"] = ("High", "高"),
            ["L.AnalysisSharpMethod"] = ("Laplacian variance · 1024 px", "Laplacian 方差 · 长边 1024"),
            ["L.AnalysisRelative"] = ("Relative", "相对"),
            ["L.AnalysisNoiseMethod"] = ("Immerkær estimate · affected by texture", "Immerkær 估算 · 受纹理和锐化影响"),
            ["L.AnalysisEstimate"] = ("Estimate", "估算"),
            ["L.AnalysisJpegMethod"] = ("From quantization tables", "量化表反推"),
            ["L.AnalysisProgressive"] = ("Progressive", "渐进式"),
            ["L.AnalysisBaseline"] = ("Baseline", "基线"),
            ["L.AnalysisQualityNote1"] = (
                "Sharpness and noise describe pixels, not taste; low-texture scenes score low on sharpness.",
                "清晰度、噪声是像素统计，不是审美判断；低纹理场景的清晰度天然偏低。"),
            ["L.AnalysisQualityNote2"] = (
                "JPEG quality is inferred from the quantization tables (about ±5). Subsampling and baseline/progressive are exact.",
                "JPEG 质量由量化表反推，误差约 ±5；子采样、基线/渐进是确定值。"),
            ["L.AnalysisQualityNote3"] = (
                "Exposure reports clipping and the tonal centre; it gives no EV recommendation.",
                "曝光只报告裁切和影调重心，不给 EV 建议。"),
            ["L.AnalysisMethodology"] = ("Method", "口径"),
            ["L.AnalysisExposureClipsFormat"] = ("Shadows {0} · highlights {1}", "阴影 {0} · 高光 {1}"),
            ["L.AnalysisToneShadow"] = ("Dark", "偏暗"),
            ["L.AnalysisToneMid"] = ("Midtones", "中间调"),
            ["L.AnalysisToneHighlight"] = ("Bright", "偏亮"),
            ["L.AnalysisToneCenter"] = ("Tone centre", "影调重心"),
            ["L.AnalysisP99Format"] = ("P99 {0}", "P99 {0}"),
            ["L.AnalysisBytesPerPixelFormat"] = ("{0} B/px", "{0} B/px"),
            ["L.AnalysisEstimatedQualityFormat"] = ("~{0}", "~{0}"),
            ["L.AnalysisDataImage"] = ("Image", "图像"),
            ["L.AnalysisDataFile"] = ("File", "文件"),
            ["L.AnalysisDataColor"] = ("Transparency and colour", "透明与颜色"),
            ["L.AnalysisDataAnimation"] = ("Animation", "动图"),
            ["L.AnalysisSize"] = ("Size", "大小"),
            ["L.AnalysisAnalysisResolution"] = ("Analysis resolution", "分析分辨率"),
            ["L.AnalysisAlphaLabel"] = ("Alpha", "Alpha"),
            ["L.AnalysisUniqueColors"] = ("Unique colors", "唯一颜色"),
            ["L.AnalysisEntropy"] = ("Entropy", "熵"),
            ["L.AnalysisDuration"] = ("Duration", "时长"),
            ["L.AnalysisLoop"] = ("Loop", "循环"),
            ["L.AnalysisOpaque"] = ("Opaque", "不透明"),
            ["L.AnalysisStillImage"] = ("Still image", "静态图片"),
            ["L.AnalysisForever"] = ("Forever", "无限"),
            ["L.AnalysisAtLeastFormat"] = ("≥ {0}", "≥ {0}"),
            ["L.AnalysisAlphaCoverageFormat"] = ("{0} opaque · {1} transparent", "不透明 {0} · 透明 {1}"),
            ["L.AnalysisPreviewNote"] = ("Based on a {0} × {1} preview", "基于 {0} × {1} 预览"),
            ["L.AnalysisOriginalNote"] = ("Original {0} × {1}", "原图 {0} × {1}"),
            ["L.AnalysisPreview"] = ("Preview", "预览"),
            ["L.AnalysisOriginal"] = ("Original", "原图"),
            ["L.AnalysisAnalyzing"] = ("Analyzing…", "正在分析…"),
            ["L.AnalysisClose"] = ("Close analysis", "关闭分析"),
            ["L.AnalysisFailed"] = ("Could not analyze this image.", "无法分析这张图片。"),
            ["L.AnalysisUnavailable"] = ("Analysis is unavailable for this image.", "当前图片无法分析。"),
            ["L.AnalysisDataNote"] = (
                "Dimensions come from file metadata; crop is export-only and does not participate.",
                "尺寸取文件元数据；裁剪是导出区域，不参与分析。"),

            ["L.CommandLineTitle"] = ("AirGlance — command line", "AirGlance — 命令行"),
            ["L.CommandLineVersionFormat"] = ("AirGlance {0}", "AirGlance {0}"),
            ["L.CommandLineHelpBody"] = (
                "Usage: AirGlance [options] [file or folder]\n\n" +
                "  -h, --help            Show this help\n" +
                "      --version         Show the version\n" +
                "      --bench=startup   Measure the first frame, then exit\n" +
                "      --bench=decode    Measure a fit-window decode, then exit\n" +
                "      --bench=scan      Measure folder enumeration and first thumbnails\n" +
                "  --                    Treat the rest as paths\n\n" +
                "A folder opens its first image. Arrow keys then step through the folder.",
                "用法：AirGlance [选项] [文件或文件夹]\n\n" +
                "  -h, --help            显示此帮助\n" +
                "      --version         显示版本号\n" +
                "      --bench=startup   测量首帧后退出\n" +
                "      --bench=decode    测量适应窗口解码后退出\n" +
                "      --bench=scan      测量文件夹枚举和首屏缩略图\n" +
                "  --                    其后参数都按路径处理\n\n" +
                "打开文件夹时从第一张图开始；之后用方向键在文件夹内切换。"),
            ["L.CommandLineMissingImage"] = ("No displayable image was given.", "没有给出可显示的图片。"),
            ["L.VersionFormat"] = ("AirGlance {0}", "AirGlance {0}"),
            ["L.UnknownOptionsFormat"] = ("Ignored: {0}", "已忽略：{0}")
        };

    private ResourceDictionary? _dictionary;

    public string Language { get; private set; } = English;

    public bool IsChinese => string.Equals(Language, ChineseSimplified, StringComparison.Ordinal);

    public string LanguageShortLabel => IsChinese ? "中" : "EN";

    public CultureInfo Culture => IsChinese
        ? CultureInfo.GetCultureInfo("zh-CN")
        : CultureInfo.GetCultureInfo("en-US");

    public event EventHandler? LanguageChanged;

    public void Apply(string? language)
    {
        var normalized = string.Equals(language, ChineseSimplified, StringComparison.OrdinalIgnoreCase) ||
                         (language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ?? false)
            ? ChineseSimplified
            : English;

        var changed = !string.Equals(Language, normalized, StringComparison.Ordinal) || _dictionary is null;
        Language = normalized;

        if (Application.Current is { } application)
        {
            var dictionary = new ResourceDictionary();
            foreach (var pair in Catalog)
            {
                dictionary[pair.Key] = IsChinese ? pair.Value.Zh : pair.Value.En;
            }

            if (_dictionary is not null)
            {
                application.Resources.MergedDictionaries.Remove(_dictionary);
            }

            application.Resources.MergedDictionaries.Add(dictionary);
            _dictionary = dictionary;
        }

        if (changed)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string T(string key) =>
        Catalog.TryGetValue(key, out var value) ? (IsChinese ? value.Zh : value.En) : key;

    public string Format(string key, params object[] args) =>
        string.Format(Culture, T(key), args);

    public void Dispose()
    {
        if (_dictionary is not null && Application.Current is { } application)
        {
            application.Resources.MergedDictionaries.Remove(_dictionary);
        }

        _dictionary = null;
    }
}
