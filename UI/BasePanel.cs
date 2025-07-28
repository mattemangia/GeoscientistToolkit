// GeoscientistToolkit/UI/BasePanel.cs (Fixed pop-out window rendering)
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
        
        // --- FIX: _mainContext field is removed as it's the source of the bug. ---

        /// <summary>
        /// Provides read-only access to the list of all created panels.
        /// </summary>
        public static IReadOnlyList<BasePanel> AllPanels => _allPanels;

        protected BasePanel(string title, Vector2 defaultSize)
        {
            _title = title;
            _defaultSize = defaultSize;
            _allPanels.Add(this);
            // --- FIX: No longer need to store the main context here. ---
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
        /// Main submit method that handles both docked and popped-out states.
        /// This version uses the internal _isOpen flag as the source of truth for the panel's state.
        /// </summary>
        public void Submit(ref bool pOpen)
        {
            // Sync our state with the caller. If they pass false, we close.
            if (!pOpen)
            {
                _isOpen = false;
            }

            // If we've been closed (programmatically or by caller), report it and clean up.
            if (!_isOpen)
            {
                pOpen = false;
                if (_isPoppedOut)
                {
                    DoPopIn(); // This disposes the popout window.
                }
                return;
            }

            // --- Panel is considered open at this point ---

            if (_isPoppedOut && _popOutWindow != null)
            {
                // For popped-out panels, we just check if the OS window still exists.
                // Drawing is handled by ProcessAllPopOutWindows().
                if (!_popOutWindow.Exists)
                {
                    // The window was closed by the user.
                    _isOpen = false; // Mark as closed.
                    pOpen = false;   // Report back to the caller.
                    DoPopIn();       // Clean up resources.
                }
                return; // Don't draw in the main window.
            }
            
            // If not popped out, render the panel as a window in the main UI.
            if (!_isPoppedOut)
            {
                // --- FIX: REMOVED the call to ImGui.SetCurrentContext(_mainContext); ---
                // The panel should render in whatever context is currently active.
                
                ImGui.SetNextWindowSize(_defaultSize, ImGuiCond.FirstUseEver);
                
                // Pass our authoritative _isOpen flag to ImGui. It will be set to false if the user closes the window.
                if (ImGui.Begin(_title, ref _isOpen))
                {
                    _lastMainWindowPos = ImGui.GetWindowPos();
                    _lastMainWindowSize = ImGui.GetWindowSize();
                    
                    DrawPopOutButton();
                    DrawContent();
                }
                ImGui.End();
                
                // After rendering, ensure the caller's flag is in sync with our state.
                pOpen = _isOpen;
            }
        }

        /// <summary>
        /// Programmatically closes the panel. The panel will be removed on the next UI loop.
        /// </summary>
        public void Close()
        {
            _isOpen = false;
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
            var cursorPos = ImGui.GetCursorPos();
            
            // Position button at top-right of content area
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var buttonSize = new Vector2(30, 24);
            ImGui.SetCursorPos(new Vector2(contentWidth - buttonSize.X - 5, cursorPos.Y));
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.25f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.59f, 0.98f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.59f, 0.98f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 3));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3.0f);
            
            // Use a regular button instead of invisible button for better visibility
            if (ImGui.Button($"##PopOutBtn{_title}", buttonSize))
            {
                if (_isPoppedOut)
                {
                    _wantsToPopIn = true;
                }
                else
                {
                    PopOut();
                }
            }
            
            ImGui.PopStyleVar(2);
            
            // Draw custom icon on top of the button
            var drawList = ImGui.GetWindowDrawList();
            var buttonMin = ImGui.GetItemRectMin();
            var buttonMax = ImGui.GetItemRectMax();
            var buttonCenter = new Vector2((buttonMin.X + buttonMax.X) * 0.5f, (buttonMin.Y + buttonMax.Y) * 0.5f);
            
            var iconColor = ImGui.IsItemHovered() ? 
                ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)) : 
                ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
            
            if (_isPoppedOut)
            {
                // Draw "dock" icon - simplified version
                var size = 8.0f;
                var p1 = buttonCenter + new Vector2(-size, -size);
                var p2 = buttonCenter + new Vector2(size, size);
                
                // Window frame
                drawList.AddRect(p1, p2, iconColor, 0.0f, ImDrawFlags.None, 1.5f);
                
                // Arrow pointing inward
                var arrowStart = buttonCenter + new Vector2(0, -size - 3);
                var arrowEnd = buttonCenter;
                drawList.AddLine(arrowStart, arrowEnd, iconColor, 2.0f);
                
                // Arrow head
                drawList.AddLine(arrowEnd, arrowEnd + new Vector2(-3, -3), iconColor, 2.0f);
                drawList.AddLine(arrowEnd, arrowEnd + new Vector2(3, -3), iconColor, 2.0f);
            }
            else
            {
                // Draw "pop out" icon - two overlapping windows
                var size = 6.0f;
                
                // Back window
                var p1 = buttonCenter + new Vector2(-size + 2, -size + 2);
                var p2 = buttonCenter + new Vector2(size + 2, size + 2);
                drawList.AddRect(p1, p2, iconColor, 0.0f, ImDrawFlags.None, 1.5f);
                
                // Front window
                var p3 = buttonCenter + new Vector2(-size - 2, -size - 2);
                var p4 = buttonCenter + new Vector2(size - 2, size - 2);
                drawList.AddRectFilled(p3, p4, ImGui.GetColorU32(ImGuiCol.Button), 0.0f);
                drawList.AddRect(p3, p4, iconColor, 0.0f, ImDrawFlags.None, 1.5f);
                
                // Arrow pointing outward
                var arrowStart = buttonCenter;
                var arrowEnd = buttonCenter + new Vector2(size + 3, -size - 3);
                drawList.AddLine(arrowStart, arrowEnd, iconColor, 2.0f);
                
                // Arrow head
                drawList.AddLine(arrowEnd, arrowEnd + new Vector2(-3, 0), iconColor, 2.0f);
                drawList.AddLine(arrowEnd, arrowEnd + new Vector2(0, 3), iconColor, 2.0f);
            }
            
            ImGui.PopStyleColor(3);
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isPoppedOut ? "Return panel to main window" : "Pop out to separate window");
            }
            
            // Restore cursor position
            ImGui.SetCursorPos(cursorPos);
            
            // Add spacing to push content down below the button
            ImGui.Dummy(new Vector2(0, buttonSize.Y + 5));
            ImGui.Separator();
            ImGui.Spacing();
        }

        /// <summary>
        /// Draws content in the pop-out window (called from PopOutWindow with correct context)
        /// </summary>
        private void DrawInPopOutWindow()
        {
            // FIXED: Create a properly positioned and sized window instead of full-screen
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowPadding = ImGui.GetStyle().WindowPadding;
            
            // Set window to take most of the display but with some padding
            ImGui.SetNextWindowPos(windowPadding);
            ImGui.SetNextWindowSize(displaySize - windowPadding * 2);
            
            // Use proper window flags - keep the title bar and allow resizing
            if (ImGui.Begin(_title, ref _isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
            {
                DrawPopOutButton();
                DrawContent();
            }
            ImGui.End();
            
            // Set flag if window wants to close
            if (!_isOpen)
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