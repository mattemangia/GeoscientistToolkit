// GeoscientistToolkit/AddIns/Development/SampleUtilitiesAddIn.cs
// --- CORRECTED VERSION ---

using System.Text;
using GeoscientistToolkit.AddIns;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
// Using StringBuilder for cleaner string construction

namespace GeoscientistToolkit.CustomAddIns;

// A tool to show information about the currently selected dataset.
// This has been updated to use the properties from your actual Dataset.cs file.
public class DatasetInfoTool : AddInTool
{
    public override string Name => "Show Dataset Info";
    public override string Icon => "ℹ️";
    public override string Tooltip => "Shows detailed information about the current dataset.";

    // The tool is enabled as long as a dataset is selected.
    public override bool CanExecute(Dataset dataset)
    {
        return dataset != null;
    }

    // This is the code that runs when the tool is used.
    public override void Execute(Dataset dataset)
    {
        if (dataset == null)
        {
            Logger.LogWarning("DatasetInfoTool executed with no dataset loaded.");
            return;
        }

        // Use a StringBuilder to efficiently build the information string.
        var infoBuilder = new StringBuilder();

        // Use the ACTUAL properties from your Dataset class
        infoBuilder.AppendLine($"Name: {dataset.Name}");
        infoBuilder.AppendLine($"Type: {dataset.Type}");
        infoBuilder.AppendLine($"File Path: {dataset.FilePath}");
        infoBuilder.AppendLine($"Created: {dataset.DateCreated:g}"); // Corrected property name and added formatting
        infoBuilder.AppendLine($"Modified: {dataset.DateModified:g}"); // Added DateModified for more info

        // The base Dataset class has GetSizeInBytes(), which is a great replacement
        // for the non-existent 'DataPoints.Count'.
        infoBuilder.AppendLine($"Size: {FormatBytes(dataset.GetSizeInBytes())}");

        // Show the final information in a message box.
        CrossPlatformMessageBox.Show(infoBuilder.ToString(), "Dataset Information");
        Logger.Log($"Executed DatasetInfoTool on '{dataset.Name}'.");
    }

    // Helper function to make byte counts human-readable.
    private static string FormatBytes(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0)
            return "0" + suf[0];
        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{num} {suf[place]}";
    }
}

// This is the main entry point for the add-in.
// This part of the code did not need to be changed.
public class SampleUtilitiesAddIn : IAddIn
{
    public string Id => "com.yourname.sample-utilities";
    public string Name => "Sample Utilities";
    public string Version => "1.0.1"; // Incremented version
    public string Author => "Your Name";
    public string Description => "A sample add-in that provides a simple tool and menu item.";

    public void Initialize()
    {
        Logger.Log($"'{Name}' Add-In Initialized!");
    }

    public void Shutdown()
    {
        Logger.Log($"'{Name}' Add-In Shut Down.");
    }

    public IEnumerable<AddInMenuItem> GetMenuItems()
    {
        return new List<AddInMenuItem>
        {
            new()
            {
                Path = "Tools/Samples",
                Label = "Show Hello Message",
                Shortcut = "Ctrl+H",
                OnClick = () => { CrossPlatformMessageBox.Show("Hello from the Sample Add-In!", "Hello World"); },
                IsEnabled = () => true
            }
        };
    }

    public IEnumerable<AddInTool> GetTools()
    {
        return new List<AddInTool> { new DatasetInfoTool() };
    }

    public IEnumerable<IDataImporter> GetDataImporters()
    {
        return Enumerable.Empty<IDataImporter>();
    }

    public IEnumerable<IDataExporter> GetDataExporters()
    {
        return Enumerable.Empty<IDataExporter>();
    }
}