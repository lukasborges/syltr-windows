using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Localization;
using Windows.Graphics;

namespace Syltr.Window;

/// <summary>
/// Presents the service catalog in an owned native Windows dialog.
/// </summary>
public sealed class AddServiceWindow : Microsoft.UI.Xaml.Window
{
    private const int OwnerWindowIndex = -8;
    private readonly nint _ownerHandle;
    private readonly TaskCompletionSource<AddServiceWindowResult?> _completion = new();
    private AutoSuggestBox? _searchBox;
    private AddServiceWindowResult? _result;
    private bool _initialFocusSet;

    public AddServiceWindow(Microsoft.UI.Xaml.Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Title = AppText.Get("AddService_Title");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.SetPresenter(OverlappedPresenter.CreateForDialog());
        AppWindow.Resize(new SizeInt32(500, 650));

        _ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetOwner(windowHandle, _ownerHandle);

        var ownerPosition = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var dialogSize = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            ownerPosition.X + Math.Max(0, (ownerSize.Width - dialogSize.Width) / 2),
            ownerPosition.Y + Math.Max(0, (ownerSize.Height - dialogSize.Height) / 2)));

        Content = BuildContent();
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public Task<AddServiceWindowResult?> ShowAsync()
    {
        EnableWindow(_ownerHandle, false);
        Activate();
        return _completion.Task;
    }

    private Grid BuildContent()
    {
        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = AppText.Get("AddService_SearchPlaceholder"),
            QueryIcon = new SymbolIcon(Symbol.Find),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
        };
        _searchBox = searchBox;
        AutomationProperties.SetName(searchBox, AppText.Get("AddService_SearchAutomationName"));
        var customButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Microsoft.UI.Xaml.Thickness(0),
            Content = new SymbolIcon(Symbol.Add),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
        AutomationProperties.SetName(customButton, AppText.Get("AddService_Custom"));
        ToolTipService.SetToolTip(customButton, AppText.Get("AddService_Custom"));
        customButton.Click += (_, _) =>
        {
            _result = new AddServiceWindowResult(null, true);
            Close();
        };

        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
        });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        searchRow.Children.Add(searchBox);
        Grid.SetColumn(customButton, 1);
        searchRow.Children.Add(customButton);

        var catalogPanel = new StackPanel { Spacing = 16 };
        var sections = new List<CatalogWindowSection>();
        foreach (var category in ServiceCatalog.Categories)
        {
            var rows = new StackPanel { Spacing = 2 };
            var section = new StackPanel { Spacing = 6 };
            var heading = new TextBlock
            {
                Text = CategoryDisplayName(category),
                Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            section.Children.Add(heading);
            section.Children.Add(rows);

            var windowRows = new List<CatalogWindowRow>();
            foreach (var entry in ServiceCatalog.Entries.Where(candidate => candidate.Category == category))
            {
                var labels = new StackPanel { Spacing = 1 };
                labels.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                labels.Children.Add(new TextBlock
                {
                    Text = entry.Url,
                    FontSize = 11,
                    Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
                });

                var rowContent = new Grid { ColumnSpacing = 12 };
                rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
                rowContent.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
                });
                rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
                rowContent.Children.Add(new Image
                {
                    Width = 28,
                    Height = 28,
                    Source = new SvgImageSource(new Uri($"ms-appx:///Assets/ServiceIcons/{entry.Key}.svg")),
                    Stretch = Stretch.Uniform
                });
                Grid.SetColumn(labels, 1);
                rowContent.Children.Add(labels);
                var addIcon = new SymbolIcon(Symbol.Add)
                {
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                };
                Grid.SetColumn(addIcon, 2);
                rowContent.Children.Add(addIcon);

                var row = new Button
                {
                    Content = rowContent,
                    Padding = new Microsoft.UI.Xaml.Thickness(10, 8, 10, 8),
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };
                AutomationProperties.SetName(row, AppText.Format("AddService_AddEntry", entry.Name));
                row.Click += (_, _) =>
                {
                    _result = new AddServiceWindowResult(entry, false);
                    Close();
                };
                rows.Children.Add(row);
                windowRows.Add(new CatalogWindowRow(row, $"{entry.Name} {entry.Url}".ToLowerInvariant()));
            }

            catalogPanel.Children.Add(section);
            sections.Add(new CatalogWindowSection(section, windowRows));
        }

        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text.Trim().ToLowerInvariant();
            foreach (var section in sections)
            {
                var anyVisible = false;
                foreach (var row in section.Rows)
                {
                    var visible = query.Length == 0 || row.SearchText.Contains(query, StringComparison.Ordinal);
                    row.Element.Visibility = visible
                        ? Microsoft.UI.Xaml.Visibility.Visible
                        : Microsoft.UI.Xaml.Visibility.Collapsed;
                    anyVisible |= visible;
                }

                section.Element.Visibility = anyVisible
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
            }
        };

        var root = new Grid
        {
            Padding = new Microsoft.UI.Xaml.Thickness(20),
            RowSpacing = 12,
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]
        };
        root.RowDefinitions.Add(new RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
        });
        root.Children.Add(searchRow);
        root.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
        var catalogScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = catalogPanel
        };
        Grid.SetRow(catalogScroll, 1);
        root.Children.Add(catalogScroll);
        return root;
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            EnableWindow(_ownerHandle, false);
            if (!_initialFocusSet)
            {
                _initialFocusSet = true;
                DispatcherQueue.TryEnqueue(() => _searchBox?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic));
            }
        }
    }

    private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosed;
        EnableWindow(_ownerHandle, true);
        SetForegroundWindow(_ownerHandle);
        _completion.TrySetResult(_result);
    }

    private static string CategoryDisplayName(ServiceCatalogCategory category) => category switch
    {
        ServiceCatalogCategory.Messaging => AppText.Get("Category_Messaging"),
        ServiceCatalogCategory.Email => AppText.Get("Category_Email"),
        ServiceCatalogCategory.Calendar => AppText.Get("Category_Calendar"),
        ServiceCatalogCategory.Tasks => AppText.Get("Category_Tasks"),
        ServiceCatalogCategory.AI => AppText.Get("Category_AI"),
        _ => category.ToString()
    };

    private static void SetOwner(nint windowHandle, nint ownerHandle)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(windowHandle, OwnerWindowIndex, ownerHandle);
        }
        else
        {
            SetWindowLong32(windowHandle, OwnerWindowIndex, ownerHandle.ToInt32());
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    private sealed record CatalogWindowRow(Microsoft.UI.Xaml.FrameworkElement Element, string SearchText);

    private sealed record CatalogWindowSection(
        Microsoft.UI.Xaml.FrameworkElement Element,
        IReadOnlyList<CatalogWindowRow> Rows);
}

public sealed record AddServiceWindowResult(ServiceCatalogEntry? Entry, bool CustomRequested);
