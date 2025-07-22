// GeoscientistToolkit/Data/Image/ImageExportDialog.cs
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.UI.Utils;
using System.IO; // Added for Path

namespace GeoscientistToolkit.Data.Image
{
    public class ImageExportDialog
    {
        private bool _isOptionsOpen; // Renamed from _isOpen for clarity
        private bool _includeScaleBar = true;
        private bool _includeTopInfo = true;
        private readonly ImGuiExportFileDialog _fileDialog;
        private ImageDataset _dataset;
        
        public ImageExportDialog()
        {
            _fileDialog = new ImGuiExportFileDialog("ImageExportDialog", "Export Image");
            _fileDialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image"),
                (".tiff", "TIFF Image"),
                (".bmp", "Bitmap Image")
            );
        }
        
        public void Open(ImageDataset dataset)
        {
            _dataset = dataset;
            _isOptionsOpen = true;
        }
        
        public bool Submit()
        {
            // FIX: The dialog wrapper is considered "active" if either the options window
            // OR the file save dialog is open. This ensures _fileDialog.Submit() is always called
            // when needed.
            if (!_isOptionsOpen && !_fileDialog.IsOpen) 
                return false;
            
            // --- Draw the initial options dialog ---
            if (_isOptionsOpen)
            {
                ImGui.SetNextWindowSize(new Vector2(400, 250), ImGuiCond.FirstUseEver);
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
            
                // Use a local variable for the Begin call to handle closing with 'X'
                bool pOpen = _isOptionsOpen;
                if (ImGui.Begin("Export Image Options", ref pOpen, 
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking))
                {
                    ImGui.Text("Select what to include in the exported image:");
                    ImGui.Spacing();
                
                    ImGui.Checkbox("Include Scale Bar", ref _includeScaleBar);
                    ImGui.Checkbox("Include Top Information", ref _includeTopInfo);
                
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                
                    ImGui.Text("Export Options:");
                    ImGui.BulletText("Scale Bar: " + (_includeScaleBar ? "Yes" : "No"));
                    ImGui.BulletText("Top Info: " + (_includeTopInfo ? "Yes" : "No"));
                
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                
                    float buttonWidth = 100;
                    float spacing = ImGui.GetStyle().ItemSpacing.X;
                    float totalWidth = buttonWidth * 2 + spacing;
                    float startX = (ImGui.GetContentRegionAvail().X - totalWidth) * 0.5f;
                
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startX);
                
                    if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
                    {
                        _isOptionsOpen = false;
                    }
                
                    ImGui.SameLine();
                
                    if (ImGui.Button("Continue", new Vector2(buttonWidth, 0)))
                    {
                        // Close this dialog and open the next one
                        _isOptionsOpen = false;
                        _fileDialog.Open(Path.GetFileNameWithoutExtension(_dataset.Name));
                    }
                
                    ImGui.End();
                }

                // If the user clicks the 'X' on the window, update our state
                if (!pOpen)
                {
                    _isOptionsOpen = false;
                }
            }
            
            // --- Handle the file save dialog ---
            // FIX: This now runs every frame as long as this dialog component is active,
            // allowing it to appear right after the options dialog closes.
            if (_fileDialog.Submit())
            {
                // A file was selected, so perform the export
                var exporter = new ImageExporter();
                exporter.Export(_dataset, _fileDialog.SelectedPath, _includeScaleBar, _includeTopInfo);
                return true; // Signal that the operation is complete
            }
            
            return false; // Still in progress
        }
    }
}