using System.Collections.ObjectModel;
using System.Windows;
using MDKOSS.Core.Data;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;

namespace MDKOSS.UI.WPF.ViewModels;

public sealed class HomeViewModel : BindableBase, IDisposable
{
    private readonly IRuntimeUiService _runtime;
    private readonly IDialogService _dialogs;
    private string _orderListMeta = "—";
    private OrderRow? _selectedOrder;

    public HomeViewModel(IRuntimeUiService runtime, IDialogService dialogs)
    {
        _runtime = runtime;
        _dialogs = dialogs;
        OpenOrderCommand = new DelegateCommand<OrderRow?>(OpenOrder, o => o is not null);
        _runtime.SnapshotChanged += OnChanged;
        ReloadOrders();
    }

    public ObservableCollection<OrderRow> Orders { get; } = [];

    public string OrderListMeta
    {
        get => _orderListMeta;
        private set => SetProperty(ref _orderListMeta, value);
    }

    public OrderRow? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (!SetProperty(ref _selectedOrder, value) || value is null)
            {
                return;
            }

            _runtime.SelectedOrderId = value.Id;
            foreach (var row in Orders)
            {
                row.IsSelected = string.Equals(row.Id, value.Id, StringComparison.OrdinalIgnoreCase);
            }

            OrderListMeta = Orders.Count == 0
                ? "0 条"
                : $"{Orders.Count} 条 · 选中 {value.Id}";
        }
    }

    public DelegateCommand<OrderRow?> OpenOrderCommand { get; }

    private void OpenOrder(OrderRow? row)
    {
        if (row is null)
        {
            return;
        }

        _runtime.SelectedOrderId = row.Id;
        _dialogs.ShowDialog(
            DialogNames.Order,
            new DialogParameters
            {
                { "title", "工单详情" },
                { "orderId", row.Id },
            },
            _ => _runtime.Refresh());
    }

    private void OnChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(ReloadOrders);

    private void ReloadOrders()
    {
        var selectedId = _runtime.SelectedOrderId;
        var rows = _runtime.ListOrders().Select(ToRow).ToList();
        if (rows.Count > 0 &&
            (string.IsNullOrWhiteSpace(selectedId) ||
             rows.All(r => !string.Equals(r.Id, selectedId, StringComparison.OrdinalIgnoreCase))))
        {
            selectedId = rows[0].Id;
            _runtime.SelectedOrderId = selectedId;
        }

        Orders.Clear();
        foreach (var row in rows)
        {
            row.IsSelected = string.Equals(row.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            Orders.Add(row);
        }

        SelectedOrder = Orders.FirstOrDefault(r => r.IsSelected);
        OrderListMeta = Orders.Count == 0
            ? "0 条"
            : $"{Orders.Count} 条 · 选中 {selectedId}";
    }

    private static OrderRow ToRow(ProductionOrderRecord o)
    {
        var status = (o.Status ?? "pending").ToLowerInvariant();
        var label = status switch
        {
            "running" => "运行中",
            "pending" => "等待",
            "done" => "完成",
            "fault" or "error" => "故障",
            _ => o.Status ?? "pending",
        };
        return new OrderRow
        {
            Id = o.Id,
            Product = string.IsNullOrWhiteSpace(o.Product) ? "—" : o.Product,
            Qty = o.Qty,
            Status = status,
            StatusLabel = label,
            Progress = Math.Clamp(o.Progress, 0, 100),
            UpdatedAt = SnapshotReader.FormatUtc(o.UpdatedAtUtc),
            RecipeId = o.RecipeId,
            Notes = o.Notes,
        };
    }

    public void Dispose() => _runtime.SnapshotChanged -= OnChanged;
}
