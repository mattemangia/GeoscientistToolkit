// GeoscientistToolkit/AddIns/IAddIn.cs
using System;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.AddIns
{
    /// <summary>
    /// Base interface for all add-ins
    /// </summary>
    public interface IAddIn
    {
        /// <summary>
        /// Unique identifier for the add-in
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Display name of the add-in
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Version of the add-in
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Author of the add-in
        /// </summary>
        string Author { get; }

        /// <summary>
        /// Description of what the add-in does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Called when the add-in is loaded
        /// </summary>
        void Initialize();

        /// <summary>
        /// Called when the add-in is unloaded
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Gets the menu items this add-in wants to add
        /// </summary>
        IEnumerable<AddInMenuItem> GetMenuItems();

        /// <summary>
        /// Gets the tools this add-in provides
        /// </summary>
        IEnumerable<AddInTool> GetTools();

        /// <summary>
        /// Gets the data importers this add-in provides
        /// </summary>
        IEnumerable<IDataImporter> GetDataImporters();

        /// <summary>
        /// Gets the data exporters this add-in provides
        /// </summary>
        IEnumerable<IDataExporter> GetDataExporters();
    }

    /// <summary>
    /// Represents a menu item added by an add-in
    /// </summary>
    public class AddInMenuItem
    {
        public string Path { get; set; } // e.g., "Tools/My Add-in/Action"
        public string Label { get; set; }
        public string Shortcut { get; set; }
        public Action OnClick { get; set; }
        public Func<bool> IsEnabled { get; set; }
    }

    /// <summary>
    /// Represents a tool provided by an add-in
    /// </summary>
    public abstract class AddInTool
    {
        public abstract string Name { get; }
        public abstract string Icon { get; }
        public abstract string Tooltip { get; }
        
        /// <summary>
        /// Executes the tool on the given dataset
        /// </summary>
        public abstract void Execute(Dataset dataset);
        
        /// <summary>
        /// Checks if the tool can be used with the given dataset
        /// </summary>
        public abstract bool CanExecute(Dataset dataset);
    }

    /// <summary>
    /// Interface for data importers provided by add-ins
    /// </summary>
    public interface IDataImporter
    {
        string Name { get; }
        string[] SupportedExtensions { get; }
        Dataset Import(string filePath);
    }

    /// <summary>
    /// Interface for data exporters provided by add-ins
    /// </summary>
    public interface IDataExporter
    {
        string Name { get; }
        string[] SupportedExtensions { get; }
        void Export(Dataset dataset, string filePath);
    }
}