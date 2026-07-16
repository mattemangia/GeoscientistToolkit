// GAIA/UI/Photogrammetry/PhotogrammetryManager.cs

using GAIA.Data;

namespace GAIA.UI.Photogrammetry;

/// <summary>
/// Manages the lifecycle of the Photogrammetry Wizard UI panel.
/// </summary>
public class PhotogrammetryManager
{
    public static PhotogrammetryManager Instance { get; } = new();

    private PhotogrammetryWizardPanel _wizardPanel;

    private PhotogrammetryManager() { }

    /// <summary>
    /// Starts a new photogrammetry job and opens the wizard UI.
    /// </summary>
    /// <param name="imageGroup">The group of images to process.</param>
    public void StartPhotogrammetry(DatasetGroup imageGroup)
    {
        // If a wizard is already open, bring its window to the front.
        if (_wizardPanel != null && _wizardPanel.IsOpen)
        {
            ImGuiNET.ImGui.SetWindowFocus(_wizardPanel.Title);
            return;
        }
        
        _wizardPanel = new PhotogrammetryWizardPanel(imageGroup);
        _wizardPanel.Open();
    }

    /// <summary>
    /// Renders the wizard panel UI if it is open. This should be called every frame in the main UI loop.
    /// </summary>
    public void SubmitUI()
    {
        if (_wizardPanel != null)
        {
            _wizardPanel.Submit();
            if (!_wizardPanel.IsOpen)
            {
                _wizardPanel = null; // Clean up after closing
            }
        }
    }
}
