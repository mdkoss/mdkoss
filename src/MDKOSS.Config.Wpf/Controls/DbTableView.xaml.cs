using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MDKOSS.Config.Wpf.Controls;

/// <summary>
/// Database table browser: column headers match the selected SQLite table schema.
/// </summary>
public partial class DbTableView : UserControl
{
    public static readonly DependencyProperty TableNameProperty =
        DependencyProperty.Register(
            nameof(TableName),
            typeof(string),
            typeof(DbTableView),
            new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty PrimaryKeyProperty =
        DependencyProperty.Register(
            nameof(PrimaryKey),
            typeof(string),
            typeof(DbTableView),
            new PropertyMetadata(null, OnSchemaChanged));

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(
            nameof(Columns),
            typeof(IEnumerable),
            typeof(DbTableView),
            new PropertyMetadata(null, OnColumnsChanged));

    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(
            nameof(Rows),
            typeof(IEnumerable),
            typeof(DbTableView),
            new PropertyMetadata(null, OnRowsChanged));

    public static readonly DependencyProperty SelectedRowProperty =
        DependencyProperty.Register(
            nameof(SelectedRow),
            typeof(DbRowItem),
            typeof(DbTableView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedRowChanged));

    private static readonly DbCellValueConverter CellConverter = new();
    private bool _suppressSelection;
    private INotifyCollectionChanged? _columnsNotify;
    private INotifyCollectionChanged? _rowsNotify;

    public DbTableView()
    {
        InitializeComponent();
        UpdateChrome();
    }

    public string? TableName
    {
        get => (string?)GetValue(TableNameProperty);
        set => SetValue(TableNameProperty, value);
    }

    public string? PrimaryKey
    {
        get => (string?)GetValue(PrimaryKeyProperty);
        set => SetValue(PrimaryKeyProperty, value);
    }

    public IEnumerable? Columns
    {
        get => (IEnumerable?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IEnumerable? Rows
    {
        get => (IEnumerable?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public DbRowItem? SelectedRow
    {
        get => (DbRowItem?)GetValue(SelectedRowProperty);
        set => SetValue(SelectedRowProperty, value);
    }

    public event EventHandler<DbRowItem?>? RowSelectionChanged;
    public event EventHandler? RowActivated;

    private static void OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DbTableView view)
        {
            view.UpdateChrome();
            view.UpdateEmptyHint();
        }
    }

    private static void OnSchemaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DbTableView view)
        {
            view.RebuildColumns();
            view.UpdateChrome();
        }
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DbTableView view)
        {
            return;
        }

        view.DetachColumnsNotify(e.OldValue as INotifyCollectionChanged);
        view.AttachColumnsNotify(e.NewValue as INotifyCollectionChanged);
        view.RebuildColumns();
        view.UpdateChrome();
    }

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DbTableView view)
        {
            return;
        }

        view.DetachRowsNotify(e.OldValue as INotifyCollectionChanged);
        view.AttachRowsNotify(e.NewValue as INotifyCollectionChanged);
        view.TableGrid.ItemsSource = e.NewValue as IEnumerable;
        view.UpdateEmptyHint();
        view.UpdateChrome();
        view.SyncSelectionFromProperty();
    }

    private static void OnSelectedRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DbTableView view)
        {
            view.SyncSelectionFromProperty();
        }
    }

    private void AttachColumnsNotify(INotifyCollectionChanged? notify)
    {
        _columnsNotify = notify;
        if (_columnsNotify is not null)
        {
            _columnsNotify.CollectionChanged += Columns_CollectionChanged;
        }
    }

    private void DetachColumnsNotify(INotifyCollectionChanged? notify)
    {
        if (notify is not null)
        {
            notify.CollectionChanged -= Columns_CollectionChanged;
        }

        if (ReferenceEquals(_columnsNotify, notify))
        {
            _columnsNotify = null;
        }
    }

    private void AttachRowsNotify(INotifyCollectionChanged? notify)
    {
        _rowsNotify = notify;
        if (_rowsNotify is not null)
        {
            _rowsNotify.CollectionChanged += Rows_CollectionChanged;
        }
    }

    private void DetachRowsNotify(INotifyCollectionChanged? notify)
    {
        if (notify is not null)
        {
            notify.CollectionChanged -= Rows_CollectionChanged;
        }

        if (ReferenceEquals(_rowsNotify, notify))
        {
            _rowsNotify = null;
        }
    }

    private void Columns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildColumns();
        UpdateChrome();
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyHint();
        UpdateChrome();
    }

    private void RebuildColumns()
    {
        var names = ResolveColumnNames();
        TableGrid.Columns.Clear();

        if (names.Count == 0)
        {
            UpdateEmptyHint();
            return;
        }

        var pk = PrimaryKey;
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var isPk = !string.IsNullOrWhiteSpace(pk)
                       && string.Equals(name, pk, StringComparison.OrdinalIgnoreCase);
            var width = EstimateWidth(name, isPk, i == names.Count - 1);

            var col = new DataGridTextColumn
            {
                Header = BuildHeader(name, isPk),
                Binding = new Binding(".")
                {
                    Mode = BindingMode.OneWay,
                    Converter = CellConverter,
                    ConverterParameter = name,
                    FallbackValue = "",
                    TargetNullValue = "",
                },
                Width = width,
                MinWidth = 56,
            };

            if (isPk)
            {
                col.ElementStyle = CreatePkCellStyle();
            }

            TableGrid.Columns.Add(col);
        }

        UpdateEmptyHint();
    }

    private static object BuildHeader(string name, bool isPk)
    {
        if (!isPk)
        {
            return name;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = " PK",
            FontSize = 10,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x6D, 0x76)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        });
        return panel;
    }

    private static Style CreatePkCellStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas, Cascadia Mono, Courier New")));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        return style;
    }

    private static DataGridLength EstimateWidth(string columnName, bool isPk, bool isLast)
    {
        if (isLast && !isPk)
        {
            return new DataGridLength(1.4, DataGridLengthUnitType.Star);
        }

        var lower = columnName.ToLowerInvariant();
        if (isPk || lower is "id" or "key" or "enabled" or "locale")
        {
            return new DataGridLength(110);
        }

        if (lower.Contains("json", StringComparison.Ordinal)
            || lower.Contains("param", StringComparison.Ordinal)
            || lower.Contains("description", StringComparison.Ordinal)
            || lower.Contains("message", StringComparison.Ordinal)
            || lower.Contains("value", StringComparison.Ordinal))
        {
            return new DataGridLength(2.0, DataGridLengthUnitType.Star);
        }

        return new DataGridLength(1.0, DataGridLengthUnitType.Star);
    }

    private List<string> ResolveColumnNames()
    {
        var list = new List<string>();
        if (Columns is null)
        {
            return list;
        }

        foreach (var item in Columns)
        {
            if (item is string s && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(s);
            }
        }

        return list;
    }

    private int CountRows()
    {
        if (Rows is ICollection collection)
        {
            return collection.Count;
        }

        if (Rows is null)
        {
            return 0;
        }

        var n = 0;
        foreach (var _ in Rows)
        {
            n++;
        }

        return n;
    }

    private void UpdateChrome()
    {
        var name = string.IsNullOrWhiteSpace(TableName) ? "—" : TableName!;
        TableNameText.Text = name;

        var cols = ResolveColumnNames();
        if (cols.Count == 0)
        {
            ColumnSummaryText.Text = "";
        }
        else
        {
            var pkNote = string.IsNullOrWhiteSpace(PrimaryKey) ? "" : $" · PK={PrimaryKey}";
            ColumnSummaryText.Text = $"· {cols.Count} 列{pkNote}";
        }

        var n = CountRows();
        RowCountText.Text = $"{n} 行";
    }

    private void UpdateEmptyHint()
    {
        var empty = CountRows() == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = string.IsNullOrWhiteSpace(TableName)
            ? "选择左侧表以浏览行"
            : $"表 {TableName} 暂无数据";
    }

    private void SyncSelectionFromProperty()
    {
        if (_suppressSelection)
        {
            return;
        }

        _suppressSelection = true;
        TableGrid.SelectedItem = SelectedRow;
        if (SelectedRow is not null)
        {
            try
            {
                TableGrid.ScrollIntoView(SelectedRow);
            }
            catch
            {
                // ignore if row not yet realized
            }
        }

        _suppressSelection = false;
    }

    private void TableGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }

        var row = TableGrid.SelectedItem as DbRowItem;
        _suppressSelection = true;
        SelectedRow = row;
        _suppressSelection = false;
        RowSelectionChanged?.Invoke(this, row);
    }

    private void TableGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TableGrid.SelectedItem is DbRowItem)
        {
            RowActivated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Programmatically select a row without raising loops.</summary>
    public void SelectRow(DbRowItem? row)
    {
        SelectedRow = row;
        SyncSelectionFromProperty();
    }
}

/// <summary>Reads <see cref="DbRowItem"/> cell by column name (ConverterParameter).</summary>
internal sealed class DbCellValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DbRowItem row && parameter is string column)
        {
            return row.GetCell(column);
        }

        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
