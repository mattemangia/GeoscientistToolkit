// GAIA/UI/GIS/TwoDGeologyProperties.cs

using System.Numerics;
using GAIA.Data;
using GAIA.Data.TwoDGeology;
using GAIA.UI.Interfaces;
using ImGuiNET;

namespace GAIA.UI.GIS;

public class TwoDGeologyProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not TwoDGeologyDataset profileDataset)
            return;

        profileDataset.Load(); // Ensure data is loaded
        var profileData = profileDataset.ProfileData;

        ImGui.Text("Type: 2D Geology Profile");

        if (profileData == null)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Data could not be loaded.");
            return;
        }

        ImGui.Separator();
        ImGui.Text($"Profile Length: {profileData.Profile.TotalDistance:F1} m");
        ImGui.Text(
            $"Elevation Range: {profileData.Profile.MinElevation:F1} to {profileData.Profile.MaxElevation:F1} m");
        ImGui.Separator();
        ImGui.Text($"Formations: {profileData.Formations.Count}");
        ImGui.Text($"Faults: {profileData.Faults.Count}");
    }
}