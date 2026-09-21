using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvaloniaCadEditor.UI.ViewModels;

public enum TreeItemCategory
{
    FeatureGroup,
    ProfileGroup,
    EntityGroup,
    FeatureItem,
    ProfileItem,
    EntityItem,
    BooleanBaseGroup,
    BooleanToolGroup
}

public class TreeItemNode : INotifyPropertyChanged
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🔹";
    public TreeItemCategory Category { get; set; }
    public object? Tag { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
                
                // Sync with underlying domain object
                if (Tag is AvaloniaCadEditor.Domain.Features.IFeature feature)
                {
                    feature.IsVisible = value;
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (Tag is AvaloniaCadEditor.Domain.Profiles.IProfile profile)
                {
                    profile.IsVisible = value;
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (Tag is AvaloniaCadEditor.Domain.Entities.Entity entity)
                {
                    entity.IsVisible = value;
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
    
    public event EventHandler? VisibilityChanged;

    public ObservableCollection<TreeItemNode> Children { get; } = new();

    public TreeItemNode(string name, string icon, TreeItemCategory category, Guid? id = null, object? tag = null)
    {
        Name = name;
        Icon = icon;
        Category = category;
        Id = id;
        Tag = tag;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
