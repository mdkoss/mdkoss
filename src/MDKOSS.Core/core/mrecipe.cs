namespace MDKOSS.Core;

/// <summary>
/// Manages named recipes: each recipe is a parameter group selected from vars, SysConfig,
/// task parameters, device/platform parameters, etc. Applying a recipe writes values into
/// <see cref="MVarStore"/> and matching setting parameter bags.
/// </summary>
public sealed class MdkRecipeManager
{
    public const string ActiveIdVarKey = "recipe.activeId";
    public const string ActiveNameVarKey = "recipe.activeName";

    public const string TaskKeyPrefix = "task.";
    public const string PlatformKeyPrefix = "platform.";
    public const string DeviceKeyPrefix = "device.";
    public const string AxisKeyPrefix = "axis.";

    private readonly MdkSetting _setting;
    private readonly MVarStore _vars;

    public MdkRecipeManager(MdkSetting setting, MVarStore vars)
    {
        _setting = setting;
        _vars = vars;
    }

    /// <summary>Currently applied recipe id, if any.</summary>
    public string? ActiveRecipeId { get; private set; }

    /// <summary>Keys from <see cref="MdkSetting.Vars"/> that belong to recipes.</summary>
    public IReadOnlyList<string> RecipeVarKeys => ResolveRecipeVarKeys();

    /// <summary>All configured recipes.</summary>
    public IReadOnlyList<MdkSetting.RecipeConfig> Recipes =>
        _setting.Recipes;

    /// <summary>Applies <see cref="MdkSetting.ActiveRecipeId"/> after base vars are seeded.</summary>
    public void BootstrapActiveRecipe()
    {
        ActiveRecipeId = null;
        _vars.Set(ActiveIdVarKey, string.Empty);
        _vars.Set(ActiveNameVarKey, string.Empty);

        if (string.IsNullOrWhiteSpace(_setting.ActiveRecipeId))
        {
            return;
        }

        if (!TryApplyRecipe(_setting.ActiveRecipeId.Trim(), out var error))
        {
            AppLog.Warn($"Active recipe '{_setting.ActiveRecipeId}' was not applied: {error}");
        }
    }

    /// <summary>Finds a recipe by id.</summary>
    public bool TryGetRecipe(string id, out MdkSetting.RecipeConfig? recipe, out string? error)
    {
        recipe = null;
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "recipe_id_required";
            return false;
        }

        recipe = _setting.Recipes.FirstOrDefault(
            r => string.Equals(r.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        if (recipe is null)
        {
            error = "recipe_not_found";
            return false;
        }

        return true;
    }

    /// <summary>Writes recipe-scoped vars into the runtime store and marks the recipe active.</summary>
    public bool TryApplyRecipe(string id, out string? error)
    {
        error = null;
        if (!TryGetRecipe(id, out var recipe, out error) || recipe is null)
        {
            return false;
        }

        if (!ValidateRecipeVars(recipe.Vars, out error))
        {
            return false;
        }

        ApplyRecipeVars(recipe);
        ActiveRecipeId = recipe.Id;
        _setting.ActiveRecipeId = recipe.Id;
        _vars.Set(ActiveIdVarKey, recipe.Id);
        _vars.Set(ActiveNameVarKey, recipe.Name);
        AppLog.Info($"Recipe applied: {recipe.Id} ({recipe.Name}).");
        return true;
    }

    /// <summary>Adds a new recipe. Fails when the id already exists.</summary>
    public bool TryAddRecipe(MdkSetting.RecipeConfig recipe, out string? error)
    {
        error = null;
        if (!ValidateRecipeDefinition(recipe, out error))
        {
            return false;
        }

        if (_setting.Recipes.Any(r => string.Equals(r.Id, recipe.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = "recipe_duplicate_id";
            return false;
        }

        _setting.Recipes.Add(CloneRecipe(recipe));
        return true;
    }

    /// <summary>Updates an existing recipe by id.</summary>
    public bool TryUpdateRecipe(MdkSetting.RecipeConfig recipe, out string? error)
    {
        error = null;
        if (!ValidateRecipeDefinition(recipe, out error))
        {
            return false;
        }

        var index = _setting.Recipes.FindIndex(
            r => string.Equals(r.Id, recipe.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            error = "recipe_not_found";
            return false;
        }

        _setting.Recipes[index] = CloneRecipe(recipe);
        if (string.Equals(ActiveRecipeId, recipe.Id, StringComparison.OrdinalIgnoreCase))
        {
            ApplyRecipeVars(recipe);
            _vars.Set(ActiveNameVarKey, recipe.Name);
        }

        return true;
    }

    /// <summary>Removes a recipe. Clears active recipe when it is deleted.</summary>
    public bool TryDeleteRecipe(string id, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "recipe_id_required";
            return false;
        }

        var index = _setting.Recipes.FindIndex(
            r => string.Equals(r.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            error = "recipe_not_found";
            return false;
        }

        _setting.Recipes.RemoveAt(index);
        if (string.Equals(ActiveRecipeId, id.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ActiveRecipeId = null;
            _setting.ActiveRecipeId = null;
            _vars.Set(ActiveIdVarKey, string.Empty);
            _vars.Set(ActiveNameVarKey, string.Empty);
            RestoreBaseRecipeVars();
        }

        return true;
    }

    /// <summary>Captures current runtime values for recipe keys into a new or existing recipe.</summary>
    public bool TryCaptureFromRuntime(string id, string name, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "recipe_id_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "recipe_name_required";
            return false;
        }

        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in RecipeVarKeys)
        {
            if (_vars.TryGet<object>(key, out var value))
            {
                vars[key] = value;
            }
            else if (_setting.Vars.TryGetValue(key, out var baseValue))
            {
                vars[key] = baseValue;
            }
        }

        var recipe = new MdkSetting.RecipeConfig
        {
            Id = id.Trim(),
            Name = name.Trim(),
            Vars = vars
        };

        if (TryGetRecipe(recipe.Id, out _, out _))
        {
            return TryUpdateRecipe(recipe, out error);
        }

        return TryAddRecipe(recipe, out error);
    }

    /// <summary>Returns a snapshot for monitoring and UI.</summary>
    public RecipeSnapshot GetSnapshot()
    {
        return new RecipeSnapshot(
            ActiveRecipeId,
            RecipeVarKeys,
            _setting.Recipes.Select(r => new RecipeSummary(r.Id, r.Name, r.Description)).ToList());
    }

    private void ApplyRecipeVars(MdkSetting.RecipeConfig recipe)
    {
        // Push every key stored on the recipe (配置端「推送所有」写入的键也要生效).
        foreach (var kv in recipe.Vars)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var key = kv.Key.Trim();
            _vars.Set(key, kv.Value);
            TryApplyStructuredParameter(key, kv.Value);
        }

        // Declared recipe keys missing from this recipe fall back to base setting vars.
        foreach (var key in RecipeVarKeys)
        {
            if (recipe.Vars.ContainsKey(key))
            {
                continue;
            }

            if (_setting.Vars.TryGetValue(key, out var baseValue))
            {
                _vars.Set(key, baseValue);
                TryApplyStructuredParameter(key, baseValue);
            }
        }
    }

    private void RestoreBaseRecipeVars()
    {
        foreach (var key in RecipeVarKeys)
        {
            if (_setting.Vars.TryGetValue(key, out var baseValue))
            {
                _vars.Set(key, baseValue);
                TryApplyStructuredParameter(key, baseValue);
            }
        }
    }

    /// <summary>
    /// Writes structured keys (<c>task.*</c> / <c>platform.*</c> / <c>device.*</c> / <c>axis.*</c>)
    /// into the matching setting parameter dictionary so runtime config stays in sync with vars.
    /// </summary>
    private void TryApplyStructuredParameter(string key, object? value)
    {
        if (!TrySplitOwnerParamKey(key, out var kind, out var ownerId, out var paramKey))
        {
            return;
        }

        var text = FormatParameterValue(value);
        Dictionary<string, string>? bag = kind switch
        {
            "task" => _setting.Tasks
                .FirstOrDefault(t => string.Equals(t.Name, ownerId, StringComparison.OrdinalIgnoreCase))
                ?.Parameters,
            "platform" => _setting.Platforms
                .FirstOrDefault(d => string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase))
                ?.Parameters,
            "device" => _setting.Devices
                .FirstOrDefault(d => string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase))
                ?.Parameters,
            "axis" => _setting.Axes
                .FirstOrDefault(d => string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase))
                ?.Parameters,
            _ => null,
        };

        if (bag is null)
        {
            return;
        }

        bag[paramKey] = text;
    }

    /// <summary>
    /// Parses <c>{kind}.{owner}.{param…}</c> where kind is task/platform/device/axis.
    /// Param may contain dots (e.g. <c>task.bond-cycle.dwellTicks</c>).
    /// </summary>
    public static bool TrySplitOwnerParamKey(
        string key,
        out string kind,
        out string ownerId,
        out string paramKey)
    {
        kind = "";
        ownerId = "";
        paramKey = "";
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();
        string? prefix = null;
        if (trimmed.StartsWith(TaskKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = TaskKeyPrefix;
            kind = "task";
        }
        else if (trimmed.StartsWith(PlatformKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = PlatformKeyPrefix;
            kind = "platform";
        }
        else if (trimmed.StartsWith(DeviceKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = DeviceKeyPrefix;
            kind = "device";
        }
        else if (trimmed.StartsWith(AxisKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            prefix = AxisKeyPrefix;
            kind = "axis";
        }

        if (prefix is null)
        {
            return false;
        }

        var rest = trimmed[prefix.Length..];
        var dot = rest.IndexOf('.');
        if (dot <= 0 || dot >= rest.Length - 1)
        {
            return false;
        }

        ownerId = rest[..dot].Trim();
        paramKey = rest[(dot + 1)..].Trim();
        return ownerId.Length > 0 && paramKey.Length > 0;
    }

    private static string FormatParameterValue(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private IReadOnlyList<string> ResolveRecipeVarKeys()
    {
        if (_setting.RecipeVarKeys.Count > 0)
        {
            return _setting.RecipeVarKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return _setting.Recipes
            .SelectMany(r => r.Vars.Keys)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool ValidateRecipeDefinition(MdkSetting.RecipeConfig recipe, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(recipe.Id))
        {
            error = "recipe_id_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            error = "recipe_name_required";
            return false;
        }

        recipe.Id = recipe.Id.Trim();
        recipe.Name = recipe.Name.Trim();
        return ValidateRecipeVars(recipe.Vars, out error);
    }

    private bool ValidateRecipeVars(IReadOnlyDictionary<string, object?> vars, out string? error)
    {
        error = null;
        var allowed = CollectAllowedRecipeKeys();

        // First recipe may define its own keys when no candidates exist yet.
        if (allowed.Count == 0)
        {
            foreach (var key in vars.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    allowed.Add(key.Trim());
                }
            }
        }

        if (allowed.Count == 0)
        {
            error = "recipe_var_keys_empty";
            return false;
        }

        foreach (var key in vars.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                error = $"recipe_var_key_invalid:{key}";
                return false;
            }

            var trimmed = key.Trim();
            if (allowed.Contains(trimmed))
            {
                continue;
            }

            // Allow any param under an existing task / platform / device / axis owner.
            if (TrySplitOwnerParamKey(trimmed, out var kind, out var ownerId, out _)
                && OwnerExists(kind, ownerId))
            {
                continue;
            }

            error = $"recipe_var_key_invalid:{key}";
            return false;
        }

        return true;
    }

    private bool OwnerExists(string kind, string ownerId) => kind switch
    {
        "task" => _setting.Tasks.Any(t =>
            string.Equals(t.Name, ownerId, StringComparison.OrdinalIgnoreCase)),
        "platform" => _setting.Platforms.Any(d =>
            string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase)),
        "device" => _setting.Devices.Any(d =>
            string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase)),
        "axis" => _setting.Axes.Any(d =>
            string.Equals(d.Id, ownerId, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    /// <summary>
    /// Allowed recipe keys: recipeVarKeys ∪ Vars ∪ other recipes ∪ task/platform/device/axis params.
    /// </summary>
    private HashSet<string> CollectAllowedRecipeKeys()
    {
        var allowed = RecipeVarKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _setting.Vars.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                allowed.Add(key.Trim());
            }
        }

        foreach (var recipe in _setting.Recipes)
        {
            foreach (var key in recipe.Vars.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    allowed.Add(key.Trim());
                }
            }
        }

        foreach (var task in _setting.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Name))
            {
                continue;
            }

            foreach (var paramKey in task.Parameters.Keys)
            {
                if (!string.IsNullOrWhiteSpace(paramKey))
                {
                    allowed.Add($"{TaskKeyPrefix}{task.Name.Trim()}.{paramKey.Trim()}");
                }
            }
        }

        void AddDeviceKeys(string prefix, IEnumerable<MdkSetting.DeviceConfig> devices)
        {
            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Id))
                {
                    continue;
                }

                foreach (var paramKey in device.Parameters.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(paramKey))
                    {
                        allowed.Add($"{prefix}{device.Id.Trim()}.{paramKey.Trim()}");
                    }
                }
            }
        }

        AddDeviceKeys(PlatformKeyPrefix, _setting.Platforms);
        AddDeviceKeys(DeviceKeyPrefix, _setting.Devices);
        AddDeviceKeys(AxisKeyPrefix, _setting.Axes);
        return allowed;
    }

    private static MdkSetting.RecipeConfig CloneRecipe(MdkSetting.RecipeConfig recipe) =>
        new()
        {
            Id = recipe.Id,
            Name = recipe.Name,
            Description = recipe.Description,
            Vars = new Dictionary<string, object?>(recipe.Vars, StringComparer.OrdinalIgnoreCase)
        };
}

public sealed record RecipeSummary(string Id, string Name, string? Description);

public sealed record RecipeSnapshot(
    string? ActiveRecipeId,
    IReadOnlyList<string> RecipeVarKeys,
    IReadOnlyList<RecipeSummary> Recipes);
