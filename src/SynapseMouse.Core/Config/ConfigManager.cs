using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Config;

/// <summary>
/// Owns the in-memory settings document and implements every config operation
/// (create / rename / duplicate / delete / reset / import / export / default / switch).
/// UI-thread only; persistence is triggered by the <see cref="Changed"/> event.
/// </summary>
public sealed class ConfigManager
{
    public ConfigManager(SettingsDocument document)
    {
        Document = ConfigSanitizer.Sanitize(document);
    }

    public SettingsDocument Document { get; private set; }

    public IReadOnlyList<MouseConfig> Configs => Document.Configs;

    public MouseConfig Active => Find(Document.ActiveConfigId) ?? Document.Configs[0];

    public MouseConfig DefaultConfig => Find(Document.DefaultConfigId) ?? Document.Configs[0];

    public AppSettings App => Document.App;

    /// <summary>The active config changed (switch, delete, import of a backup…).</summary>
    public event EventHandler? ActiveConfigChanged;

    /// <summary>A config was added, removed, renamed or reordered.</summary>
    public event EventHandler? ConfigListChanged;

    /// <summary>Anything in the document changed and should be saved.</summary>
    public event EventHandler? Changed;

    public MouseConfig? Find(Guid id)
    {
        foreach (var config in Document.Configs)
        {
            if (config.Id == id)
            {
                return config;
            }
        }

        return null;
    }

    public MouseConfig? FindByName(string name) =>
        Document.Configs.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes a config active. Returns false when it does not exist or is already active.</summary>
    public bool SetActive(Guid id)
    {
        if (Find(id) is null || Document.ActiveConfigId == id)
        {
            return false;
        }

        Document.ActiveConfigId = id;
        ActiveConfigChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
        return true;
    }

    /// <summary>Returns the next/previous config in list order (wrapping).</summary>
    public MouseConfig Neighbor(int direction)
    {
        int index = Document.Configs.IndexOf(Active);
        int count = Document.Configs.Count;
        return Document.Configs[((index + direction) % count + count) % count];
    }

    public MouseConfig Create(string name, string preset = ConfigPresets.Custom)
    {
        EnsureCapacity();
        var config = ConfigPresets.Create(preset, UniqueName(ConfigSanitizer.CleanName(name, "New config")));
        ConfigSanitizer.Sanitize(config);
        Document.Configs.Add(config);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
        return config;
    }

    public MouseConfig Duplicate(Guid id)
    {
        var source = Find(id) ?? throw new InvalidOperationException("Config not found.");
        EnsureCapacity();
        var copy = ConfigSerializer.Clone(source);
        copy.Id = Guid.NewGuid();
        copy.Name = UniqueName(source.Name + " copy");
        copy.Hotkey = null;
        copy.ModifiedUtc = DateTime.UtcNow;
        foreach (var vb in copy.Buttons.VirtualButtons)
        {
            vb.Id = Guid.NewGuid();
        }

        Document.Configs.Insert(Document.Configs.IndexOf(source) + 1, copy);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
        return copy;
    }

    public void Rename(Guid id, string newName)
    {
        var config = Find(id) ?? throw new InvalidOperationException("Config not found.");
        string cleaned = ConfigSanitizer.CleanName(newName, config.Name);
        if (cleaned == config.Name)
        {
            return;
        }

        config.Name = UniqueName(cleaned, config);
        Touch(config);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    /// <summary>Deletes a config. The last remaining config cannot be deleted.</summary>
    public bool Delete(Guid id)
    {
        var config = Find(id);
        if (config is null || Document.Configs.Count <= 1)
        {
            return false;
        }

        bool wasActive = Document.ActiveConfigId == id;
        Document.Configs.Remove(config);
        Document.App.AppAssociations.RemoveAll(a => a.ConfigId == id);
        if (Document.DefaultConfigId == id)
        {
            Document.DefaultConfigId = Document.Configs[0].Id;
        }

        if (Document.App.LastManualConfigId == id)
        {
            Document.App.LastManualConfigId = Document.DefaultConfigId;
        }

        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        if (wasActive)
        {
            Document.ActiveConfigId = Document.DefaultConfigId;
            ActiveConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Restores a config to its preset values, keeping its identity, name and hotkey.</summary>
    public void Reset(Guid id)
    {
        var config = Find(id) ?? throw new InvalidOperationException("Config not found.");
        var fresh = ConfigPresets.Create(config.Preset, config.Name);
        fresh.Id = config.Id;
        fresh.Hotkey = config.Hotkey;
        fresh.MasterEnabled = config.MasterEnabled;
        ConfigSanitizer.Sanitize(fresh);
        Document.Configs[Document.Configs.IndexOf(config)] = fresh;
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        if (Document.ActiveConfigId == id)
        {
            ActiveConfigChanged?.Invoke(this, EventArgs.Empty);
        }

        RaiseChanged();
    }

    public void SetDefault(Guid id)
    {
        if (Find(id) is null || Document.DefaultConfigId == id)
        {
            return;
        }

        Document.DefaultConfigId = id;
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    /// <summary>Imports a .synapseconfig file's contents as a new config (never replaces an existing one).</summary>
    public MouseConfig Import(string json)
    {
        EnsureCapacity();
        var config = ConfigSerializer.ImportConfig(json);
        config.Name = UniqueName(config.Name);
        Document.Configs.Add(config);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
        return config;
    }

    public string Export(Guid id) =>
        ConfigSerializer.ExportConfig(Find(id) ?? throw new InvalidOperationException("Config not found."));

    /// <summary>Replaces the whole document (backup import / reset all).</summary>
    public void ReplaceDocument(SettingsDocument document)
    {
        Document = ConfigSanitizer.Sanitize(document);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        ActiveConfigChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    /// <summary>Call after editing a config's settings in place.</summary>
    public void NotifyEdited(MouseConfig config)
    {
        Touch(config);
        RaiseChanged();
    }

    /// <summary>Call after editing app-wide settings in place.</summary>
    public void NotifyAppSettingsEdited() => RaiseChanged();

    public void MoveConfig(Guid id, int direction)
    {
        var config = Find(id);
        if (config is null)
        {
            return;
        }

        int index = Document.Configs.IndexOf(config);
        int target = Math.Clamp(index + direction, 0, Document.Configs.Count - 1);
        if (target == index)
        {
            return;
        }

        Document.Configs.RemoveAt(index);
        Document.Configs.Insert(target, config);
        ConfigListChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    /// <summary>Returns <paramref name="baseName"/> or "baseName (2)", "(3)"… so names stay unique.</summary>
    public string UniqueName(string baseName, MouseConfig? except = null)
    {
        string name = ConfigSanitizer.CleanName(baseName, "Config");
        bool Taken(string candidate) => Document.Configs.Any(c =>
            !ReferenceEquals(c, except) && string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));

        if (!Taken(name))
        {
            return name;
        }

        for (int n = 2; n < 1000; n++)
        {
            string suffix = $" ({n})";
            string stem = name.Length + suffix.Length > ConfigSanitizer.MaxNameLength
                ? name[..(ConfigSanitizer.MaxNameLength - suffix.Length)]
                : name;
            string candidate = stem + suffix;
            if (!Taken(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N")[..8];
    }

    private void EnsureCapacity()
    {
        if (Document.Configs.Count >= ConfigSanitizer.MaxConfigs)
        {
            throw new InvalidOperationException($"You can have at most {ConfigSanitizer.MaxConfigs} configs.");
        }
    }

    private static void Touch(MouseConfig config) => config.ModifiedUtc = DateTime.UtcNow;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
