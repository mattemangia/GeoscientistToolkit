// GeoscientistToolkit/UI/LogPanel.cs
// A panel for displaying real-time log messages from the static Logger.

using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using System.Text;

namespace GeoscientistToolkit.UI
{
    public class LogPanel
    {
        private bool _autoScroll = true;

        public void Submit(ref bool pOpen)
        {
            ImGui.SetNextWindowSize(new Vector2(0, 150), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Log", ref pOpen, ImGuiWindowFlags.None))
            {
                if (ImGui.Button("Clear"))
                {
                    Logger.Clear();
                }

                ImGui.SameLine();
                if (ImGui.Button("Copy"))
                {
                    var sb = new StringBuilder();
                    foreach (var message in Logger.GetMessages())
                    {
                        sb.AppendLine(message);
                    }
                    ImGui.SetClipboardText(sb.ToString());
                }

                ImGui.SameLine();
                ImGui.Checkbox("Auto-scroll", ref _autoScroll);

                ImGui.Separator();

                ImGui.BeginChild("LogScrollingRegion", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

                var messages = Logger.GetMessages();
                foreach (var message in messages)
                {
                    ImGui.TextUnformatted(message);
                }

                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1.0f);
                }

                ImGui.EndChild();
            }
            ImGui.End();
        }
    }
}