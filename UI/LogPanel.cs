// GeoscientistToolkit/UI/LogPanel.cs (Updated with selectable text)
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using System.Linq;
using System;
using GeoscientistToolkit.Settings; 

namespace GeoscientistToolkit.UI
{
    public class LogPanel : BasePanel
    {
        private bool _autoScroll = true;
        private string _filter = "";
        
        public LogPanel() : base("Log", new Vector2(600, 200))
        {
        }
        
        public void Submit(ref bool pOpen)
        {
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            // Toolbar
            if (ImGui.Button("Clear"))
            {
                Logger.Clear();
            }
            ImGui.SameLine();
            ImGui.Checkbox("Auto-scroll", ref _autoScroll);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##Filter", "Filter...", ref _filter, 256);

            ImGui.Separator();

            // Log content
            if (ImGui.BeginChild("LogContent", new Vector2(0, 0), ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar))
            {
                var entries = Logger.GetEntries()
                    .Where(e => string.IsNullOrEmpty(_filter) || 
                               e.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in entries)
                {
                    // Push a unique ID for each log entry to avoid ImGui conflicts
                    ImGui.PushID(entry.GetHashCode() + entry.Timestamp.Ticks.GetHashCode());

                    var color = GetLogLevelColor(entry.Level);
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    
                    string levelStr = entry.Level switch
                    {
                        LogLevel.Debug => "[DEBUG]",
                        LogLevel.Information => "[INFO]",
                        LogLevel.Warning => "[WARN]",
                        LogLevel.Error => "[ERROR]",
                        LogLevel.Critical => "[CRITICAL]",
                        LogLevel.Trace => "[TRACE]",
                        _ => "[?]"
                    };
                    
                    // Construct the full log message string
                    string logText = $"{entry.Timestamp:HH:mm:ss} {levelStr} {entry.Message}";

                    // --- CHANGE: Use a read-only InputText to make it selectable ---
                    // Style it to look like plain text by removing the frame/border
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);

                    ImGui.InputText("##LogLine", ref logText, (uint)logText.Length + 1, ImGuiInputTextFlags.ReadOnly);

                    // Pop styling
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(); // Pops FrameBg
                    // --- END CHANGE ---
                    
                    ImGui.PopStyleColor(); // Pops Text color
                    ImGui.PopID();
                }

                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1.0f);
                }
            }
            ImGui.EndChild();
        }

        private Vector4 GetLogLevelColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                LogLevel.Debug => new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
                LogLevel.Information => new Vector4(0.8f, 0.8f, 0.8f, 1.0f),
                LogLevel.Warning => new Vector4(1.0f, 0.8f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
                LogLevel.Critical => new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };
        }
    }
}