using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles GET /api/db — SQLite database overview for maintenance UI.</summary>
public sealed class DbApiModule : MonitoringApiModule
{
    public DbApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/db";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(remainingPath) && remainingPath != "/")
        {
            return false;
        }

        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var store = Runtime.DataStore;
        var teachFiles = store.ListTeachFiles();
        var teachPointCount = teachFiles.Sum(f =>
            store.GetTeachFileSnapshot(f.PlatformId, f.Name)?.Points.Count ?? 0);

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            databasePath = store.DatabasePath,
            activeRecipeId = Runtime.RecipeManager.ActiveRecipeId,
            counts = new
            {
                orders = store.ListOrders().Count,
                recipes = store.ListRecipes().Count,
                teachFiles = teachFiles.Count,
                teachPoints = teachPointCount,
            },
            timestampUtc = DateTime.UtcNow,
        }, SnapshotJsonOptions);

        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
