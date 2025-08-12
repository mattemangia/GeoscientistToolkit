using System;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class PropertiesPanel : BasePanel
    {
        private readonly MetadataEditor _metadataEditor = new();
        
        public PropertiesPanel() : base("Properties", new Vector2(300, 400))
        {
        }

        public void Submit(ref bool pOpen, Dataset dataset)
        {
            _dataset = dataset;
            base.Submit(ref pOpen);
            
            // Submit metadata editor if open
            _metadataEditor.Submit();
        }

        private Dataset _dataset;

        protected override void DrawContent()
        {
            if (_dataset == null)
            {
                ImGui.TextDisabled("No dataset selected");
                return;
            }

            // Basic Properties
            if (ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                DrawProperty("Name", _dataset.Name);
                DrawProperty("Type", _dataset.Type.ToString());
                DrawProperty("Path", _dataset.FilePath);
                DrawProperty("Size", FormatFileSize(_dataset.GetSizeInBytes()));
                DrawProperty("Created", _dataset.DateCreated.ToString("yyyy-MM-dd HH:mm"));
                DrawProperty("Modified", _dataset.DateModified.ToString("yyyy-MM-dd HH:mm"));
                
                if (_dataset.IsMissing)
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "⚠ Source file/directory not found");
                }
                ImGui.Unindent();
            }
            
            // NEW: Metadata section
            if (ImGui.CollapsingHeader("Metadata"))
            {
                ImGui.Indent();
                
                var meta = _dataset.DatasetMetadata;
                if (meta != null)
                {
                    if (!string.IsNullOrEmpty(meta.SampleName))
                        DrawProperty("Sample", meta.SampleName);
                    
                    if (!string.IsNullOrEmpty(meta.LocationName))
                        DrawProperty("Location", meta.LocationName);
                    
                    if (meta.Latitude.HasValue && meta.Longitude.HasValue)
                        DrawProperty("Coordinates", $"{meta.Latitude:F6}°, {meta.Longitude:F6}°");
                    
                    if (meta.Depth.HasValue)
                        DrawProperty("Depth", $"{meta.Depth:F2} m");
                    
                    if (meta.Size.HasValue)
                        DrawProperty("Physical Size", $"{meta.Size.Value.X:F2} × {meta.Size.Value.Y:F2} × {meta.Size.Value.Z:F2} {meta.SizeUnit}");
                    
                    if (meta.CollectionDate.HasValue)
                        DrawProperty("Collection Date", meta.CollectionDate.Value.ToString("yyyy-MM-dd"));
                    
                    if (!string.IsNullOrEmpty(meta.Collector))
                        DrawProperty("Collector", meta.Collector);
                    
                    if (!string.IsNullOrEmpty(meta.Notes))
                    {
                        ImGui.Text("Notes:");
                        ImGui.TextWrapped(meta.Notes);
                    }
                    
                    // Custom fields
                    foreach (var kvp in meta.CustomFields)
                    {
                        DrawProperty(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    ImGui.TextDisabled("No metadata");
                }
                
                if (ImGui.Button("Edit Metadata..."))
                {
                    _metadataEditor.Open(_dataset);
                }
                
                ImGui.Unindent();
            }

            // Type-specific properties
            var renderer = DatasetUIFactory.CreatePropertiesRenderer(_dataset);
            renderer?.Draw(_dataset);
        }

        public static void DrawProperty(string label, string value)
        {
            ImGui.Text($"{label}:");
            ImGui.SameLine(120);
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), value);
        }

        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        public static string FormatNumber(long number)
        {
            if (number >= 1_000_000_000)
                return $"{number / 1_000_000_000.0:0.##}B";
            if (number >= 1_000_000)
                return $"{number / 1_000_000.0:0.##}M";
            if (number >= 1_000)
                return $"{number / 1_000.0:0.##}K";
            return number.ToString();
        }
    }
}