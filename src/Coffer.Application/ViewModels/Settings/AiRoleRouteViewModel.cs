using System.Collections.ObjectModel;
using Coffer.Core.Ai;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Settings;

/// <summary>
/// The provider + model pickers of one <see cref="AiRole"/> in Settings (Sprint 31-D). Claude offers the
/// fixed <see cref="AiDefaults.ClaudeModels"/>; Ollama offers the models the endpoint listed (handed in by
/// <see cref="SettingsViewModel"/> through <see cref="SetOllamaModels"/>), plus the stored model when the
/// server does not list it right now, so opening Settings while Ollama is down never loses a choice.
/// <para>
/// <see cref="IsEdited"/> turns true only when the owner picks a provider or model here: the parent writes
/// a route only then, so a stored route the pickers cannot show (an unknown provider) survives saving
/// another setting.
/// </para>
/// </summary>
public sealed partial class AiRoleRouteViewModel : ObservableObject
{
    private readonly Action<AiRoleRouteViewModel> _onProviderChanged;

    private IReadOnlyList<OllamaModelInfo>? _ollamaModels;
    private string? _selectedModel;
    private string? _lastClaudeModel;
    private string? _lastOllamaModel;

    // Set while Load fills the pickers, so loading does not count as an edit.
    private bool _loading;

    // Set while Models is rebuilt: the ComboBox writes null back when its items are cleared, which must
    // not wipe the selection.
    private bool _rebuilding;

    private AiProviderOption _selectedProvider;

    [ObservableProperty]
    private bool _modelNotListed;

    [ObservableProperty]
    private bool _modelLacksTools;

    [ObservableProperty]
    private string _capabilitiesText = "";

    public AiRoleRouteViewModel(
        AiRole role,
        IReadOnlyList<AiProviderOption> providers,
        Action<AiRoleRouteViewModel> onProviderChanged)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(onProviderChanged);
        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }

        Role = role;
        Providers = providers;
        _onProviderChanged = onProviderChanged;
        _selectedProvider = ProviderOrClaude(AiDefaults.ClaudeProvider);
        _selectedModel = AiDefaults.DefaultModel(role);
        RebuildModels();
    }

    public AiRole Role { get; }

    public IReadOnlyList<AiProviderOption> Providers { get; }

    /// <summary>The models the model picker offers for the selected provider.</summary>
    public ObservableCollection<string> Models { get; } = [];

    /// <summary>The picked model; <c>null</c> for an Ollama route before any model is chosen or listed.</summary>
    public string? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is null && _rebuilding)
            {
                return;
            }

            if (string.Equals(_selectedModel, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedModel = value;
            OnPropertyChanged();
            RememberModel();
            MarkEdited();
            UpdateModelFlags();
        }
    }

    public bool IsOllama => IsProvider(AiDefaults.OllamaProvider);

    public bool IsClaude => IsProvider(AiDefaults.ClaudeProvider);

    /// <summary>Whether the owner changed this role's provider or model since the last load or save.</summary>
    public bool IsEdited { get; private set; }

    /// <summary>Whether a picked Ollama route still has no model (Save refuses it).</summary>
    public bool IsMissingModel => string.IsNullOrWhiteSpace(_selectedModel);

    /// <summary>Shows a stored route. Claude models outside the offered list, and unknown providers, show the default.</summary>
    public void Load(AiRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        _loading = true;
        try
        {
            var provider = Providers.FirstOrDefault(
                p => string.Equals(p.Id, route.Provider, StringComparison.OrdinalIgnoreCase));
            string? model;
            if (provider is null)
            {
                // A provider this build does not have: shown as the default, kept in storage until edited.
                provider = ProviderOrClaude(AiDefaults.ClaudeProvider);
                model = AiDefaults.DefaultModel(Role);
            }
            else if (provider.Id == AiDefaults.ClaudeProvider)
            {
                model = AiDefaults.ClaudeModels.FirstOrDefault(
                    m => string.Equals(m, route.Model, StringComparison.OrdinalIgnoreCase))
                    ?? AiDefaults.DefaultModel(Role);
            }
            else
            {
                model = string.IsNullOrWhiteSpace(route.Model) ? null : route.Model;
            }

            _selectedModel = model;
            _lastClaudeModel = null;
            _lastOllamaModel = null;
            SelectedProvider = provider;
            RebuildModels();
            RememberModel();
            IsEdited = false;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Hands in the models the Ollama endpoint listed. The current pick is kept (and stays in the list even
    /// if the server no longer has it); an Ollama route the owner just switched to and that has no model yet
    /// gets one picked.
    /// </summary>
    public void SetOllamaModels(IReadOnlyList<OllamaModelInfo> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _ollamaModels = models;
        if (!IsOllama)
        {
            return;
        }

        if (IsMissingModel && IsEdited)
        {
            _selectedModel = PickOllamaModel();
            RememberModel();
        }

        RebuildModels();
    }

    /// <summary>The route to save.</summary>
    public AiRoute ToRoute() => new(SelectedProvider.Id, _selectedModel?.Trim() ?? "");

    /// <summary>Called after the route was saved.</summary>
    public void MarkSaved() => IsEdited = false;

    /// <summary>The picked provider. Switching it swaps the model list and picks a model for the new provider.</summary>
    public AiProviderOption SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            // A picker never clears itself on purpose (only when its items reset); keep the provider.
            if (value is null || ReferenceEquals(value, _selectedProvider))
            {
                return;
            }

            _selectedProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOllama));
            OnPropertyChanged(nameof(IsClaude));
            if (_loading)
            {
                return;
            }

            // Switching provider brings back the model last picked for it on this screen, else a sensible one.
            _selectedModel = value.Id == AiDefaults.OllamaProvider
                ? _lastOllamaModel ?? PickOllamaModel()
                : _lastClaudeModel ?? AiDefaults.DefaultModel(Role);
            RebuildModels();
            MarkEdited();
            _onProviderChanged(this);
        }
    }

    private void RebuildModels()
    {
        _rebuilding = true;
        try
        {
            Models.Clear();
            IEnumerable<string> offered = IsOllama
                ? _ollamaModels?.Select(m => m.Name) ?? []
                : IsClaude ? AiDefaults.ClaudeModels : [];
            foreach (var name in offered)
            {
                Models.Add(name);
            }

            // Keep a stored Ollama model the server does not list right now (Ollama down, model removed).
            if (!string.IsNullOrEmpty(_selectedModel)
                && !Models.Contains(_selectedModel, StringComparer.OrdinalIgnoreCase))
            {
                Models.Add(_selectedModel);
            }
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(SelectedModel));
        UpdateModelFlags();
    }

    private string? PickOllamaModel()
    {
        if (_ollamaModels is not { Count: > 0 } models)
        {
            return null;
        }

        // Chat needs tool calling, so the analysis role prefers a model that reports it.
        return Role == AiRole.Analysis
            ? (models.FirstOrDefault(m => m.SupportsTools) ?? models[0]).Name
            : models[0].Name;
    }

    private void UpdateModelFlags()
    {
        OnPropertyChanged(nameof(IsMissingModel));

        var info = IsOllama && !string.IsNullOrEmpty(_selectedModel)
            ? _ollamaModels?.FirstOrDefault(m => string.Equals(m.Name, _selectedModel, StringComparison.OrdinalIgnoreCase))
            : null;

        ModelNotListed = IsOllama && _ollamaModels is not null && !IsMissingModel && info is null;
        ModelLacksTools = Role == AiRole.Analysis && info is not null && !info.SupportsTools;
        CapabilitiesText = info is { Capabilities.Count: > 0 } ? string.Join(" · ", info.Capabilities) : "";
    }

    private void RememberModel()
    {
        if (string.IsNullOrEmpty(_selectedModel))
        {
            return;
        }

        if (IsOllama)
        {
            _lastOllamaModel = _selectedModel;
        }
        else if (IsClaude)
        {
            _lastClaudeModel = _selectedModel;
        }
    }

    private void MarkEdited()
    {
        if (!_loading)
        {
            IsEdited = true;
        }
    }

    private bool IsProvider(string id) => string.Equals(SelectedProvider.Id, id, StringComparison.Ordinal);

    private AiProviderOption ProviderOrClaude(string id) =>
        Providers.FirstOrDefault(p => p.Id == id) ?? Providers[0];
}
