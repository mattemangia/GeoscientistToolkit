// GeoscientistToolkit/UI/BasePanel.cs
using System;
using System.Numerics;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// Base class for all panels that provides pop-out functionality
    /// </summary>
    public abstract class BasePanel : IDisposable
    {
        protected bool _isPoppedOut = false;
        protected PopOutWindow _popOutWindow;
        protected string _title;
        protected Vector2 _defaultSize;
        protected bool _isOpen = true;
        
        private Vector2 _lastMainWindowPos;
        private Vector2 _lastMainWindowSize;

        protected BasePanel(string title, Vector2 defaultSize)
        {
            _title = title;
            _defaultSize = defaultSize;
        }

        /// <summary>
        /// Main submit method that handles both docked and popped-out states
        /// </summary>
        public void Submit(ref bool pOpen)
        {
            if (_isPoppedOut && _popOutWindow != null)
            {
                // If window was closed externally, clean up
                if (!_popOutWindow.Exists)
                {
                    _isPoppedOut = false;
                    _popOutWindow.Dispose();
                    _popOutWindow = null;
                    return;
                }
                
                // Use a local copy for the lambda
                bool localOpen = pOpen;
                
                // Render in the pop-out window
                _popOutWindow.Render(() => {
                    ImGui.SetNextWindowPos(Vector2.Zero);
                    ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
                    
                    if (ImGui.Begin(_title + "##PopOut", ref localOpen, 
                        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | 
                        ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
                    {
                        DrawPopOutButton();
                        DrawContent();
                    }
                    ImGui.End();
                });
                
                // Update the ref parameter from the local copy
                pOpen = localOpen;
                
                if (!pOpen)
                {
                    PopIn();
                }
            }
            else
            {
                // Render in the main window
                ImGui.SetNextWindowSize(_defaultSize, ImGuiCond.FirstUseEver);
                
                if (ImGui.Begin(_title, ref pOpen))
                {
                    _lastMainWindowPos = ImGui.GetWindowPos();
                    _lastMainWindowSize = ImGui.GetWindowSize();
                    
                    DrawPopOutButton();
                    DrawContent();
                }
                ImGui.End();
            }
            
            _isOpen = pOpen;
        }

        /// <summary>
        /// Override this to provide the panel's content
        /// </summary>
        protected abstract void DrawContent();

        /// <summary>
        /// Draws the pop-out/pop-in button
        /// </summary>
        protected virtual void DrawPopOutButton()
        {
            // Save current cursor position
            var originalCursorPos = ImGui.GetCursorPos();
            
            // Draw the button at the beginning of the content area
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.59f, 0.98f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.59f, 0.98f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            
            // Position the button at the top-right of the content area
            var contentWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SameLine(contentWidth - 60);
            
            if (ImGui.Button(_isPoppedOut ? "Dock ↩" : "Pop Out ↗", new Vector2(60, 20)))
            {
                if (_isPoppedOut)
                    PopIn();
                else
                    PopOut();
            }
            
            ImGui.PopStyleColor(4);
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isPoppedOut ? "Return panel to main window" : "Pop out to separate window");
            }
            
            // Add some spacing after the button
            ImGui.Spacing();
        }

        /// <summary>
        /// Pops out the panel to a separate window
        /// </summary>
        protected virtual void PopOut()
        {
            if (_isPoppedOut) return;
            
            // Calculate position for new window
            var mainVp = ImGui.GetMainViewport();
            var newX = (int)(_lastMainWindowPos.X + mainVp.Pos.X);
            var newY = (int)(_lastMainWindowPos.Y + mainVp.Pos.Y);
            
            // Create the pop-out window
            _popOutWindow = new PopOutWindow(
                _title, 
                newX, 
                newY, 
                (int)_lastMainWindowSize.X, 
                (int)_lastMainWindowSize.Y
            );
            
            _isPoppedOut = true;
        }

        /// <summary>
        /// Returns the panel to the main window
        /// </summary>
        protected virtual void PopIn()
        {
            if (!_isPoppedOut) return;
            
            _isPoppedOut = false;
            _popOutWindow?.Dispose();
            _popOutWindow = null;
        }

        public virtual void Dispose()
        {
            _popOutWindow?.Dispose();
        }
    }
}