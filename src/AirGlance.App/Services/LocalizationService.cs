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
            ["L.EmptyLimitations"] = ("No HEIC, RAW, TIFF or video yet.", "还不支持 HEIC、RAW、TIFF 和视频。"),
            ["L.EmptyFolderTitle"] = ("No images in this folder.", "这个文件夹里没有可显示的图片。"),
            ["L.EmptyFolderHint"] = ("Try another folder, or press Ctrl+Shift+O.", "换一个文件夹，或者按 Ctrl+Shift+O。"),
            ["L.DropHint"] = ("Release to open", "松开即可打开"),
            ["L.Loading"] = ("Loading…", "正在打开…"),

            ["L.CantOpenTitle"] = ("Can't open this file", "打不开这张图"),
            ["L.ErrorUnsupportedFormat"] = ("Unsupported format ({0}).", "格式不支持（{0}）。"),
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
            ["L.ShortcutGroupApp"] = ("APPLICATION", "应用"),
            ["L.ShortcutNextPrev"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutArrows"] = ("Previous / next image", "上一张 / 下一张"),
            ["L.ShortcutPage"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutSpace"] = ("Next / previous image", "下一张 / 上一张"),
            ["L.ShortcutZoom"] = ("Zoom in / out at the pointer", "以光标为中心放大 / 缩小"),
            ["L.ShortcutWheel"] = ("Previous / next image", "上一张 / 下一张"),
            ["L.ShortcutCtrlWheel"] = ("Zoom at the pointer", "以光标为中心缩放"),
            ["L.ShortcutDoubleClick"] = ("Toggle 100% and fit", "在 100% 与适应窗口之间切换"),
            ["L.ShortcutPresets"] = ("Fit window / 100% / fit width", "适应窗口 / 100% / 适应宽度"),
            ["L.ShortcutRotate"] = ("Rotate clockwise / counter-clockwise", "顺时针 / 逆时针旋转"),
            ["L.ShortcutFlip"] = ("Flip horizontal / vertical", "水平 / 垂直翻转"),
            ["L.ShortcutFullscreen"] = ("Fullscreen; Esc exits", "全屏；Esc 退出"),
            ["L.ShortcutEscape"] = ("Close info/filmstrip; exit fullscreen", "关闭信息/胶片条；退出全屏"),
            ["L.ShortcutSlideshow"] = ("Start or stop the slideshow", "开始 / 停止幻灯片"),
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
