// GeoscientistToolkit/Settings/SettingsManager.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using Veldrid;
using Veldrid.StartupUtilities;
using System.Collections.Generic;

namespace GeoscientistToolkit.Settings
{
    public class SettingsManager
    {
        private static SettingsManager _instance;
        public static SettingsManager Instance => _instance ??= new SettingsManager();

        private readonly string _settingsFilePath;
        private readonly string _settingsDirectory;
        private AppSettings _settings;
        private AppSettings _originalSettings;

        public AppSettings Settings => _settings;
        public bool HasUnsavedChanges => !AreSettingsEqual(_settings, _originalSettings);

        public event Action<AppSettings> SettingsChanged;
        public event Action<string> SettingsSaved;
        public event Action<string> SettingsLoaded;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private SettingsManager()
        {
            _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GeoscientistToolkit");
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }
        
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                    Logger.Log("Settings loaded successfully");
                    SettingsLoaded?.Invoke(_settingsFilePath);
                }
                else
                {
                    _settings = AppSettings.CreateDefaults();
                    Logger.Log("Created default settings");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load settings: {ex.Message}");
                _settings = AppSettings.CreateDefaults();
            }

            _originalSettings = _settings.Clone();
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(_settingsDirectory);
                string json = JsonSerializer.Serialize(_settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
                
                _originalSettings = _settings.Clone();
                Logger.Log("Settings saved successfully");
                SettingsSaved?.Invoke(_settingsFilePath);
                
                //
                // THIS WAS THE BUG. IT HAS BEEN REMOVED.
                // ProjectManager.Instance.HasUnsavedChanges = true; 
                //
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save settings: {ex.Message}");
                throw;
            }
        }
        
        public void UpdateSettings(AppSettings newSettings)
        {
            _settings = newSettings.Clone();
            SettingsChanged?.Invoke(_settings);
        }

        private bool AreSettingsEqual(AppSettings a, AppSettings b)
        {
            var jsonA = JsonSerializer.Serialize(a, _jsonOptions);
            var jsonB = JsonSerializer.Serialize(b, _jsonOptions);
            return jsonA == jsonB;
        }

        public static string[] GetAvailableGpuNames()
        {
            var gpus = new List<string> { "Auto" };
            gpus.AddRange(GraphicsAdapterUtil.GetGpuList());
            return gpus.ToArray();
        }

        public static string[] GetAvailableBackends()
        {
            return new[] { "Auto", "Direct3D11", "Vulkan", "Metal", "OpenGL" };
        }
    }
}