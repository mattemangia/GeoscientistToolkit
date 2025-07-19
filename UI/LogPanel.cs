// GeoscientistToolkit/UI/LogPanel.cs (Updated to inherit from BasePanel)
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;

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
                    var color = GetLogLevelColor(entry.Level);
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    
                    string levelStr = entry.Level switch
                    {
                        LogLevel.Debug => "[DEBUG]",
                        LogLevel.Info => "[INFO]",
                        LogLevel.Warning => "[WARN]",
                        LogLevel.Error => "[ERROR]",
                        _ => "[?]"
                    };
                    
                    ImGui.TextUnformatted($"{entry.Timestamp:HH:mm:ss} {levelStr} {entry.Message}");
                    ImGui.PopStyleColor();
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
                LogLevel.Debug => new Vector4(0.5f, 0.5f, 0.5f, 1.0f),
                LogLevel.Info => new Vector4(0.8f, 0.8f, 0.8f, 1.0f),
                LogLevel.Warning => new Vector4(1.0f, 0.8f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };
        }
    }
}