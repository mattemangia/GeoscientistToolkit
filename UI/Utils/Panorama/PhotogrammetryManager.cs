// GeoscientistToolkit/UI/Photogrammetry/PhotogrammetryManager.cs

using GeoscientistToolkit.Data;
using Veldrid;

namespace GeoscientistToolkit.UI.Photogrammetry;

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
    /// <param name="graphicsDevice">The active Veldrid GraphicsDevice.</param>
    /// <param name="imGuiRenderer">The active Veldrid ImGuiRenderer.</param>
    public void StartPhotogrammetry(DatasetGroup imageGroup, GraphicsDevice graphicsDevice, ImGuiRenderer imGuiRenderer)
    {
        // If a wizard is already open, bring its window to the front.
        if (_wizardPanel != null && _wizardPanel.IsOpen)
        {
            ImGuiNET.ImGui.SetWindowFocus(_wizardPanel.Title);
            return;
        }
        
        _wizardPanel = new PhotogrammetryWizardPanel(imageGroup, graphicsDevice, imGuiRenderer);
        _wizardPanel.Open();
    }

    /// <summary>
    /// Renders the wizard panel UI if it is open. This should be called every frame in the main UI loop.
    /// </summary>
    public void SubmitUI()
    {
        _wizardPanel?.Submit();
    }
}