using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Config;

/// <summary>Thrown when a settings/config file cannot be used. The message is safe to show to users.</summary>
public sealed class ConfigFormatException : Exception
{
    public ConfigFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// JSON (de)serialization for the settings document, exported configs (.synapseconfig) and backups
/// (.synapsebackup). All reads are size-limited, validated and sanitized.
/// </summary>
public static class ConfigSerializer
{
    public const string ConfigFormatId = "SynapseMouseAdjustments.Config";
    public const int ConfigSchemaVersion = 1;
    public const string ConfigExtension = ".synapseconfig";
    public const string BackupExtension = ".synapsebackup";

    /// <summary>Config files are tiny; anything bigger is rejected before parsing.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(SettingsDocument doc) => JsonSerializer.Serialize(doc, Options);

    /// <summary>Parses and sanitizes a settings document (settings.json or a backup file).</summary>
    public static SettingsDocument DeserializeDocument(string json)
    {
        CheckSize(json);
        JsonObject root = ParseObject(json);
        string? format = ReadFormat(root);
        if (format is not null && format != SettingsDocument.FormatId)
        {
            throw new ConfigFormatException(format == ConfigFormatId
                ? "This file is a single config. Use \"Import config\" on the Configs page instead."
                : "This is not a Synapse Mouse Adjustments settings file.");
        }

        CheckVersion(root, SettingsDocument.CurrentSchemaVersion);
        SettingsDocument? doc;
        try
        {
            doc = root.Deserialize<SettingsDocument>(Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
        {
            throw new ConfigFormatException("The settings file contains invalid values.", ex);
        }

        if (doc is null)
        {
            throw new ConfigFormatException("The settings file is empty.");
        }

        return ConfigSanitizer.Sanitize(doc);
    }

    /// <summary>Wraps one config in the portable .synapseconfig format.</summary>
    public static string ExportConfig(MouseConfig config)
    {
        var envelope = new ConfigEnvelope
        {
            Format = ConfigFormatId,
            SchemaVersion = ConfigSchemaVersion,
            ExportedUtc = DateTime.UtcNow,
            Config = Clone(config),
        };
        envelope.Config.Hotkey = null; // Hotkeys are machine-specific; do not carry them across.
        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <summary>
    /// Parses a .synapseconfig file. The result is validated and sanitized and gets a fresh id,
    /// so it can be added next to existing configs safely.
    /// </summary>
    public static MouseConfig ImportConfig(string json)
    {
        CheckSize(json);
        JsonObject root = ParseObject(json);
        string? format = ReadFormat(root);
        if (format == SettingsDocument.FormatId)
        {
            throw new ConfigFormatException("This file is a full settings backup. Use \"Import settings\" on the Settings page instead.");
        }

        if (format != ConfigFormatId)
        {
            throw new ConfigFormatException("This is not a Synapse Mouse Adjustments config file (.synapseconfig).");
        }

        CheckVersion(root, ConfigSchemaVersion);
        if (root["config"] is not JsonObject configNode)
        {
            throw new ConfigFormatException("The config file does not contain a config.");
        }

        MouseConfig? config;
        try
        {
            config = configNode.Deserialize<MouseConfig>(Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
        {
            throw new ConfigFormatException("The config file contains invalid values.", ex);
        }

        if (config is null)
        {
            throw new ConfigFormatException("The config file is empty.");
        }

        config.Id = Guid.NewGuid();
        config.Hotkey = null;
        config.ModifiedUtc = DateTime.UtcNow;
        return ConfigSanitizer.Sanitize(config);
    }

    /// <summary>Deep copy via JSON round trip.</summary>
    public static MouseConfig Clone(MouseConfig config)
    {
        string json = JsonSerializer.Serialize(config, Options);
        return JsonSerializer.Deserialize<MouseConfig>(json, Options)!;
    }

    public static SettingsDocument Clone(SettingsDocument doc) =>
        JsonSerializer.Deserialize<SettingsDocument>(Serialize(doc), Options)!;

    private static JsonObject ParseObject(string json)
    {
        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 32,
            });
            return node as JsonObject ?? throw new ConfigFormatException("The file does not contain a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ConfigFormatException("The file is not valid JSON: " + ex.Message, ex);
        }
    }

    private static string? ReadFormat(JsonObject root) =>
        root["format"] is JsonValue value && value.TryGetValue(out string? format) ? format : null;

    private static void CheckSize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ConfigFormatException("The file is empty.");
        }

        if (json.Length > MaxFileBytes)
        {
            throw new ConfigFormatException("The file is too large to be a Synapse config.");
        }
    }

    private static void CheckVersion(JsonObject root, int supported)
    {
        int version;
        try
        {
            version = root["schemaVersion"]?.GetValue<int>() ?? supported;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new ConfigFormatException("The file has an invalid schema version.", ex);
        }

        if (version > supported)
        {
            throw new ConfigFormatException("The file was created by a newer version of Synapse Mouse Adjustments.");
        }

        if (version < 1)
        {
            throw new ConfigFormatException("The file has an invalid schema version.");
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 32,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ConfigEnvelope
    {
        public string Format { get; set; } = ConfigFormatId;

        public int SchemaVersion { get; set; } = ConfigSchemaVersion;

        public DateTime ExportedUtc { get; set; }

        public MouseConfig Config { get; set; } = new();
    }
}
