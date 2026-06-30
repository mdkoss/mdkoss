using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles GET/POST /api/recipe/* — list recipes, apply, and capture from runtime.
/// </summary>
public sealed class RecipeApiModule : MonitoringApiModule
{
    public RecipeApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/recipe";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var path = remainingPath.Trim('/');
        var method = context.Request.HttpMethod ?? string.Empty;

        if (string.IsNullOrEmpty(path))
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteRecipeListAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        if (string.Equals(path, "apply", StringComparison.OrdinalIgnoreCase)
            && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleApplyAsync(context, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(path, "capture", StringComparison.OrdinalIgnoreCase)
            && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCaptureAsync(context, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            await HandleGetByIdAsync(context.Response, path, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task WriteRecipeListAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var snap = Runtime.GetRecipeSnapshot();
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            activeRecipeId = snap.ActiveRecipeId,
            recipeVarKeys = snap.RecipeVarKeys,
            recipes = snap.Recipes.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                description = r.Description
            })
        }, SnapshotJsonOptions);

        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleGetByIdAsync(
        HttpListenerResponse response,
        string id,
        CancellationToken cancellationToken)
    {
        if (!Runtime.RecipeManager.TryGetRecipe(id, out var recipe, out var error) || recipe is null)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(response, error ?? "recipe_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            id = recipe.Id,
            name = recipe.Name,
            description = recipe.Description,
            vars = recipe.Vars
        }, SnapshotJsonOptions);

        await WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleApplyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var id = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var req = Deserialize<RecipeApplyRequest>(body);
                    id = req?.Id;
                }
                catch (JsonException)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context.Response, "recipe_id_required", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!Runtime.TryApplyRecipe(id.Trim(), out var error))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context.Response, error ?? "apply_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        var activeName = Runtime.Vars.Get<string>(MdkRecipeManager.ActiveNameVarKey) ?? string.Empty;
        var payload = JsonSerializer.Serialize(new
        {
            success = true,
            activeRecipeId = Runtime.RecipeManager.ActiveRecipeId,
            activeRecipeName = activeName,
            timestampUtc = DateTime.UtcNow
        }, SnapshotJsonOptions);

        await WriteResponseAsync(context.Response, "application/json; charset=utf-8", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task HandleCaptureAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Url?.Query ?? string.Empty;
        var id = GetQueryValue(query, "id");
        var name = GetQueryValue(query, "name");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var req = Deserialize<RecipeCaptureRequest>(body);
                    id ??= req?.Id;
                    name ??= req?.Name;
                }
                catch (JsonException)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context.Response, "recipe_id_and_name_required", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!Runtime.RecipeManager.TryCaptureFromRuntime(id.Trim(), name.Trim(), out var error))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context.Response, error ?? "capture_failed", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteSuccessAsync(context.Response, "capture", cancellationToken).ConfigureAwait(false);
    }

    private static string? GetQueryValue(string query, string name)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq >= 0 ? part[..eq] : part;
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : string.Empty;
        }

        return null;
    }

    private sealed class RecipeApplyRequest
    {
        public string? Id { get; set; }
    }

    private sealed class RecipeCaptureRequest
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
}
