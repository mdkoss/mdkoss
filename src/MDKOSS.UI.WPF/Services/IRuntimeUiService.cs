using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.UI.WPF.Services;

public interface IRuntimeUiService
{
    MdkRuntime Runtime { get; }

    event EventHandler? SnapshotChanged;

    RuntimeSnapshot? LatestSnapshot { get; }

    IReadOnlyList<ProductionOrderRecord> ListOrders();

    IReadOnlyList<TaskSnapshot> ListTasks();

    IReadOnlyList<MdkSetting.AlarmConfig> ListActiveAlarms();

    RecipeSnapshot GetRecipeSnapshot();

    string? SelectedOrderId { get; set; }

    void SendMachineCommand(string command);

    bool TryApplyRecipe(string recipeId, out string? error);

    bool TryTriggerDemoAlarm(out string? error);

    void ClearAllAlarms();

    int AckAllAlarms();

    bool TryClearAlarm(string id, out string? error);

    bool TryWriteIo(string deviceId, string alias, bool value, out string? error);

    bool TryAxisJog(string axisId, double direction, double velocity, out string? error);

    bool TryAxisMove(string axisId, double position, out string? error);

    bool TryAxisEnable(string axisId, bool enabled, out string? error);

    bool TryAxisStop(string axisId, out string? error);

    bool TryPlatformEnable(string platformId, bool enabled, out string? error);

    bool TryPlatformAxisJog(string platformId, string letter, double direction, double velocity, out string? error);

    bool TryPlatformAxisMove(string platformId, string letter, double position, out string? error);

    DeviceActionResult ExecuteAction(string deviceId, string action, Dictionary<string, object?>? parameters = null);

    bool TryReadDriver(string driverId, string address, out object? value, out string? error);

    bool TryWriteDriver(string driverId, string address, object? value, out string? error);

    bool TrySaveSetting(out string? error);

    void Refresh();
}
