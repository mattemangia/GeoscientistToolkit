// GeoscientistToolkit/AddIns/AddInManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.AddIns
{
    /// <summary>
    /// Manages loading, unloading, and interaction with add-ins
    /// </summary>
    public class AddInManager
    {
        private static AddInManager _instance;
        public static AddInManager Instance => _instance ??= new AddInManager();

        private readonly Dictionary<string, IAddIn> _loadedAddIns = new();
        private readonly Dictionary<string, AddInInfo> _addInInfos = new();
        private readonly List<AddInMenuItem> _menuItems = new();
        private readonly List<AddInTool> _tools = new();
        private readonly List<IDataImporter> _importers = new();
        private readonly List<IDataExporter> _exporters = new();

        public IReadOnlyDictionary<string, IAddIn> LoadedAddIns => _loadedAddIns;
        public IReadOnlyList<AddInMenuItem> MenuItems => _menuItems;
        public IReadOnlyList<AddInTool> Tools => _tools;
        public IReadOnlyList<IDataImporter> Importers => _importers;
        public IReadOnlyList<IDataExporter> Exporters => _exporters;

        public event Action<IAddIn> AddInLoaded;
        public event Action<IAddIn> AddInUnloaded;
        public event Action<string, Exception> AddInError;

        private AddInManager()
        {
            // Subscribe to settings changes
            SettingsManager.Instance.SettingsChanged += OnSettingsChanged;
        }

        /// <summary>
        /// Initializes the add-in system and loads enabled add-ins
        /// </summary>
        public void Initialize()
        {
            var settings = SettingsManager.Instance.Settings.AddIns;
            
            if (!settings.LoadAddInsOnStartup)
            {
                Logger.Log("Add-in loading on startup is disabled");
                return;
            }

            LoadAddInsFromDirectory(settings.AddInDirectory);
        }

        /// <summary>
        /// Loads all add-ins from the specified directory
        /// </summary>
        public void LoadAddInsFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Logger.LogWarning($"Add-in directory does not exist: {directory}");
                return;
            }

            Logger.Log($"Loading add-ins from: {directory}");

            // Look for .dll files in the directory
            var dllFiles = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
            
            foreach (var dllFile in dllFiles)
            {
                try
                {
                    LoadAddInFromAssembly(dllFile);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load add-in from {dllFile}: {ex.Message}");
                    AddInError?.Invoke(dllFile, ex);
                }
            }
        }

        /// <summary>
        /// Loads an add-in from the specified assembly file
        /// </summary>
        private void LoadAddInFromAssembly(string assemblyPath)
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            
            // Find all types that implement IAddIn
            var addInTypes = assembly.GetTypes()
                .Where(t => typeof(IAddIn).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            if (addInTypes.Count == 0)
            {
                Logger.LogWarning($"No add-ins found in assembly: {assemblyPath}");
                return;
            }

            foreach (var addInType in addInTypes)
            {
                try
                {
                    var addIn = (IAddIn)Activator.CreateInstance(addInType);
                    LoadAddIn(addIn);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to create add-in instance of type {addInType.Name}: {ex.Message}");
                    AddInError?.Invoke(addInType.Name, ex);
                }
            }
        }

        /// <summary>
        /// Loads a specific add-in
        /// </summary>
        public void LoadAddIn(IAddIn addIn)
        {
            if (_loadedAddIns.ContainsKey(addIn.Id))
            {
                Logger.LogWarning($"Add-in {addIn.Id} is already loaded");
                return;
            }

            // Check if the add-in is enabled in settings
            var settings = SettingsManager.Instance.Settings.AddIns;
            var addInInfo = settings.InstalledAddIns.FirstOrDefault(a => a.Id == addIn.Id);
            
            if (addInInfo == null)
            {
                // New add-in, add to settings
                addInInfo = new AddInInfo
                {
                    Id = addIn.Id,
                    Name = addIn.Name,
                    Version = addIn.Version,
                    Author = addIn.Author,
                    Description = addIn.Description,
                    Enabled = true,
                    InstalledDate = DateTime.Now
                };
                settings.InstalledAddIns.Add(addInInfo);
                SettingsManager.Instance.SaveSettings();
            }

            if (!addInInfo.Enabled)
            {
                Logger.Log($"Add-in {addIn.Id} is disabled");
                return;
            }

            try
            {
                // Initialize the add-in
                addIn.Initialize();

                // Register the add-in
                _loadedAddIns[addIn.Id] = addIn;
                _addInInfos[addIn.Id] = addInInfo;

                // Collect menu items
                var menuItems = addIn.GetMenuItems();
                if (menuItems != null)
                {
                    _menuItems.AddRange(menuItems);
                }

                // Collect tools
                var tools = addIn.GetTools();
                if (tools != null)
                {
                    _tools.AddRange(tools);
                }

                // Collect importers
                var importers = addIn.GetDataImporters();
                if (importers != null)
                {
                    _importers.AddRange(importers);
                }

                // Collect exporters
                var exporters = addIn.GetDataExporters();
                if (exporters != null)
                {
                    _exporters.AddRange(exporters);
                }

                Logger.Log($"Loaded add-in: {addIn.Name} v{addIn.Version} by {addIn.Author}");
                AddInLoaded?.Invoke(addIn);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize add-in {addIn.Id}: {ex.Message}");
                AddInError?.Invoke(addIn.Id, ex);
                
                // Remove from loaded list if initialization failed
                if (_loadedAddIns.ContainsKey(addIn.Id))
                {
                    _loadedAddIns.Remove(addIn.Id);
                }
            }
        }

        /// <summary>
        /// Unloads a specific add-in
        /// </summary>
        public void UnloadAddIn(string addInId)
        {
            if (!_loadedAddIns.TryGetValue(addInId, out var addIn))
            {
                Logger.LogWarning($"Add-in {addInId} is not loaded");
                return;
            }

            try
            {
                // Shutdown the add-in
                addIn.Shutdown();

                // Remove menu items
                _menuItems.RemoveAll(m => 
                {
                    var items = addIn.GetMenuItems();
                    return items != null && items.Contains(m);
                });

                // Remove tools
                _tools.RemoveAll(t =>
                {
                    var tools = addIn.GetTools();
                    return tools != null && tools.Contains(t);
                });

                // Remove importers
                _importers.RemoveAll(i =>
                {
                    var importers = addIn.GetDataImporters();
                    return importers != null && importers.Contains(i);
                });

                // Remove exporters
                _exporters.RemoveAll(e =>
                {
                    var exporters = addIn.GetDataExporters();
                    return exporters != null && exporters.Contains(e);
                });

                // Remove from loaded list
                _loadedAddIns.Remove(addInId);
                _addInInfos.Remove(addInId);

                Logger.Log($"Unloaded add-in: {addIn.Name}");
                AddInUnloaded?.Invoke(addIn);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error while unloading add-in {addInId}: {ex.Message}");
                AddInError?.Invoke(addInId, ex);
            }
        }

        /// <summary>
        /// Enables or disables an add-in
        /// </summary>
        public void SetAddInEnabled(string addInId, bool enabled)
        {
            var settings = SettingsManager.Instance.Settings.AddIns;
            var addInInfo = settings.InstalledAddIns.FirstOrDefault(a => a.Id == addInId);
            
            if (addInInfo == null)
            {
                Logger.LogWarning($"Add-in {addInId} not found in settings");
                return;
            }

            addInInfo.Enabled = enabled;
            SettingsManager.Instance.SaveSettings();

            if (enabled && !_loadedAddIns.ContainsKey(addInId))
            {
                // Try to load the add-in
                // This would need the assembly path, which we don't have here
                // In a real implementation, we'd store the assembly path in AddInInfo
                Logger.Log($"Add-in {addInId} enabled but not loaded (requires restart)");
            }
            else if (!enabled && _loadedAddIns.ContainsKey(addInId))
            {
                // Unload the add-in
                UnloadAddIn(addInId);
            }
        }

        /// <summary>
        /// Shuts down all add-ins
        /// </summary>
        public void Shutdown()
        {
            var addInIds = _loadedAddIns.Keys.ToList();
            foreach (var addInId in addInIds)
            {
                UnloadAddIn(addInId);
            }
        }

        /// <summary>
        /// Gets menu items for a specific menu path
        /// </summary>
        public IEnumerable<AddInMenuItem> GetMenuItemsForPath(string menuPath)
        {
            return _menuItems.Where(m => m.Path.StartsWith(menuPath));
        }

        /// <summary>
        /// Gets tools that can work with the specified dataset
        /// </summary>
        public IEnumerable<AddInTool> GetToolsForDataset(Data.Dataset dataset)
        {
            return _tools.Where(t => t.CanExecute(dataset));
        }

        /// <summary>
        /// Gets importers that support the specified file extension
        /// </summary>
        public IEnumerable<IDataImporter> GetImportersForExtension(string extension)
        {
            return _importers.Where(i => i.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets exporters that support the specified file extension
        /// </summary>
        public IEnumerable<IDataExporter> GetExportersForExtension(string extension)
        {
            return _exporters.Where(e => e.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            // Handle changes to add-in settings
            foreach (var addInInfo in settings.AddIns.InstalledAddIns)
            {
                var isLoaded = _loadedAddIns.ContainsKey(addInInfo.Id);
                
                if (addInInfo.Enabled && !isLoaded)
                {
                    // Add-in was enabled but not loaded
                    Logger.Log($"Add-in {addInInfo.Id} enabled (requires restart to load)");
                }
                else if (!addInInfo.Enabled && isLoaded)
                {
                    // Add-in was disabled, unload it
                    UnloadAddIn(addInInfo.Id);
                }
            }
        }
    }
}