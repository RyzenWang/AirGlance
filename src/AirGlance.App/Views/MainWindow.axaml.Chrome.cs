using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Diagnostics;
using AirGlance.App.Controls;
using AirGlance.App.Services;
using AirGlance.App.ViewModels;
using AirGlance.Core.Imaging;
using AirGlance.Core.Files;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AirGlance.App.Views;

public partial class MainWindow
{
    private void BuildUi()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 720;
        MinHeight = 480;
        Title = "AirGlance";
        Classes.Add("shell");

        _root = new Grid();
        var root = _root;

        // The image owns the whole window. Overlay chrome floats above it.
        CanvasBorder = new Border
        {
            ClipToBounds = true,
            Padding = new Thickness(56, 42, 56, 58)
        };
        CanvasBorder.Classes.Add("canvas");

        var canvasGrid = new Grid();
        Viewer.FirstFramePresented += OnFirstFramePresented;
        Viewer.ZoomChanged += OnViewerZoomChanged;
        Viewer.ViewportChanged += OnViewerViewportChanged;
        canvasGrid.Children.Add(Viewer);

        CropControl = new CropOverlay(Viewer);
        CropControl.SetValue(Panel.ZIndexProperty, 20);
        CropControl.CropChanged += OnCropChanged;
        canvasGrid.Children.Add(CropControl);

        TileLoadingChip = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#E60A0E13")),
            BorderBrush = new SolidColorBrush(Color.Parse("#24FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 10)
        };
        TileLoadingChip.SetValue(Panel.ZIndexProperty, 30);
        TileLoadingText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12
        };
        TileLoadingChip.Child = TileLoadingText;
        canvasGrid.Children.Add(TileLoadingChip);

        LoadingChip = new Border();
        LoadingChip.Classes.Add("loading-chip");
        LoadingText = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        LoadingChip.Child = LoadingText;
        canvasGrid.Children.Add(LoadingChip);

        CanvasBorder.Child = canvasGrid;
        CanvasBorder.AddHandler(
            PointerPressedEvent,
            OnCropCanvasPointerPressed,
            RoutingStrategies.Tunnel);
        CanvasBorder.AddHandler(
            PointerMovedEvent,
            OnCropCanvasPointerMoved,
            RoutingStrategies.Tunnel);
        CanvasBorder.AddHandler(
            PointerReleasedEvent,
            OnCropCanvasPointerReleased,
            RoutingStrategies.Tunnel);
        root.Children.Add(CanvasBorder);

        // Empty / error card.
        EmptyCard = new Border { IsVisible = false };
        EmptyCard.Classes.Add("empty-card");
        EmptyCard.SetValue(Panel.ZIndexProperty, 20);

        var emptyStack = new StackPanel { Spacing = 0 };
        var emptyIcon = new Border();
        emptyIcon.Classes.Add("empty-icon");
        var emptyGlyph = new IconPath
        {
            Data = FindGeometry("IconFolderPlus"),
            Width = 27,
            Height = 27
        };
        emptyGlyph.Classes.Add("empty-glyph");
        emptyIcon.Child = emptyGlyph;
        emptyStack.Children.Add(emptyIcon);

        OverlayTitle = new TextBlock();
        OverlayTitle.Classes.Add("empty-title");
        emptyStack.Children.Add(OverlayTitle);

        OverlayMessage = new TextBlock { Margin = new Thickness(0, 9, 0, 0) };
        OverlayMessage.Classes.Add("empty-message");
        emptyStack.Children.Add(OverlayMessage);

        OverlayHint = new TextBlock { Margin = new Thickness(0, 5, 0, 0) };
        OverlayHint.Classes.Add("empty-hint");
        emptyStack.Children.Add(OverlayHint);

        var emptyActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Margin = new Thickness(0, 22, 0, 0)
        };
        EmptyOpenFolderButton = new Button();
        EmptyOpenFolderButton.Classes.Add("empty-primary");
        EmptyOpenFolderText = AttachEmptyActionIcon(EmptyOpenFolderButton, "IconFolder");
        EmptyOpenFolderButton.Click += OnOpenFolderClick;
        EmptyOpenImageButton = new Button();
        EmptyOpenImageButton.Classes.Add("empty-secondary");
        EmptyOpenImageText = AttachEmptyActionIcon(EmptyOpenImageButton, "IconImage");
        EmptyOpenImageButton.Click += OnOpenImageClick;
        emptyActions.Children.Add(EmptyOpenFolderButton);
        emptyActions.Children.Add(EmptyOpenImageButton);
        emptyStack.Children.Add(emptyActions);

        var shortcutHint = new TextBlock
        {
            Text = "Ctrl+O  \u00B7  Ctrl+Shift+O",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0)
        };
        shortcutHint.Classes.Add("empty-shortcut");
        emptyStack.Children.Add(shortcutHint);

        EmptyCard.Child = emptyStack;
        root.Children.Add(EmptyCard);

        // Top chrome: folder chip, file chip, window controls.
        TopBar = new Border
        {
            Height = 38,
            Margin = new Thickness(18, 16, 18, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent
        };
        TopBar.PointerPressed += OnTitleBarPointerPressed;
        TopBar.SetValue(Panel.ZIndexProperty, 40);

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        FolderChip = new Border { IsVisible = false, MaxWidth = 300 };
        FolderChip.Classes.Add("overlay-chip");
        var folderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderRow.Children.Add(new IconPath
        {
            Data = FindGeometry("IconFolder"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            Width = 15,
            Height = 15
        });
        FolderNameText = new TextBlock();
        FolderNameText.Classes.Add("chrome-name");
        FolderNameText.MaxWidth = 180;
        folderRow.Children.Add(FolderNameText);
        FolderCountText = new TextBlock();
        FolderCountText.Classes.Add("chrome-meta");
        folderRow.Children.Add(FolderCountText);
        FolderChip.Child = folderRow;
        topGrid.Children.Add(FolderChip);

        BrandChip = new Border { IsVisible = false };
        BrandChip.Classes.Add("overlay-chip");
        var brandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        var brandMark = new Border();
        brandMark.Classes.Add("chrome-icon");
        brandRow.Children.Add(brandMark);
        var brandName = new TextBlock { Text = "AirGlance" };
        brandName.Classes.Add("chrome-name");
        brandRow.Children.Add(brandName);
        BrandChip.Child = brandRow;
        topGrid.Children.Add(BrandChip);

        FileChip = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520
        };
        FileChip.Classes.Add("overlay-chip");
        var fileRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,54"),
            VerticalAlignment = VerticalAlignment.Center
        };
        FileNameText = new TextBlock { MaxWidth = 200, TextTrimming = TextTrimming.CharacterEllipsis };
        FileNameText.Classes.Add("chrome-file");
        fileRow.Children.Add(FileNameText);
        var fileSeparator = new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(10, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF")),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(fileSeparator, 1);
        fileRow.Children.Add(fileSeparator);
        FileMetaText = new TextBlock
        {
            MaxWidth = 300,
            Margin = new Thickness(0, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        FileMetaText.Classes.Add("chrome-meta");
        Grid.SetColumn(FileMetaText, 2);
        fileRow.Children.Add(FileMetaText);
        FilePositionText = new TextBlock
        {
            Width = 54,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.Parse("#F4A593"))
        };
        FilePositionText.Classes.Add("chrome-meta");
        Grid.SetColumn(FilePositionText, 3);
        fileRow.Children.Add(FilePositionText);
        FileChip.Child = fileRow;
        Grid.SetColumn(FileChip, 0);
        Grid.SetColumnSpan(FileChip, 3);
        topGrid.Children.Add(FileChip);

        WindowChip = new Border { IsVisible = OperatingSystem.IsWindows(), Padding = new Thickness(4) };
        WindowChip.Classes.Add("overlay-chip");
        CaptionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        ThemeButton = CreateWindowButton("IconTheme", "L.ToggleTheme", OnThemeClick, iconSize: 14);
        MinimizeButton = CreateWindowButton("IconMinimize", "L.Minimize", OnMinimizeClick, iconSize: 14);
        MaximizeButton = CreateWindowButton("IconMaximize", "L.Maximize", OnMaximizeClick, iconSize: 13);
        MaximizeIcon = (IconPath)MaximizeButton.Content!;
        CloseButton = CreateWindowButton("IconClose", "L.Close", OnCloseClick, iconSize: 14, close: true);
        CaptionButtons.Children.Add(ThemeButton);
        CaptionButtons.Children.Add(MinimizeButton);
        CaptionButtons.Children.Add(MaximizeButton);
        CaptionButtons.Children.Add(CloseButton);
        WindowChip.Child = CaptionButtons;
        WindowChip.HorizontalAlignment = HorizontalAlignment.Right;
        WindowChip.VerticalAlignment = VerticalAlignment.Top;
        WindowChip.Margin = new Thickness(0, 16, 18, 0);
        WindowChip.SetValue(Panel.ZIndexProperty, 60);
        root.Children.Add(WindowChip);

        TopBar.Child = topGrid;
        root.Children.Add(TopBar);

        // Side navigation. Each button gets its own 46 x 46 host so the layer that
        // fades never covers the canvas, while the buttons keep their resting opacity.
        PrevButton = CreateNavButton("IconChevronLeft", "L.PreviousImage", OnPreviousClick);
        NextButton = CreateNavButton("IconChevronRight", "L.NextImage", OnNextClick);

        PrevHost = new Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 0, 0, 0),
            IsHitTestVisible = false
        };
        NextHost = new Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 22, 0),
            IsHitTestVisible = false
        };
        PrevHost.Children.Add(PrevButton);
        NextHost.Children.Add(NextButton);
        PrevHost.SetValue(Panel.ZIndexProperty, 35);
        NextHost.SetValue(Panel.ZIndexProperty, 35);
        root.Children.Add(PrevHost);
        root.Children.Add(NextHost);

        // Bottom dock.
        Dock = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 22)
        };
        Dock.Classes.Add("dock");
        Dock.SetValue(Panel.ZIndexProperty, 50);

        var dockRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        };

        ZoomOutButton = CreateToolButton("IconMinus", "L.ZoomOut", (_, _) => Viewer.ZoomOut());
        dockRow.Children.Add(ZoomOutButton);

        DockZoomText = new TextBlock { Text = "100%" };
        DockZoomText.Classes.Add("dock-zoom");
        ZoomButton = new Button
        {
            Content = DockZoomText,
            Width = 48,
            Height = 40,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        ZoomButton.Click += OnZoomPresetClick;
        dockRow.Children.Add(ZoomButton);

        ZoomInButton = CreateToolButton("IconPlus", "L.ZoomIn", (_, _) => Viewer.ZoomIn());
        dockRow.Children.Add(ZoomInButton);

        dockRow.Children.Add(CreateDockDivider());
        FitButton = CreateToolButton("IconFit", "L.FitWindow", (_, _) => Viewer.AnimateZoomMode(ViewerZoomMode.FitWindow));
        dockRow.Children.Add(FitButton);

        ActualButton = new Button
        {
            Content = new TextBlock
            {
                Text = "1:1",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        ((TextBlock)ActualButton.Content).Classes.Add("tool-text");
        ActualButton.Classes.Add("tool");
        ActualButton.Classes.Add("wide");
        ActualButton.Click += (_, _) => Viewer.AnimateZoomMode(ViewerZoomMode.Actual);
        dockRow.Children.Add(ActualButton);

        dockRow.Children.Add(CreateDockDivider());
        RotateButton = CreateToolButton(
            "IconRotate",
            "L.RotateClockwise",
            async (_, _) => await _viewModel.RotateClockwiseAsync());
        dockRow.Children.Add(RotateButton);

        InfoToggle = CreateToolButton("IconInfo", "L.InfoPanel", OnInfoToggleClick);
        dockRow.Children.Add(InfoToggle);

        FilmstripToggle = CreateToolButton("IconFilmstrip", "L.Filmstrip", OnFilmstripToggleClick);
        dockRow.Children.Add(FilmstripToggle);

        DualViewButton = CreateToolButton(
            "IconSplitView",
            "L.DualView",
            (_, _) => _ = ToggleDualAsync());
        dockRow.Children.Add(DualViewButton);

        SlideshowButton = CreateToolButton("IconPlay", "L.Slideshow", (_, _) => _viewModel.ToggleSlideshow());
        dockRow.Children.Add(SlideshowButton);

        dockRow.Children.Add(CreateDockDivider());
        EditButton = CreateToolButton("IconEdit", "L.EditHint", OnEditClick);
        dockRow.Children.Add(EditButton);
        FullscreenButton = CreateToolButton("IconFullscreen", "L.Fullscreen", OnFullscreenClick);
        dockRow.Children.Add(FullscreenButton);

        SettingsButton = CreateToolButton("IconSettings", "L.Settings", OnSettingsClick);
        dockRow.Children.Add(SettingsButton);

        Dock.Child = dockRow;
        root.Children.Add(Dock);

        // Filmstrip is an overlay drawer, not a layout row.
        FilmstripHost = new Border { IsVisible = false };
        FilmstripHost.Classes.Add("filmstrip-overlay");
        FilmstripHost.SetValue(Panel.ZIndexProperty, 30);
        root.Children.Add(FilmstripHost);

        // Information drawer.
        InfoHost = new Border { IsVisible = false };
        InfoHost.Classes.Add("info-overlay");
        InfoHost.SetValue(Panel.ZIndexProperty, 36);

        var infoGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var infoHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 18, 10, 4)
        };
        InfoTitleText = new TextBlock();
        InfoTitleText.Classes.Add("info-title");
        infoHeader.Children.Add(InfoTitleText);
        var infoClose = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry("IconClose"),
                Stroke = new SolidColorBrush(Color.Parse("#9A9286")),
                StrokeThickness = 1.7,
                Width = 15,
                Height = 15
            },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        infoClose.Click += OnInfoToggleClick;
        Grid.SetColumn(infoClose, 1);
        infoHeader.Children.Add(infoClose);
        infoGrid.Children.Add(infoHeader);

        InfoContentHost = new ContentControl();
        Grid.SetRow(InfoContentHost, 1);
        infoGrid.Children.Add(InfoContentHost);
        InfoHost.Child = infoGrid;
        root.Children.Add(InfoHost);

        // Analysis drawer. Same geometry as the information drawer; only one of the
        // two is visible at a time.
        AnalysisHost = new Border { IsVisible = false };
        AnalysisHost.Classes.Add("analysis-drawer");
        AnalysisHost.SetValue(Panel.ZIndexProperty, 36);
        AnalysisContentHost = new ContentControl();
        AnalysisHost.Child = AnalysisContentHost;
        root.Children.Add(AnalysisHost);

        EditPanel = new EditPanelView(_services.Localization)
        {
            IsVisible = false
        };
        EditPanel.CloseRequested += OnEditCloseRequested;
        EditPanel.CancelRequested += OnEditCloseRequested;
        EditPanel.ExportRequested += OnEditExportRequested;
        EditPanel.ResetRequested += OnEditResetRequested;
        EditPanel.TransformRequested += OnEditTransformRequested;
        EditPanel.CropRequested += OnEditCropRequested;
        root.Children.Add(EditPanel);

        CropBar = new CropToolbarView(_services.Localization) { IsVisible = false };
        CropBar.SetValue(Panel.ZIndexProperty, 55);
        CropBar.ResetRequested += OnCropBarResetRequested;
        CropBar.CancelRequested += OnCropBarCancelRequested;
        CropBar.DoneRequested += OnCropBarDoneRequested;
        CropBar.AspectRequested += OnEditCropAspectRequested;
        root.Children.Add(CropBar);

        CropModeTag = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, 22, 0, 0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(178, 15, 12, 10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = _services.Localization.T("L.EditCropTag"),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 0.8,
                Foreground = new SolidColorBrush(Color.Parse("#F5EFE8"))
            }
        };
        CropModeTag.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(CropModeTag);

        CropSafeTag = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 22, 24, 0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(178, 15, 12, 10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = _services.Localization.T("L.EditSafeTag"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.Parse("#D8D2CA"))
            }
        };
        CropSafeTag.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(CropSafeTag);

        // Transient toast.
        ToastBorder = new Border { IsVisible = false };
        ToastBorder.Classes.Add("toast");
        TransientMessageText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#F7F3EA")),
            FontSize = 11
        };
        ToastBorder.Child = TransientMessageText;
        ToastBorder.SetValue(Panel.ZIndexProperty, 55);
        root.Children.Add(ToastBorder);

        // Drop hint on top of everything.
        DragOverlay = new Border { IsVisible = false };
        DragOverlay.Classes.Add("dragover");
        DragOverlay.SetValue(Panel.ZIndexProperty, 60);
        var dropHintBorder = new Border
        {
            Padding = new Thickness(18, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dropHintBorder.Classes.Add("panel");
        DropHintText = new TextBlock();
        DropHintText.Classes.Add("drop-hint");
        dropHintBorder.Child = DropHintText;
        DragOverlay.Child = dropHintBorder;
        root.Children.Add(DragOverlay);

        // Fullscreen keeps a single quiet hint in the bottom-right corner.
        FullscreenHint = new Border { IsVisible = false };
        FullscreenHint.Classes.Add("fullscreen-hint");
        FullscreenHint.Margin = new Thickness(0, 0, 22, 18);
        FullscreenHint.SetValue(Panel.ZIndexProperty, 50);
        FullscreenText = new TextBlock();
        FullscreenText.Classes.Add("fullscreen-hint-text");
        FullscreenHint.Child = FullscreenText;
        root.Children.Add(FullscreenHint);

        Content = root;
    }

    private Button CreateToolButton(
        string iconResource,
        string tooltipKey,
        EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = 18,
                Height = 18
            }
        };
        button.Classes.Add("tool");
        button.Click += handler;
        button.Tag = tooltipKey;
        return button;
    }

    private Button CreateNavButton(
        string iconResource,
        string tooltipKey,
        EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = 19,
                Height = 19
            }
        };
        button.Classes.Add("nav-button");
        button.Click += handler;
        button.Tag = tooltipKey;
        return button;
    }

    private Button CreateWindowButton(
        string iconResource,
        string automationKey,
        EventHandler<RoutedEventArgs> clickHandler,
        double iconSize,
        bool close = false)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(iconResource),
                Width = iconSize,
                Height = iconSize
            }
        };
        button.Classes.Add("window-tool");
        if (close)
        {
            button.Classes.Add("close");
        }

        button.Click += clickHandler;
        button.Tag = automationKey;
        return button;
    }

    private TextBlock AttachEmptyActionIcon(Button button, string iconResource)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(new IconPath
        {
            Data = FindGeometry(iconResource),
            Width = 15,
            Height = 15
        });
        row.Children.Add(label);
        button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        return label;
    }

    private Border CreateDockDivider()
    {
        var divider = new Border();
        divider.Classes.Add("dock-divider");
        return divider;
    }

    private Geometry FindGeometry(string key) =>
        this.FindResource(key) as Geometry ?? StreamGeometry.Parse("M 0 0");

    private void ApplyLanguage()
    {
        LoadingText.Text = _services.Localization.T("L.Loading");
        TileLoadingText.Text = _viewModel.RegionLoadingText;
        DropHintText.Text = _services.Localization.T("L.DropHint");
        AutomationProperties.SetName(MinimizeButton, _services.Localization.T("L.Minimize"));
        AutomationProperties.SetName(MaximizeButton, _services.Localization.T("L.Maximize"));
        AutomationProperties.SetName(CloseButton, _services.Localization.T("L.Close"));

        UpdateThemeButton();
        ApplyTool(ZoomButton, "L.ZoomLevel");
        ApplyTool(ZoomOutButton, "L.ZoomOut");
        ApplyTool(ZoomInButton, "L.ZoomIn");
        ApplyTool(FitButton, "L.FitWindow");
        ApplyTool(ActualButton, "L.ActualSize");
        ApplyTool(RotateButton, "L.RotateClockwise");
        ApplyTool(EditButton, "L.EditHint");
        ApplyTool(InfoToggle, "L.InfoPanel");
        ApplyTool(FilmstripToggle, "L.Filmstrip");
        ApplyTool(DualViewButton, "L.DualView");
        ApplyTool(SlideshowButton, "L.Slideshow");
        ApplyTool(FullscreenButton, "L.Fullscreen");
        ApplyTool(SettingsButton, "L.Settings");
        ApplyTool(PrevButton, "L.PreviousImage");
        ApplyTool(NextButton, "L.NextImage");
        EditPanel.ApplyLanguage();

        InfoTitleText.Text = _services.Localization.T("L.InfoPanelShort");
        FullscreenText.Text = _services.Localization.T("L.FullscreenHint");
        EmptyOpenFolderText.Text = _services.Localization.T("L.OpenFolder").TrimEnd('…');
        EmptyOpenImageText.Text = _services.Localization.T("L.OpenFile").TrimEnd('…');
    }

    private void ApplyTool(Button button, string key)
    {
        var text = _services.Localization.T(key);
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
        CropControl.RefreshTheme();
    }

    private void UpdateThemeButton()
    {
        var dark = _services.Theme.IsDark;
        if (ThemeButton.Content is IconPath icon)
        {
            icon.Data = FindGeometry(dark ? "IconMoon" : "IconTheme");
        }

        ApplyTool(ThemeButton, dark ? "L.SwitchToLight" : "L.SwitchToDark");
    }

    private void ApplyViewModelState()
    {
        Title = _viewModel.WindowTitle;
        FolderNameText.Text = _viewModel.FolderName;
        UpdateTopBarLayout();
        FolderCountText.Text = _viewModel.Images.Count > 0
            ? _viewModel.Images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        FileChip.IsVisible = _viewModel.HasImage;
        FileNameText.Text = _viewModel.TitleFileName;
        var meta = new List<string>(2);
        if (!string.IsNullOrEmpty(_viewModel.StatusDimensions))
        {
            meta.Add(_viewModel.StatusDimensions);
        }

        if (!string.IsNullOrEmpty(_viewModel.ZoomText))
        {
            meta.Add(_viewModel.ZoomText);
        }

        FileMetaText.Text = string.Join(" · ", meta);
        var count = _viewModel.Images.Count;
        FilePositionText.Text = count == 0
            ? string.Empty
            : $"{_viewModel.CurrentIndex + 1:D2} / {count:D2}";

        DockZoomText.Text = _viewModel.ZoomText;
        EmptyCard.IsVisible = _viewModel.OverlayVisible;
        OverlayTitle.Text = _viewModel.OverlayTitle;
        OverlayMessage.Text = _viewModel.OverlayMessage;
        OverlayMessage.IsVisible = !string.IsNullOrEmpty(_viewModel.OverlayMessage);
        OverlayHint.Text = _viewModel.OverlayHint;
        OverlayHint.IsVisible = !string.IsNullOrEmpty(_viewModel.OverlayHint);

        LoadingChip.IsVisible = _viewModel.IsLoading;
        TileLoadingText.Text = _viewModel.RegionLoadingText;
        TileLoadingChip.IsVisible = _viewModel.ShowRegionLoading;
        InfoHost.IsVisible = _viewModel.InfoShown && !_cropMode;
        InfoHost.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 82, 22, 150)
            : new Thickness(0, 82, 22, 92);
        AnalysisHost.IsVisible = _viewModel.AnalysisShown && !_cropMode && !_viewModel.IsEditing;
        AnalysisHost.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 82, 22, 150)
            : new Thickness(0, 82, 22, 92);
        if (AnalysisHost.IsVisible)
        {
            EnsureAnalysisPanel();
        }
        Viewer.IsPicking = _viewModel.Analysis.IsEyedropperActive &&
                           _viewModel.AnalysisShown &&
                           !_viewModel.IsEditing &&
                           !_dualMode &&
                           !_cropMode;
        EditPanel.IsVisible = _viewModel.IsEditing && !_cropMode;
        EditPanel.Margin = new Thickness(0, 82, 22, 0);
        CropBar.IsVisible = _viewModel.IsEditing && _cropMode;
        CropModeTag.IsVisible = _viewModel.IsEditing && _cropMode;
        CropSafeTag.IsVisible = _viewModel.IsEditing && _cropMode;
        CropControl.IsActive = _viewModel.IsEditing && _cropMode;
        if (_viewModel.IsEditing && !_cropDragging && CropControl.Crop != _viewModel.Crop)
        {
            CropControl.SetCrop(_viewModel.Crop);
            UpdateCropUi();
        }

        EditButton.IsEnabled = _viewModel.HasImage && !_dualMode && _viewModel.CanEdit;
        EditButton.Classes.Set("active", _viewModel.IsEditing);
        var editTip = _dualMode
            ? _services.Localization.T("L.EditDualUnavailable")
            : _viewModel.IsProxyOnly
                ? _services.Localization.T("L.EditNeedsFullResolution")
                : _viewModel.IsTiled
                    ? _services.Localization.T("L.EditUnavailable")
                    : _services.Localization.T("L.EditHint");
        ToolTip.SetTip(EditButton, editTip);
        FilmstripHost.IsVisible = _viewModel.FilmstripShown && !_cropMode;
        Dock.IsVisible = !_viewModel.OverlayVisible && !_cropMode;
        Dock.Margin = _viewModel.FilmstripShown
            ? new Thickness(0, 0, 0, 150)
            : new Thickness(0, 0, 0, 22);

        // Navigation depends on the folder, not on the current file: a corrupt image must
        // not strand the user with no way to reach the next one.
        var showNavigation = count > 1 && !_cropMode;
        PrevButton.IsVisible = showNavigation;
        NextButton.IsVisible = showNavigation;
        if (showNavigation && _viewModel.HasError && !_chromeVisible)
        {
            // The pointer may be nowhere near the canvas after a keyboard step onto a
            // broken file, so bring the arrows back with the error card.
            ShowChrome();
        }

        InfoToggle.Classes.Set("active", _viewModel.InfoShown);
        FilmstripToggle.Classes.Set("active", _viewModel.FilmstripShown);
        FullscreenButton.Classes.Set("active", _viewModel.IsFullscreen);
        TopBar.IsVisible = !_viewModel.IsFullscreen && !_cropMode;

        TransientMessageText.Text = _viewModel.TransientMessage ?? string.Empty;
        ToastBorder.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.TransientMessage);
        DragOverlay.IsVisible = _viewModel.IsDragOver;

        CanvasBorder.Padding = _cropMode
            ? new Thickness(32, 80, 32, 150)
            : _viewModel.IsFullscreen
                ? new Thickness(0)
                : new Thickness(56, 42, 56, 58);
        if (_viewModel.IsFullscreen)
        {
            if (!CanvasBorder.Classes.Contains("fullscreen"))
            {
                CanvasBorder.Classes.Add("fullscreen");
            }
        }
        else
        {
            CanvasBorder.Classes.Remove("fullscreen");
        }

        RefreshChromeVisibility();

        if (_dualMode)
        {
            CanvasBorder.IsVisible = false;
            EmptyCard.IsVisible = false;
            TopBar.IsVisible = false;
            Dock.IsVisible = false;
            PrevHost.IsVisible = false;
            NextHost.IsVisible = false;
            FilmstripHost.IsVisible = false;
            InfoHost.IsVisible = false;
            AnalysisHost.IsVisible = false;
            FullscreenHint.IsVisible = false;
            DragOverlay.IsVisible = false;
            WindowChip.IsVisible = OperatingSystem.IsWindows() && !_viewModel.IsFullscreen;
            if (_dualView is not null)
            {
                _dualView.IsVisible = true;
            }

            return;
        }

        if (_dualView is not null)
        {
            _dualView.IsVisible = false;
        }

        CanvasBorder.IsVisible = true;
        WindowChip.IsVisible = OperatingSystem.IsWindows() && !_viewModel.IsFullscreen && !_cropMode;
    }

    private void UpdateTopBarLayout()
    {
        var compact = Bounds.Width > 0 && Bounds.Width < 900;
        var hasFolder = !string.IsNullOrWhiteSpace(_viewModel.FolderName);
        FolderChip.IsVisible = !compact && hasFolder;
        BrandChip.IsVisible = !hasFolder;
        FileChip.MaxWidth = compact ? Math.Max(220, Bounds.Width * 0.62) : 520;
    }

    private void ShowChrome()
    {
        _chromeVisible = true;
        RefreshChromeVisibility();
        _chromeTimer.Stop();
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        _chromeVisible = false;
        RefreshChromeVisibility();
    }

    private void RefreshChromeVisibility()
    {
        if (_viewModel is null)
        {
            return;
        }

        var empty = _viewModel.OverlayVisible;
        var topVisible = _chromeVisible || empty;
        TopBar.IsVisible = !_viewModel.IsFullscreen && !_cropMode;
        TopBar.Opacity = topVisible ? 1 : 0;
        TopBar.IsHitTestVisible = topVisible;

        var controlsVisible = _chromeVisible && !empty;
        Dock.Opacity = controlsVisible && !_cropMode ? 1 : 0;
        Dock.IsHitTestVisible = controlsVisible && !_cropMode;

        // The arrows follow the folder rather than the overlay: an error card occupies
        // the centre of the canvas, and hiding navigation there trapped the user.
        var navVisible = _chromeVisible &&
                         !_viewModel.IsFullscreen &&
                         !_cropMode &&
                         _viewModel.Images.Count > 1;
        PrevHost.Opacity = navVisible ? 1 : 0;
        NextHost.Opacity = navVisible ? 1 : 0;
        PrevHost.IsHitTestVisible = navVisible;
        NextHost.IsHitTestVisible = navVisible;

        // In fullscreen the hint appears once the dock has faded out, so the two
        // never share the bottom-right corner on a narrow screen.
        FullscreenHint.IsVisible = _viewModel.IsFullscreen && !_chromeVisible && !_cropMode;
    }

    private void OnChromeTimerTick(object? sender, EventArgs e)
    {
        _chromeTimer.Stop();
        if (_viewModel.OverlayVisible)
        {
            return;
        }

        if (TopBar.IsPointerOver || Dock.IsPointerOver ||
            PrevButton.IsPointerOver || NextButton.IsPointerOver ||
            InfoHost.IsPointerOver || FilmstripHost.IsPointerOver)
        {
            _chromeTimer.Start();
            return;
        }

        HideChrome();
    }

    private void ConfigureWindowChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            // BorderOnly keeps the native resize border while the caption row is ours.
            SystemDecorations = SystemDecorations.BorderOnly;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 44;
            CaptionButtons.IsVisible = true;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            SystemDecorations = SystemDecorations.Full;
        }
    }

    private void SetWindowIcon()
    {
        if (Icon is not null)
        {
            return;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://AirGlance/Assets/airglance.png"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // An icon is cosmetic; a failure must not affect the window.
        }
    }

    private void UpdateMaximizeIcon()
    {
        var key = WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize";
        if (this.FindResource(key) is Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }
    }
}
