using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AGS.Editor.Avalonia.Services
{
    /// <summary>The editor's look: the macOS style (default) or the classic look closer to the Windows editor.</summary>
    public enum EditorAppearance { Mac, Classic }

    /// <summary>Light, dark, or whatever macOS is set to.</summary>
    public enum EditorThemeMode { System, Light, Dark }

    /// <summary>
    /// Editor preferences of this Mac user (not of a game): kept in
    /// ~/Library/Application Support/AGS Editor/settings.json, never in the project files.
    /// </summary>
    public class EditorSettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EditorAppearance Appearance { get; set; } = EditorAppearance.Mac;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EditorThemeMode Theme { get; set; } = EditorThemeMode.System;

        /// <summary>Where the settings are kept (tests point it elsewhere).</summary>
        public static string SettingsFile { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AGS Editor", "settings.json");

        private static EditorSettings _current;

        /// <summary>The settings in use, loaded on first access.</summary>
        public static EditorSettings Current
        {
            get => _current ??= Load();
            set => _current = value;
        }

        /// <summary>Raised after <see cref="Apply"/> changed the settings in use.</summary>
        public static event Action Changed;

        public static EditorSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(SettingsFile)) ?? new EditorSettings();
            }
            catch (Exception)
            {
                // unreadable: defaults
            }
            return new EditorSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Makes these the settings in use, saves them and tells the app to restyle itself.</summary>
        public void Apply()
        {
            _current = this;
            Save();
            Changed?.Invoke();
        }
    }
}
