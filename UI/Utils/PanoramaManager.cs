// GeoscientistToolkit/UI/Panorama/PanoramaManager.cs

using GeoscientistToolkit.Data;
using Veldrid;

namespace GeoscientistToolkit.UI.Panorama;

/// <summary>
/// Manages the lifecycle of the Panorama Wizard UI panel.
/// </summary>
public class PanoramaManager
{
    public static PanoramaManager Instance { get; } = new();

    private PanoramaWizardPanel _wizardPanel;

    private PanoramaManager() { }

    /// <summary>
    /// Starts a new panorama stitching job and opens the wizard UI.
    /// </summary>
    /// <param name="imageGroup">The group of images to stitch.</param>
    /// <param name="graphicsDevice">The active Veldrid GraphicsDevice.</param>
    /// <param name="imGuiController">The active GeoscientistToolkit ImGuiController.</param>
    public void StartPanorama(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiController imGuiController)
    {
        // If a wizard is already open, bring its window to the front.
        if (_wizardPanel != null && _wizardPanel.IsOpen)
        {
            ImGuiNET.ImGui.SetWindowFocus(_wizardPanel.Title);
            return;
        }
        
        // CORRECTED: The constructor now receives the correct ImGuiController type.
        _wizardPanel = new PanoramaWizardPanel(imageGroup, graphicsDevice, imGuiController);
        _wizardPanel.Open();
    }

    /// <summary>
    /// Renders the wizard panel UI if it is open. This should be called every frame in the main UI loop.
    /// </summary>
    public void SubmitUI()
    {
        // The Submit method inside the panel handles its own lifecycle, including closing.
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