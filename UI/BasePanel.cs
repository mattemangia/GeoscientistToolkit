// GeoscientistToolkit/UI/BasePanel.cs
using System;
using System.Collections.Generic;
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
        private static List<BasePanel> _allPanels = new List<BasePanel>();
        private bool _popOutWindowWantsClosed = false;
        private bool _wantsToPopIn = false;
        private IntPtr _mainContext;

        protected BasePanel(string title, Vector2 defaultSize)
        {
            _title = title;
            _defaultSize = defaultSize;
            _allPanels.Add(this);
            // Store the main window's ImGui context
            _mainContext = VeldridManager.ImGuiController.Context;
        }

        /// <summary>
        /// Process all popped out windows
        /// </summary>
        public static void ProcessAllPopOutWindows()
        {
            foreach (var panel in _allPanels)
            {
                if (panel._isPoppedOut && panel._popOutWindow != null)
                {
                    panel._popOutWindow.ProcessFrame();
                    
                    // Check if window should be closed after processing
                    if (panel._popOutWindowWantsClosed)
                    {
                        panel._popOutWindowWantsClosed = false;
                        panel._isOpen = false;
                        panel.DoPopIn();
                    }
                    
                    // Check if window wants to pop in after processing
                    if (panel._wantsToPopIn)
                    {
                        panel._wantsToPopIn = false;
                        panel.DoPopIn();
                    }
                }
            }
        }

        /// <summary>
        /// Main submit method that handles both docked and popped-out states
        /// </summary>
        public void Submit(ref bool pOpen)
        {
            _isOpen = pOpen;
            
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
                
                // Don't draw anything in the main window when popped out
                // The drawing happens in the pop-out window's ProcessFrame
                return;
            }
            
            // Only draw in main window if not popped out
            if (!_isPoppedOut)
            {
                // Ensure we're using the main context
                ImGui.SetCurrentContext(_mainContext);
                
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
                
                _isOpen = pOpen;
            }
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
                {
                    // Set flag to pop in after frame completes
                    _wantsToPopIn = true;
                }
                else
                {
                    PopOut();
                }
            }
            
            ImGui.PopStyleColor(4);
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isPoppedOut ? "Return panel to main window" : "Pop out to separate window");
            }
            
            // Add some spacing after the button
            ImGui.Separator();
            ImGui.Spacing();
        }

        /// <summary>
        /// Draws content in the pop-out window (called from PopOutWindow with correct context)
        /// </summary>
        private void DrawInPopOutWindow()
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            
            // Use local variable to avoid ref in lambda
            bool localOpen = true;
            if (ImGui.Begin(_title + "##PopOut", ref localOpen, 
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | 
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
            {
                DrawPopOutButton();
                DrawContent();
            }
            ImGui.End();
            
            // Set flag if window wants to close
            if (!localOpen)
            {
                _popOutWindowWantsClosed = true;
            }
        }

        /// <summary>
        /// Pops out the panel to a separate window
        /// </summary>
        protected virtual void PopOut()
        {
            if (_isPoppedOut) return;
            
            // Calculate position for new window (offset a bit to make it obvious)
            var mainVp = ImGui.GetMainViewport();
            var newX = (int)(_lastMainWindowPos.X + mainVp.Pos.X + 20);
            var newY = (int)(_lastMainWindowPos.Y + mainVp.Pos.Y + 20);
            
            // Create the pop-out window
            _popOutWindow = new PopOutWindow(
                _title, 
                newX, 
                newY, 
                (int)_lastMainWindowSize.X, 
                (int)_lastMainWindowSize.Y
            );
            
            // Set the draw callback - use method reference instead of lambda
            _popOutWindow.SetDrawCallback(DrawInPopOutWindow);
            
            _isPoppedOut = true;
        }

        /// <summary>
        /// Request to return the panel to the main window (deferred until after frame)
        /// </summary>
        protected virtual void PopIn()
        {
            _wantsToPopIn = true;
        }

        /// <summary>
        /// Actually performs the pop-in (called after frame completes)
        /// </summary>
        private void DoPopIn()
        {
            if (!_isPoppedOut) return;
            
            _isPoppedOut = false;
            _popOutWindow?.Dispose();
            _popOutWindow = null;
        }

        public virtual void Dispose()
        {
            _allPanels.Remove(this);
            _popOutWindow?.Dispose();
        }
    }
}