// GeoscientistToolkit/UI/Utils/ConfirmationDialog.cs
using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// A reusable modal dialog for confirming actions.
    /// </summary>
    public class ConfirmationDialog
    {
        private readonly string _title;
        private readonly string _message;
        private bool _isOpen;

        public ConfirmationDialog(string title, string message)
        {
            _title = title;
            _message = message;
        }

        /// <summary>
        /// Opens the confirmation dialog on the next frame.
        /// </summary>
        public void Open()
        {
            _isOpen = true;
        }

        /// <summary>
        /// Draws the dialog and returns true if the user confirmed the action.
        /// </summary>
        /// <returns>True if 'Yes' was clicked, otherwise false.</returns>
        public bool Submit()
        {
            bool confirmed = false;

            if (_isOpen)
            {
                ImGui.OpenPopup(_title);
                _isOpen = false; // Reset for next call
            }

            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            if (ImGui.BeginPopupModal(_title, ref _isOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped(_message);
                ImGui.Separator();

                if (ImGui.Button("Yes", new Vector2(120, 0)))
                {
                    confirmed = true;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            return confirmed;
        }
    }
}