namespace MDKOSS.Core;

/// <summary>
/// Manages named recipe presets: a subset of <see cref="MdkSetting.Vars"/> keys defined by
/// <see cref="MdkSetting.RecipeVarKeys"/>, stored in <see cref="MdkSetting.Recipes"/>.
/// </summary>
public sealed class MdkRecipeManager
{
    public const string ActiveIdVarKey = "recipe.activeId";
    public const string ActiveNameVarKey = "recipe.activeName";

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
        foreach (var key in RecipeVarKeys)
        {
            if (recipe.Vars.TryGetValue(key, out var value))
            {
                _vars.Set(key, value);
            }
            else if (_setting.Vars.TryGetValue(key, out var baseValue))
            {
                _vars.Set(key, baseValue);
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
            }
        }
    }

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
        var allowed = RecipeVarKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            error = "recipe_var_keys_empty";
            return false;
        }

        foreach (var key in vars.Keys)
        {
            if (!allowed.Contains(key))
            {
                error = $"recipe_var_key_invalid:{key}";
                return false;
            }
        }

        return true;
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
