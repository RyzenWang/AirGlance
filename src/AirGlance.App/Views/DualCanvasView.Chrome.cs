using System.Globalization;
using AirGlance.App.Controls;
using AirGlance.App.Imaging;
using AirGlance.App.Rendering;
using AirGlance.App.Services;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AirGlance.App.Views;

public sealed partial class DualCanvasView
{
    private PaneSlot CreatePane(int index)
    {
        var viewer = new ImageViewer
        {
            Margin = new Thickness(30, 72, 30, 68)
        };
        var host = new Border { Classes = { "dual-pane" } };
        var content = new Grid();
        content.Children.Add(viewer);

        var activeFrame = new Border { Classes = { "dual-active-frame", index == 0 ? "a" : "b" } };
        activeFrame.SetValue(Panel.ZIndexProperty, 25);
        content.Children.Add(activeFrame);

        var header = BuildHeader(index, out var badge, out var folderName, out var folderCount,
            out var fileName, out var fileMeta, out var fileZoom, out var filePosition,
            out var folderChip, out var fileChip, out var fileSeparator1, out var fileSeparator2);
        content.Children.Add(header);

        var navGroup = BuildPaneNav(index, out var navBadge, out var navPrev, out var navNext);
        content.Children.Add(navGroup);

        var loading = new Border();
        loading.Classes.Add("loading-chip");
        var loadingText = new TextBlock
        {
            Text = _services.Localization.T("L.Loading"),
            Foreground = Brushes.White,
            FontSize = 12
        };
        loading.Child = loadingText;
        loading.IsVisible = false;
        content.Children.Add(loading);

        var empty = BuildEmpty(index, out var emptyTitle, out var emptyDetail);
        content.Children.Add(empty);

        host.Child = content;
        viewer.PointerWheelChanged += (_, _) => SetActive(index);
        host.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (args.GetCurrentPoint(host).Properties.IsRightButtonPressed)
            {
                OpenPaneMenu(index, args.GetPosition(this));
                args.Handled = true;
                return;
            }

            SetActive(index);
            ShowChrome();
            Focus();
        }, RoutingStrategies.Tunnel);
        folderChip.PointerPressed += (_, args) =>
        {
            args.Handled = true;
            OpenRequested?.Invoke(this, new DualOpenRequest(index, true));
        };

        return new PaneSlot(index, viewer, host, activeFrame, header, badge, folderName, folderCount,
            fileName, fileMeta, fileZoom, filePosition, folderChip, fileChip, fileSeparator1,
            fileSeparator2, loading, empty, emptyTitle, emptyDetail, navGroup, navBadge, navPrev,
            navNext);
    }

    private StackPanel BuildHeader(
        int index,
        out Border badge,
        out TextBlock folderName,
        out TextBlock folderCount,
        out TextBlock fileName,
        out TextBlock fileMeta,
        out TextBlock fileZoom,
        out TextBlock filePosition,
        out Border folderChip,
        out Border fileChip,
        out Border fileSeparator1,
        out Border fileSeparator2)
    {
        var row = new StackPanel
        {
            Classes = { "dual-pane-header", index == 0 ? "a" : "b" },
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Margin = new Thickness(16, 16, index == 1 ? 166 : 16, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        row.SetValue(Panel.ZIndexProperty, 30);
        row.ClipToBounds = true;

        badge = new Border { Classes = { "dual-badge", index == 0 ? "a" : "b" } };
        badge.Child = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        row.Children.Add(badge);

        folderChip = new Border
        {
            Classes = { "overlay-chip", "dual-folder-chip", index == 0 ? "a" : "b" },
            MaxWidth = 190,
            ClipToBounds = true
        };
        var folderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        folderRow.Children.Add(new IconPath
        {
            Data = FindGeometry("IconFolder"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            Width = 14,
            Height = 14
        });
        folderName = new TextBlock { MaxWidth = 125 };
        folderName.Classes.Add("chrome-name");
        folderRow.Children.Add(folderName);
        folderCount = new TextBlock();
        folderCount.Classes.Add("chrome-meta");
        folderRow.Children.Add(folderCount);
        folderChip.Child = folderRow;
        row.Children.Add(folderChip);

        fileChip = new Border
        {
            Classes = { "overlay-chip", "dual-file-chip", index == 0 ? "a" : "b" },
            MaxWidth = 430,
            Padding = new Thickness(9, 0),
            ClipToBounds = true
        };
        var fileRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,46"),
            VerticalAlignment = VerticalAlignment.Center
        };
        fileName = new TextBlock { MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
        fileName.Classes.Add("chrome-file");
        fileRow.Children.Add(fileName);
        fileSeparator1 = new Border
        {
            Width = 1,
            Height = 15,
            Margin = new Thickness(4, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF"))
        };
        Grid.SetColumn(fileSeparator1, 1);
        fileRow.Children.Add(fileSeparator1);
        fileMeta = new TextBlock { Margin = new Thickness(0, 0, 4, 0) };
        fileMeta.Classes.Add("chrome-meta");
        Grid.SetColumn(fileMeta, 2);
        fileRow.Children.Add(fileMeta);
        fileZoom = new TextBlock { Margin = new Thickness(0, 0, 6, 0) };
        fileZoom.Classes.Add("chrome-meta");
        Grid.SetColumn(fileZoom, 3);
        fileRow.Children.Add(fileZoom);
        fileSeparator2 = new Border
        {
            Width = 1,
            Height = 15,
            Margin = new Thickness(1, 0, 4, 0),
            Background = new SolidColorBrush(Color.Parse("#24FFFFFF"))
        };
        Grid.SetColumn(fileSeparator2, 4);
        fileRow.Children.Add(fileSeparator2);
        filePosition = new TextBlock
        {
            Width = 46,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(Color.Parse(index == 0 ? "#F4A593" : "#8BC8BB"))
        };
        filePosition.Classes.Add("chrome-meta");
        Grid.SetColumn(filePosition, 5);
        fileRow.Children.Add(filePosition);
        fileChip.Child = fileRow;
        row.Children.Add(fileChip);
        return row;
    }

    private Border BuildEmpty(int index, out TextBlock title, out TextBlock detail)
    {
        var card = new Border();
        card.Classes.Add("dual-empty-card");
        card.IsVisible = false;

        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = index == 0
                ? new SolidColorBrush(Color.Parse("#1FE3563B"))
                : new SolidColorBrush(Color.Parse("#1F2E6F63")),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.Child = new IconPath
        {
            Data = FindGeometry("IconFolderPlus"),
            Stroke = index == 0
                ? new SolidColorBrush(Color.Parse("#E3563B"))
                : new SolidColorBrush(Color.Parse("#2E6F63")),
            StrokeThickness = 1.6,
            Width = 22,
            Height = 22
        };
        stack.Children.Add(icon);

        title = new TextBlock
        {
            Text = _services.Localization.T("L.DualEmptyTitle"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(title);
        detail = new TextBlock
        {
            Text = _services.Localization.T("L.DualEmptyDetail"),
            Classes = { "dual-empty-detail" }
        };
        stack.Children.Add(detail);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var folderButton = new Button
        {
            Content = _services.Localization.T("L.OpenFolder").TrimEnd('…'),
            Classes = { "empty-primary" }
        };
        folderButton.Click += (_, _) => OpenRequested?.Invoke(this, new DualOpenRequest(index, true));
        var imageButton = new Button
        {
            Content = _services.Localization.T("L.OpenFile").TrimEnd('…'),
            Classes = { "empty-secondary" }
        };
        imageButton.Click += (_, _) => OpenRequested?.Invoke(this, new DualOpenRequest(index, false));
        actions.Children.Add(folderButton);
        actions.Children.Add(imageButton);
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private Border BuildDivider()
    {
        var host = new Border
        {
            Width = 18,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.SizeWestEast)
        };
        var line = new Border();
        line.Classes.Add("dual-divider-line");
        var grip = new Border
        {
            Width = 18,
            Height = 44,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#C40A0E13")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "⋮",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.Parse("#B9B1A5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        host.Child = new Grid { Children = { line, grip } };
        host.PointerPressed += OnDividerPointerPressed;
        host.PointerMoved += OnDividerPointerMoved;
        host.PointerReleased += OnDividerPointerReleased;
        host.DoubleTapped += (_, _) => ResetDivider();
        return host;
    }

    private Border BuildDock()
    {
        var dock = new Border { Classes = { "dock" }, HorizontalAlignment = HorizontalAlignment.Center };
        dock.SetValue(Panel.ZIndexProperty, 50);
        return dock;
    }

    private TextBlock BuildDockCore(
        Border dock,
        out Button infoButton,
        out Button filmstripButton,
        out TextBlock zoomText)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        _activeBadge = new Border { Classes = { "dual-dock-pane", "a" } };
        _activeBadgeText = new TextBlock { Text = "A", Classes = { "dual-dock-pane-text" } };
        _activeBadge.Child = _activeBadgeText;
        row.Children.Insert(0, _activeBadge);

        Button Tool(string icon, string tooltip, Action action)
        {
            var button = new Button
            {
                Content = new IconPath
                {
                    Data = FindGeometry(icon),
                    Width = 18,
                    Height = 18
                },
                Classes = { "tool" }
            };
            ToolTip.SetTip(button, tooltip);
            button.Click += (_, _) => action();
            return button;
        }

        _zoomOutButton = Tool("IconMinus", _services.Localization.T("L.ZoomOut"), () => Active.Viewer.ZoomOut());
        row.Children.Add(_zoomOutButton);
        zoomText = new TextBlock { Text = "100%" };
        zoomText.Classes.Add("dock-zoom");
        row.Children.Add(zoomText);
        _zoomInButton = Tool("IconPlus", _services.Localization.T("L.ZoomIn"), () => Active.Viewer.ZoomIn());
        row.Children.Add(_zoomInButton);
        row.Children.Add(new Border { Classes = { "dock-divider" } });
        _fitButton = Tool("IconFit", _services.Localization.T("L.FitWindow"),
            () => Active.Viewer.SetZoomMode(ViewerZoomMode.FitWindow));
        row.Children.Add(_fitButton);
        _actualButton = new Button
        {
            Content = new TextBlock { Text = "1:1", Classes = { "tool-text" } },
            Classes = { "tool", "wide" }
        };
        ToolTip.SetTip(_actualButton, _services.Localization.T("L.ActualSize"));
        _actualButton.Click += (_, _) => Active.Viewer.SetZoomMode(ViewerZoomMode.Actual);
        row.Children.Add(_actualButton);
        row.Children.Add(new Border { Classes = { "dock-divider" } });

        infoButton = Tool("IconInfo", _services.Localization.T("L.InfoPanel"), () => SetInfoVisible(!_infoVisible));
        row.Children.Add(infoButton);
        filmstripButton = Tool("IconFilmstrip", _services.Localization.T("L.Filmstrip"),
            () => SetFilmstripVisible(!_filmstripVisible));
        row.Children.Add(filmstripButton);
        row.Children.Add(Tool("IconSwap", _services.Localization.T("L.SwapPanes"), SwapPanes));
        row.Children.Add(new Border { Classes = { "dock-divider" } });
        row.Children.Add(Tool("IconClose", _services.Localization.T("L.CloseCompare"),
            () => ExitRequested?.Invoke(this, EventArgs.Empty)));
        dock.Child = row;
        return zoomText;
    }

    private Border BuildFilmstrip(out FilmHalf filmA, out FilmHalf filmB, out Button close)
    {
        var strip = new Border { Classes = { "dual-filmstrip" }, IsVisible = false };
        strip.SetValue(Panel.ZIndexProperty, 30);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        filmA = BuildFilmHalf(0);
        filmB = BuildFilmHalf(1);
        Grid.SetColumn(filmA.Host, 0);
        Grid.SetColumn(filmB.Host, 2);

        var split = new Border { Classes = { "dual-film-split" } };
        Grid.SetColumn(split, 1);

        close = new Button
        {
            Content = new TextBlock { Text = "×", FontSize = 16 },
            Classes = { "dual-film-close" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 11, 0)
        };
        close.SetValue(Panel.ZIndexProperty, 4);
        close.Click += (_, _) => SetFilmstripVisible(false);
        Grid.SetColumnSpan(close, 3);

        grid.Children.Add(filmA.Host);
        grid.Children.Add(split);
        grid.Children.Add(filmB.Host);
        grid.Children.Add(close);
        strip.Child = grid;
        return strip;
    }

    private FilmHalf BuildFilmHalf(int index)
    {
        var host = new Border { Classes = { "dual-film-half", index == 0 ? "a" : "b" } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };

        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border { Classes = { "dual-film-badge", index == 0 ? "a" : "b" } };
        var badgeText = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        badge.Child = badgeText;
        head.Children.Add(badge);

        var folder = new TextBlock
        {
            Classes = { "dual-film-title" },
            MaxWidth = 190,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(folder);
        var count = new TextBlock { Classes = { "dual-film-meta" }, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(count);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 7, 0, 0)
        };
        var thumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        scroll.Content = thumbs;

        Grid.SetRow(scroll, 1);
        Grid.SetRow(head, 0);
        grid.Children.Add(head);
        grid.Children.Add(scroll);
        host.Child = grid;

        return new FilmHalf(index, host, badge, badgeText, folder, count, scroll, thumbs);
    }

    private Border BuildInfo(out StackPanel content, out Button tabA, out Button tabB)
    {
        var info = new Border { Classes = { "info-overlay" }, IsVisible = false };
        info.SetValue(Panel.ZIndexProperty, 45);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 14, 10, 4) };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        tabA = new Button { Content = "A", Classes = { "dual-tab", "a", "active" } };
        tabB = new Button { Content = "B", Classes = { "dual-tab", "b" } };
        tabA.Click += (_, _) => SetActive(0);
        tabB.Click += (_, _) => SetActive(1);
        tabs.Children.Add(tabA);
        tabs.Children.Add(tabB);
        header.Children.Add(tabs);
        var title = new TextBlock
        {
            Text = _services.Localization.T("L.InfoPanel"),
            Classes = { "info-title" },
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = new Button
        {
            Content = new TextBlock { Text = "×", FontSize = 16 },
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#9A9286")),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => SetInfoVisible(false);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        grid.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        content = new StackPanel { Spacing = 0, Margin = new Thickness(16, 4, 16, 14) };
        scroll.Content = content;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        info.Child = grid;
        return info;
    }

    private Border BuildMenuScrim()
    {
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#01000000")),
            IsVisible = false
        };
        scrim.SetValue(Panel.ZIndexProperty, 54);
        scrim.PointerPressed += (_, args) =>
        {
            ClosePaneMenu();
            args.Handled = true;
        };
        return scrim;
    }

    private Border BuildPaneMenu(
        out Border badge,
        out TextBlock badgeText,
        out TextBlock folder,
        out TextBlock meta,
        out Button openFolder,
        out Button openImage,
        out Button reveal,
        out Button copyImage,
        out Button copyPath,
        out Button swap,
        out Button exit)
    {
        var menu = new Border { Classes = { "dual-menu" }, IsVisible = false };
        menu.SetValue(Panel.ZIndexProperty, 55);
        var stack = new StackPanel { Spacing = 1 };

        var head = new Border { Classes = { "dual-menu-head" } };
        var headGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        badge = new Border { Classes = { "dual-menu-badge", "b" } };
        badgeText = new TextBlock { Text = "B", Classes = { "dual-badge-text" } };
        badge.Child = badgeText;
        var headText = new StackPanel { Spacing = 2, Margin = new Thickness(9, 0, 0, 0) };
        folder = new TextBlock { Classes = { "dual-menu-folder" } };
        meta = new TextBlock { Classes = { "dual-menu-meta" } };
        headText.Children.Add(folder);
        headText.Children.Add(meta);
        Grid.SetColumn(headText, 1);
        headGrid.Children.Add(badge);
        headGrid.Children.Add(headText);
        head.Child = headGrid;
        stack.Children.Add(head);

        openFolder = BuildMenuItem("IconFolderPlus", _services.Localization.T("L.OpenFolder"),
            "Ctrl+Shift+O", () => ActivateMenuFolder(), primary: true);
        openImage = BuildMenuItem("IconImage", _services.Localization.T("L.OpenFile"),
            "Ctrl+O", () => ActivateMenuImage());
        reveal = BuildMenuItem("IconFolder", _services.Localization.T("L.ShowInFolder"), null,
            ActivateMenuReveal);
        copyImage = BuildMenuItem("IconImage", _services.Localization.T("L.CopyImage"), "Ctrl+C",
            () => _ = CopyMenuImageAsync());
        copyPath = BuildMenuItem("IconLink", _services.Localization.T("L.CopyPath"), null,
            () => _ = CopyMenuPathAsync());
        swap = BuildMenuItem("IconSwap", _services.Localization.T("L.SwapPanes"), "X",
            () => { ClosePaneMenu(); SwapPanes(); });
        exit = BuildMenuItem("IconClose", _services.Localization.T("L.CloseCompare"), "D",
            () => { ClosePaneMenu(); ExitRequested?.Invoke(this, EventArgs.Empty); });

        stack.Children.Add(openFolder);
        stack.Children.Add(openImage);
        stack.Children.Add(new Border { Classes = { "dual-menu-sep" } });
        stack.Children.Add(reveal);
        stack.Children.Add(copyImage);
        stack.Children.Add(copyPath);
        stack.Children.Add(new Border { Classes = { "dual-menu-sep" } });
        stack.Children.Add(swap);
        stack.Children.Add(exit);
        stack.Children.Add(new TextBlock
        {
            Text = _services.Localization.T("L.ContextMenuNote"),
            Classes = { "dual-menu-note" }
        });
        menu.Child = stack;
        return menu;
    }

    private Button BuildMenuItem(string iconKey, string label, string? shortcut, Action action, bool primary = false)
    {
        var button = new Button { Classes = { "dual-menu-item" } };
        _menuItems.Add(button);
        button.PointerEntered += (_, _) => button.Classes.Set("hover", true);
        button.PointerMoved += (_, _) => button.Classes.Set("hover", true);
        button.PointerExited += (_, _) => button.Classes.Set("hover", false);
        if (primary)
        {
            button.Classes.Add("primary");
        }

        var grid = new Grid
        {
            Width = 242,
            HorizontalAlignment = HorizontalAlignment.Left,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new IconPath
        {
            Data = FindGeometry(iconKey),
            Stroke = primary ? Brushes.White : new SolidColorBrush(Color.Parse("#D8D1C5")),
            StrokeThickness = 1.6,
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = primary ? Brushes.White : new SolidColorBrush(Color.Parse("#F0EBE2"))
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (shortcut is not null)
        {
            var hint = new TextBlock
            {
                Text = shortcut,
                Classes = { "dual-menu-shortcut" },
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.Parse(primary ? "#B7E6DC" : "#ABA49A"))
            };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        button.Content = grid;
        button.Click += (_, _) => action();
        return button;
    }

    private Border BuildPaneNav(int index, out Border badge, out Button previous, out Button next)
    {
        var group = new Border { Classes = { "dual-pane-nav", index == 0 ? "a" : "b" }, IsVisible = false };
        group.SetValue(Panel.ZIndexProperty, 38);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        badge = new Border { Classes = { "dual-pane-nav-badge", index == 0 ? "a" : "b" } };
        badge.Child = new TextBlock
        {
            Text = index == 0 ? "A" : "B",
            Classes = { "dual-badge-text" }
        };
        row.Children.Add(badge);

        previous = CreatePaneNavButton(index, "IconChevronLeft", -1, "L.PreviousImage");
        next = CreatePaneNavButton(index, "IconChevronRight", 1, "L.NextImage");
        row.Children.Add(previous);
        row.Children.Add(next);
        group.Child = row;
        return group;
    }

    private Button CreatePaneNavButton(int index, string icon, int direction, string tooltipKey)
    {
        var button = new Button
        {
            Content = new IconPath
            {
                Data = FindGeometry(icon),
                Stroke = new SolidColorBrush(Color.Parse("#F7F3EA")),
                StrokeThickness = 1.7,
                Width = 16,
                Height = 16
            },
            Classes = { "dual-pane-nav-button", index == 0 ? "a" : "b", direction < 0 ? "prev" : "next" }
        };
        ToolTip.SetTip(button,
            $"{(index == 0 ? "A" : "B")} · {_services.Localization.T(tooltipKey)}");
        button.Click += (_, _) => _ = NavigatePaneAsync(Pane(index), direction);
        return button;
    }

    private static Border CreateThumbBadge(string letter, bool isA) =>
        new()
        {
            Width = 17,
            Height = 15,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse(isA ? "#E3563B" : "#2E6F63")),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            Child = new TextBlock
            {
                Text = letter,
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
                FontSize = 8,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

    private void ApplyPaneHeaderLayout(PaneSlot pane)
    {
        var paneWidth = pane.Host.Bounds.Width;
        if (paneWidth <= 0)
        {
            return;
        }

        var windowWidth = Bounds.Width;
        var compact = windowWidth > 0 && windowWidth < 1120;
        var narrow = windowWidth >= 1120 && windowWidth < 1240;
        var wide = windowWidth >= 1600;
        var rightReserve = pane.PaneNumber == 1 ? 166 : 16;
        var available = Math.Max(160, paneWidth - 16 - rightReserve);
        pane.Header.MaxWidth = available;

        var folderMax = compact ? 120 : available >= 500 ? 190 : 150;
        var fileMax = Math.Min(340, Math.Max(120, available - 44 - folderMax));
        if (compact)
        {
            fileMax = Math.Min(fileMax, 170);
        }

        pane.FolderChip.MaxWidth = folderMax;
        pane.FileChip.MaxWidth = fileMax;
        pane.FileChip.Width = fileMax;

        var folderChars = compact ? 8 : 12;
        var fileChars = wide ? 14 : narrow ? 10 : 8;
        pane.FolderName.MaxWidth = compact ? 60 : 82;
        pane.FileName.MaxWidth = fileChars >= 14 ? 96 : fileChars >= 10 ? 70 : 52;

        var folderText = pane.Folder is null ? string.Empty : Path.GetFileName(pane.Folder);
        var fileText = pane.CurrentPath is null ? string.Empty : Path.GetFileName(pane.CurrentPath);
        pane.FolderName.Text = TruncateLabel(folderText, folderChars);
        pane.FileName.Text = TruncateFileName(fileText, fileChars);
        ToolTip.SetTip(pane.FolderChip, string.IsNullOrEmpty(folderText) ? null : folderText);
        ToolTip.SetTip(pane.FileChip, string.IsNullOrEmpty(fileText) ? null : fileText);

        var showDimensions = !compact && !narrow;
        var showZoom = !compact;
        pane.FileMeta.IsVisible = showDimensions;
        pane.FileSeparator1.IsVisible = showDimensions;
        pane.FileZoom.IsVisible = showZoom;
        pane.FileSeparator2.IsVisible = true;
        pane.FilePosition.IsVisible = true;
        pane.FolderCount.IsVisible = !compact;
    }

    private static string TruncateLabel(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxChars - 1)), "…");
    }

    private static string TruncateFileName(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        var extension = Path.GetExtension(value).TrimStart('.');
        if (extension.Length > 0 && extension.Length + 2 <= maxChars)
        {
            var prefixLength = maxChars - extension.Length - 1;
            var prefix = value[..prefixLength].TrimEnd('.');
            if (prefix.Length > 0)
            {
                return string.Concat(prefix, "…", extension);
            }
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxChars - 1)), "…");
    }

    private void ApplyCompactLayout()
    {
        var compact = Bounds.Width > 0 && Bounds.Width < 1120;
        ApplyToPane(_a);
        ApplyToPane(_b);

        ApplyPaneHeaderLayout(_a);
        ApplyPaneHeaderLayout(_b);

        void ApplyToPane(PaneSlot pane)
        {
            pane.NavGroup.Height = compact ? 34 : 38;
            pane.NavGroup.Padding = new Thickness(compact ? 2 : 3);
            pane.NavGroup.Margin = new Thickness(0, compact ? 58 : 64, 0, 0);
            pane.NavBadge.Width = compact ? 28 : 30;
            pane.NavBadge.Height = compact ? 28 : 30;
            pane.NavPrev.Width = compact ? 28 : 30;
            pane.NavPrev.Height = compact ? 28 : 30;
            pane.NavNext.Width = compact ? 28 : 30;
            pane.NavNext.Height = compact ? 28 : 30;
        }
    }

    private Geometry FindGeometry(string key) =>
        (this.FindResource(key) as Geometry ?? Application.Current?.FindResource(key) as Geometry)
        ?? StreamGeometry.Parse("M0 0");

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB"
    };
}
