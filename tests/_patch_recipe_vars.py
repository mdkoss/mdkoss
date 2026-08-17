from pathlib import Path

p = Path(r"d:/Work/mdkoss/src/MDKOSS.Config.Wpf/ConfigWorkspace.cs")
text = p.read_text(encoding="utf-8")

replacements = []

replacements.append((
    '    private bool _showComposeAxes;\n    private string _headline = "未选择组件";',
    '    private bool _showComposeAxes;\n    private bool _showPickRecipeVars;\n    private string _headline = "未选择组件";',
))

replacements.append((
    '''    /// <summary>Show「组合轴」button when editing a Platform component.</summary>
    public bool ShowComposeAxes
    {
        get => _showComposeAxes;
        set { if (_showComposeAxes == value) return; _showComposeAxes = value; OnPropertyChanged(); }
    }
    public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; OnPropertyChanged(); } }''',
    '''    /// <summary>Show「组合轴」button when editing a Platform component.</summary>
    public bool ShowComposeAxes
    {
        get => _showComposeAxes;
        set { if (_showComposeAxes == value) return; _showComposeAxes = value; OnPropertyChanged(); }
    }
    /// <summary>Show「从 Vars…」button when editing a Recipe component.</summary>
    public bool ShowPickRecipeVars
    {
        get => _showPickRecipeVars;
        set { if (_showPickRecipeVars == value) return; _showPickRecipeVars = value; OnPropertyChanged(); }
    }
    public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; OnPropertyChanged(); } }''',
))

replacements.append((
    '            ShowComposeAxes = false;\n            ResetFieldLabels();',
    '            ShowComposeAxes = false;\n            ShowPickRecipeVars = false;\n            ResetFieldLabels();',
))

replacements.append((
    '''            case ConfigModule.Recipes:
                req.Id = UniqueId(_setting.Recipes.Select(r => r.Id), "recipe-new");
                req.Name = req.Id;
                break;''',
    '''            case ConfigModule.Recipes:
                req.Id = UniqueId(_setting.Recipes.Select(r => r.Id), "recipe-new");
                req.Name = req.Id;
                {
                    var seed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (_setting.RecipeVarKeys.Count > 0)
                    {
                        foreach (var key in _setting.RecipeVarKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                        {
                            var k = key.Trim();
                            seed[k] = _setting.Vars.TryGetValue(k, out var value) ? value : "";
                        }
                    }

                    req.Vars = seed;
                    req.KeySuggestions = EnumerateRecipeKeyCandidates().Select(c => c.Key).ToList();
                }
                break;''',
))

replacements.append((
    '''            Draft.IsReadOnly = false;
            Draft.ShowQuickAddTypes = false;
            Draft.QuickAddTypes.Clear();
            Draft.Headline = $"{ModuleDisplayName(item.Module)} / {item.Title}";''',
    '''            Draft.IsReadOnly = false;
            Draft.ShowQuickAddTypes = false;
            Draft.ShowComposeAxes = false;
            Draft.ShowPickRecipeVars = false;
            Draft.QuickAddTypes.Clear();
            Draft.Headline = $"{ModuleDisplayName(item.Module)} / {item.Title}";''',
))

replacements.append((
    '''                case ConfigModule.Recipes when item.Source is MdkSetting.RecipeConfig r:
                    SetDraftVisibility(id: true, name: true, description: true, parameters: true);
                    Draft.ShowType = false;
                    Draft.ShowEnabled = false;
                    Draft.FieldId = r.Id;
                    Draft.FieldName = r.Name;
                    Draft.FieldDescription = r.Description ?? "";
                    Draft.LoadObjectParameters(r.Vars);
                    Draft.SetParamKeySuggestions(_setting.RecipeVarKeys);
                    break;''',
    '''                case ConfigModule.Recipes when item.Source is MdkSetting.RecipeConfig r:
                    SetDraftVisibility(id: true, name: true, description: true, parameters: true);
                    Draft.ShowType = false;
                    Draft.ShowEnabled = false;
                    Draft.ShowPickRecipeVars = true;
                    Draft.FieldId = r.Id;
                    Draft.FieldName = r.Name;
                    Draft.FieldDescription = r.Description ?? "";
                    Draft.LoadObjectParameters(r.Vars);
                    RefreshRecipeParamSuggestions();
                    break;''',
))

replacements.append((
    '''        if (module is ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Tasks or ConfigModule.Drivers)
        {
            Draft.SetParamValueSuggestions(
                _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>Refine Value ComboBox items for the parameter key currently being edited.</summary>
    public void RefreshParamValueSuggestionsForKey(string? key)
''',
    '''        if (module is ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Tasks or ConfigModule.Drivers)
        {
            Draft.SetParamValueSuggestions(
                _setting.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        if (module == ConfigModule.Recipes)
        {
            Draft.SetParamValueSuggestions(
                _setting.Vars.Values
                    .Select(FormatRecipeVarValue)
                    .Where(v => !string.IsNullOrWhiteSpace(v)));
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>Refine Value ComboBox items for the parameter key currently being edited.</summary>
    public void RefreshParamValueSuggestionsForKey(string? key)
''',
))

helper_block = r'''
        if (_module == ConfigModule.Recipes)
        {
            RefreshRecipeValueSuggestionsForKey(k);
            return;
        }

        if (_module == ConfigModule.SysConfig)
        {
            RefreshSysConfigValueSuggestionsForKey(k);
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>Key suggestions for recipe vars: SysConfig.recipeVarKeys U Vars U other recipes.</summary>
    public void RefreshRecipeParamSuggestions()
    {
        Draft.SetParamKeySuggestions(EnumerateRecipeKeyCandidates().Select(c => c.Key));
        RefreshParamValueSuggestions(ConfigModule.Recipes);
    }

    /// <summary>Candidates for the recipe vars picker (Vars + SysConfig.recipeVarKeys + existing recipe keys).</summary>
    public IReadOnlyList<RecipeVarCandidate> GetRecipeVarCandidates() =>
        EnumerateRecipeKeyCandidates()
            .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Merge selected keys into the current recipe draft.
    /// Existing keys keep their draft values; new keys take current Vars values when available.
    /// </summary>
    public void ApplyRecipeVarSelection(IEnumerable<string> keys)
    {
        if (_module != ConfigModule.Recipes || Draft.IsReadOnly || !Draft.ShowParameters)
        {
            throw new InvalidOperationException("仅 Recipe 组件支持从 Vars / SysConfig 选择键。");
        }

        var selected = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一个变量键。");
        }

        var book = Draft.CollectObjectParameters();
        foreach (var key in selected)
        {
            if (book.ContainsKey(key))
            {
                continue;
            }

            book[key] = _setting.Vars.TryGetValue(key, out var value) ? value : "";
        }

        Draft.LoadObjectParameters(book);
        Draft.MarkDirty();
        RefreshRecipeParamSuggestions();
        StatusLine = $"已加入 {selected.Count} 个配方变量键";
    }

    private IEnumerable<RecipeVarCandidate> EnumerateRecipeKeyCandidates()
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void AddSource(string key, string source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var k = key.Trim();
            if (!sources.TryGetValue(k, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sources[k] = set;
            }

            set.Add(source);
        }

        foreach (var key in _setting.RecipeVarKeys)
        {
            AddSource(key, "sysconfig.recipeVarKeys");
        }

        foreach (var key in _setting.Vars.Keys)
        {
            AddSource(key, "vars");
        }

        foreach (var recipe in _setting.Recipes)
        {
            foreach (var key in recipe.Vars.Keys)
            {
                AddSource(key, $"recipe:{recipe.Id}");
            }
        }

        foreach (var (key, tags) in sources)
        {
            var ordered = tags
                .OrderBy(t => t.StartsWith("sysconfig", StringComparison.OrdinalIgnoreCase) ? 0
                    : t.Equals("vars", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(t => t, StringComparer.OrdinalIgnoreCase);
            yield return new RecipeVarCandidate
            {
                Key = key,
                Source = string.Join(" · ", ordered),
                ValuePreview = _setting.Vars.TryGetValue(key, out var value)
                    ? FormatRecipeVarValue(value)
                    : "",
            };
        }
    }

    private void RefreshRecipeValueSuggestionsForKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Draft.ParamValueSuggestions.Clear();
            return;
        }

        var suggestions = new List<string>();
        if (_setting.Vars.TryGetValue(key, out var current))
        {
            var text = FormatRecipeVarValue(current);
            if (!string.IsNullOrWhiteSpace(text))
            {
                suggestions.Add(text);
            }
        }

        foreach (var recipe in _setting.Recipes)
        {
            if (recipe.Vars.TryGetValue(key, out var fromRecipe))
            {
                var text = FormatRecipeVarValue(fromRecipe);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    suggestions.Add(text);
                }
            }
        }

        Draft.SetParamValueSuggestions(suggestions);
    }

    private void RefreshSysConfigValueSuggestionsForKey(string key)
    {
        var entryKey = _selected?.Key ?? "";
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            entryKey = Draft.ParameterRows
                .FirstOrDefault(r => string.Equals(r.Key, "key", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim() ?? "";
        }

        if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase)
            && string.Equals(entryKey, "activeRecipeId", StringComparison.OrdinalIgnoreCase))
        {
            Draft.SetParamValueSuggestions(
                _setting.Recipes.Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)));
            return;
        }

        if (string.Equals(key, "value", StringComparison.OrdinalIgnoreCase)
            && string.Equals(entryKey, "recipeVarKeys", StringComparison.OrdinalIgnoreCase))
        {
            Draft.SetParamValueSuggestions(_setting.Vars.Keys.Where(k => !string.IsNullOrWhiteSpace(k)));
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    private static string FormatRecipeVarValue(object? value) => value switch
    {
        null => "",
        JsonElement je => je.ValueKind == JsonValueKind.String
            ? je.GetString() ?? ""
            : je.ToString(),
        _ => Convert.ToString(value) ?? "",
    };

    /// <summary>
    /// Apply Axis device selections into draft parameters (<c>axis.X</c> = Axis id only).
    /// </summary>
    public void ApplyPlatformAxisComposition(IReadOnlyDictionary<string, string> letterToAxisId)
'''

replacements.append((
    '''            Draft.ParamValueSuggestions.Clear();
            return;
        }

        Draft.ParamValueSuggestions.Clear();
    }

    /// <summary>
    /// Apply Axis device selections into draft parameters (<c>axis.X</c> = Axis id only).
    /// </summary>
    public void ApplyPlatformAxisComposition(IReadOnlyDictionary<string, string> letterToAxisId)
''',
    helper_block.lstrip('\n') if False else (
        '''            Draft.ParamValueSuggestions.Clear();
            return;
        }

''' + helper_block
    ),
))

for i, (old, new) in enumerate(replacements):
    if old not in text:
        raise SystemExit(f"missing pattern #{i}")
    text = text.replace(old, new, 1)
    print(f"applied #{i}")

p.write_text(text, encoding="utf-8")
print("ConfigWorkspace.cs OK")
