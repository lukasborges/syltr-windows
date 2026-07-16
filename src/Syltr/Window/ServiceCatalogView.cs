using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Syltr.Catalog;
using Syltr.Localization;

namespace Syltr.Window;

/// <summary>
/// Builds and filters the service catalog content independently of its native window.
/// </summary>
internal sealed class ServiceCatalogView : Grid
{
    private readonly AutoSuggestBox _searchBox;
    private readonly List<CatalogSection> _sections = [];

    public ServiceCatalogView()
    {
        _searchBox = CreateSearchBox();
        BuildLayout(CreateSearchRow(), CreateCatalogPanel());
        KeyDown += OnKeyDown;
    }

    public event EventHandler<AddServiceWindowResult>? Completed;

    public event EventHandler? CancelRequested;

    public void FocusSearch() => _searchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

    private AutoSuggestBox CreateSearchBox()
    {
        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = AppText.Get("AddService_SearchPlaceholder"),
            QueryIcon = new SymbolIcon(Symbol.Find),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(searchBox, AppText.Get("AddService_SearchAutomationName"));
        searchBox.TextChanged += (_, _) => ApplyFilter(searchBox.Text);
        return searchBox;
    }

    private Grid CreateSearchRow()
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        row.Children.Add(_searchBox);

        var customButton = CreateCustomServiceButton();
        Grid.SetColumn(customButton, 1);
        row.Children.Add(customButton);
        return row;
    }

    private Button CreateCustomServiceButton()
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Microsoft.UI.Xaml.Thickness(0),
            Content = new SymbolIcon(Symbol.Add),
            Background = TransparentBrush(),
            BorderBrush = TransparentBrush(),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, AppText.Get("AddService_Custom"));
        ToolTipService.SetToolTip(button, AppText.Get("AddService_Custom"));
        button.Click += (_, _) => Completed?.Invoke(this, new AddServiceWindowResult(null, true));
        return button;
    }

    private StackPanel CreateCatalogPanel()
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (var category in ServiceCatalog.Categories)
        {
            var section = CreateSection(category);
            panel.Children.Add(section.Element);
            _sections.Add(section);
        }

        return panel;
    }

    private CatalogSection CreateSection(ServiceCatalogCategory category)
    {
        var rowsPanel = new StackPanel { Spacing = 2 };
        var sectionPanel = new StackPanel { Spacing = 6 };
        sectionPanel.Children.Add(CreateSectionHeading(category));
        sectionPanel.Children.Add(rowsPanel);

        var rows = ServiceCatalog.Entries
            .Where(entry => entry.Category == category)
            .Select(entry => CreateEntryRow(entry, rowsPanel))
            .ToArray();
        return new CatalogSection(sectionPanel, rows);
    }

    private static TextBlock CreateSectionHeading(ServiceCatalogCategory category)
    {
        var heading = new TextBlock
        {
            Text = CategoryDisplayName(category),
            Margin = new Microsoft.UI.Xaml.Thickness(4, 0, 0, 0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = SecondaryTextBrush()
        };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private CatalogRow CreateEntryRow(ServiceCatalogEntry entry, StackPanel rowsPanel)
    {
        var button = new Button
        {
            Content = CreateEntryContent(entry),
            Padding = new Microsoft.UI.Xaml.Thickness(10, 8, 10, 8),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            Background = TransparentBrush(),
            BorderBrush = TransparentBrush()
        };
        AutomationProperties.SetName(button, AppText.Format("AddService_AddEntry", entry.Name));
        button.Click += (_, _) => Completed?.Invoke(this, new AddServiceWindowResult(entry, false));
        rowsPanel.Children.Add(button);
        return new CatalogRow(button, $"{entry.Name} {entry.Url}".ToLowerInvariant());
    }

    private static Grid CreateEntryContent(ServiceCatalogEntry entry)
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
        });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        content.Children.Add(CreateEntryIcon(entry));

        var labels = CreateEntryLabels(entry);
        Grid.SetColumn(labels, 1);
        content.Children.Add(labels);

        var addIcon = new SymbolIcon(Symbol.Add)
        {
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
        Grid.SetColumn(addIcon, 2);
        content.Children.Add(addIcon);
        return content;
    }

    private static Image CreateEntryIcon(ServiceCatalogEntry entry) => new()
    {
        Width = 28,
        Height = 28,
        Source = new SvgImageSource(new Uri($"ms-appx:///Assets/ServiceIcons/{entry.Key}.svg")),
        Stretch = Stretch.Uniform
    };

    private static StackPanel CreateEntryLabels(ServiceCatalogEntry entry)
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
            Foreground = SecondaryTextBrush(),
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
        });
        return labels;
    }

    private void BuildLayout(Grid searchRow, StackPanel catalogPanel)
    {
        Padding = new Microsoft.UI.Xaml.Thickness(20);
        RowSpacing = 12;
        Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        RowDefinitions.Add(new RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        RowDefinitions.Add(new RowDefinition
        {
            Height = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star)
        });
        Children.Add(searchRow);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = catalogPanel
        };
        Grid.SetRow(scrollViewer, 1);
        Children.Add(scrollViewer);
    }

    private void ApplyFilter(string searchText)
    {
        var query = searchText.Trim().ToLowerInvariant();
        foreach (var section in _sections)
        {
            var visibleRows = section.Rows.Where(row => row.Matches(query)).ToArray();
            foreach (var row in section.Rows)
            {
                row.Element.Visibility = visibleRows.Contains(row)
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
            }

            section.Element.Visibility = visibleRows.Length > 0
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        args.Handled = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
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

    private static SolidColorBrush TransparentBrush() => new(Microsoft.UI.Colors.Transparent);

    private static Brush SecondaryTextBrush() =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];

    private sealed record CatalogRow(Microsoft.UI.Xaml.FrameworkElement Element, string SearchText)
    {
        public bool Matches(string query) =>
            query.Length == 0 || SearchText.Contains(query, StringComparison.Ordinal);
    }

    private sealed record CatalogSection(
        Microsoft.UI.Xaml.FrameworkElement Element,
        IReadOnlyList<CatalogRow> Rows);
}
