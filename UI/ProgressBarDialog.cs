// GeoscientistToolkit/UI/ProgressBarDialog.cs
using System;
using System.Numerics;
using System.Threading;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    /// <summary>
    /// A reusable modal dialog for displaying progress of a long-running operation.
    /// </summary>
    public class ProgressBarDialog
    {
        private readonly string _title;
        private string _statusText = "";
        private float _progress; // 0.0f to 1.0f
        private bool _isOpen;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Gets a value indicating whether the user has requested cancellation.
        /// </summary>
        public bool IsCancellationRequested => _cancellationTokenSource?.IsCancellationRequested ?? false;
        
        /// <summary>
        /// The cancellation token to pass to the long-running operation.
        /// </summary>
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProgressBarDialog"/> class.
        /// </summary>
        /// <param name="title">The title of the dialog window.</param>
        public ProgressBarDialog(string title)
        {
            _title = title;
        }

        /// <summary>
        /// Opens the progress dialog and resets its state.
        /// </summary>
        /// <param name="initialStatus">The initial status message to display.</param>
        public void Open(string initialStatus)
        {
            _statusText = initialStatus;
            _progress = 0.0f;
            _isOpen = true;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Updates the progress bar and status text.
        /// </summary>
        /// <param name="progress">The current progress, from 0.0f to 1.0f.</param>
        /// <param name="status">The new status message to display.</param>
        public void Update(float progress, string status)
        {
            _progress = Math.Clamp(progress, 0.0f, 1.0f);
            _statusText = status;
        }

        /// <summary>
        /// Closes the dialog.
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        /// <summary>
        /// Submits the UI for the dialog. This should be called every frame.
        /// </summary>
        public void Submit()
        {
            if (!_isOpen) return;

            // Use a specific ID for the popup to avoid conflicts
            string popupId = $"ProgressPopup_{_title}";

            // We must use OpenPopup and BeginPopupModal on separate frames.
            // A simple way to manage this is to call OpenPopup once when _isOpen is set to true.
            ImGui.OpenPopup(popupId);
            
            // Center the modal
            var mainViewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(mainViewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(400, 0));

            // Begin the modal popup
            if (ImGui.BeginPopupModal(popupId, ref _isOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar))
            {
                // Title
                ImGui.Text(_title);
                ImGui.Separator();
                ImGui.Spacing();

                // Status text
                ImGui.Text(_statusText);

                // Progress bar
                ImGui.ProgressBar(_progress, new Vector2(-1, 0));
                ImGui.Spacing();
                
                ImGui.Separator();
                ImGui.Spacing();

                // Cancellation button
                var buttonSize = new Vector2(100, 0);
                ImGui.SetCursorPosX(ImGui.GetWindowWidth() - buttonSize.X - ImGui.GetStyle().WindowPadding.X);
                
                if (ImGui.Button("Cancel", buttonSize))
                {
                    _cancellationTokenSource?.Cancel();
                    Close(); // Immediately close the dialog on cancel
                }
                
                ImGui.EndPopup();
            }
            else
            {
                // If the popup is closed by other means (like pressing ESC), ensure our state is consistent.
                if (_isOpen)
                {
                    _cancellationTokenSource?.Cancel(); // Signal cancellation if closed unexpectedly
                    Close();
                }
            }
        }
    }
}