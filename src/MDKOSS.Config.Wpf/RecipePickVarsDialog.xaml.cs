using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace MDKOSS.Config.Wpf;

/// <summary>Multi-select recipe var keys from all Vars and all SysConfig parameters.</summary>
public partial class RecipePickVarsDialog : Window
{
    private readonly ObservableCollection<RecipeVarCandidateRow> _rows;
    private readonly ICollectionView _view;

    public RecipePickVarsDialog(IEnumerable<RecipeVarCandidate> candidates, IEnumerable<string> alreadySelected)
    {
        InitializeComponent();
        var selected = new HashSet<string>(
            alreadySelected.Where(k => !string.IsNullOrWhiteSpace(k)),
            StringComparer.OrdinalIgnoreCase);

        _rows = new ObservableCollection<RecipeVarCandidateRow>(
            candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(c => new RecipeVarCandidateRow
                {
                    Key = c.Key.Trim(),
                    Source = c.Source,
                    ValuePreview = c.ValuePreview,
                    IsSelected = selected.Contains(c.Key),
                }));

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        CandidateList.ItemsSource = _view;
    }

    /// <summary>Keys checked when the dialog closes with OK.</summary>
    public IReadOnlyList<string> SelectedKeys { get; private set; } = [];

    private bool FilterRow(object obj)
    {
        if (obj is not RecipeVarCandidateRow row)
        {
            return false;
        }

        var q = FilterBox.Text?.Trim() ?? "";
        if (q.Length == 0)
        {
            return true;
        }

        return row.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.Source.Contains(q, StringComparison.OrdinalIgnoreCase)
               || row.ValuePreview.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        _view.Refresh();

    private void SelectVisible_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            if (FilterRow(row))
            {
                row.IsSelected = true;
            }
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedKeys = _rows
            .Where(r => r.IsSelected)
            .Select(r => r.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class RecipeVarCandidate
{
    public required string Key { get; init; }
    public required string Source { get; init; }
    public string ValuePreview { get; init; } = "";
}

internal sealed class RecipeVarCandidateRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Key { get; init; } = "";
    public string Source { get; init; } = "";
    public string ValuePreview { get; init; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
