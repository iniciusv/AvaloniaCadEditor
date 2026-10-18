using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AiCore.Models;
using AvaloniaCadEditor.Application.Ai;
using AvaloniaCadEditor.Domain.Settings;
using AvaloniaCadEditor.Harness;
using AvaloniaCadEditor.Harness.Roles;

namespace AvaloniaCadEditor.UI.ViewModels.Harness;

/// <summary>
/// A parte do painel de configurações que é do harness (ferramentas e Inspetor). Fica à parte do
/// <see cref="AiChatViewModel"/>: sem harness ligado no composition root, ela simplesmente não existe.
/// Só delega: quem cria a sessão é a <see cref="HarnessSessionFactory"/>.
/// </summary>
public sealed class HarnessSettingsViewModel : INotifyPropertyChanged
{
    private readonly IHarnessSettingsStore _store;
    private readonly HarnessSessionFactory _factory;

    public HarnessSettingsViewModel(IHarnessSettingsStore store, HarnessSessionFactory factory)
    {
        _store = store;
        _factory = factory;
    }

    private bool _enabled;
    /// <summary>Chat com ferramentas (vista em texto, cotas, mover/girar, editar trechos).</summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    private bool _useInspector;
    public bool UseInspector
    {
        get => _useInspector;
        set { _useInspector = value; OnPropertyChanged(); }
    }

    private AiEndpointProfile? _inspectorProfile;
    /// <summary>Perfil do Inspetor (um dos perfis do painel; normalmente um modelo local).</summary>
    public AiEndpointProfile? InspectorProfile
    {
        get => _inspectorProfile;
        set { _inspectorProfile = value; OnPropertyChanged(); }
    }

    private bool _preInspection = true;
    public bool PreInspection
    {
        get => _preInspection;
        set { _preInspection = value; OnPropertyChanged(); }
    }

    /// <summary>Como a sessão atual ficou ("ferramentas · Inspetor: Ollama"), vazio sem harness.</summary>
    public string SessionLabel => _factory.Description;

    /// <summary>Preenche com o que está gravado; <paramref name="profiles"/> são os perfis em edição no painel.</summary>
    public void Load(IEnumerable<AiEndpointProfile> profiles)
    {
        var settings = _store.Load();
        Enabled = settings.Enabled;
        PreInspection = settings.PreInspection;
        string? inspectorId = settings.ProfileFor(AgentRole.Inspector);
        InspectorProfile = profiles.FirstOrDefault(p => p.Id == inspectorId);
        UseInspector = InspectorProfile != null;
    }

    public void Save()
    {
        var settings = _store.Load();
        settings.Enabled = Enabled;
        settings.PreInspection = PreInspection;
        settings.SetProfile(AgentRole.Inspector, UseInspector ? InspectorProfile?.Id : null);
        _store.Save(settings);
    }

    public ICadScriptChatSession CreateSession(LlmOptions options, CadScriptProtocol protocol)
    {
        var session = _factory.Create(options, protocol);
        OnPropertyChanged(nameof(SessionLabel));
        return session;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
