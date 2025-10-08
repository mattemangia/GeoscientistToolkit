// GeoscientistToolkit/Analysis/MaterialManager/PhysicalMaterialAssignmentTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Materials;

/// <summary>
///     A dedicated tool for assigning physical properties from the MaterialLibrary to materials within a CT dataset.
/// </summary>
public class PhysicalMaterialAssignmentTool : IDatasetTools
{
    private int _selectedDatasetMaterialIndex;
    private int _selectedLibraryMaterialIndex;

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ct)
        {
            ImGui.TextDisabled("This tool requires a CT Image Stack dataset.");
            return;
        }

        ImGui.SeparatorText("Physical Property Assignment");
        ImGui.TextWrapped(
            "Assign predefined physical properties from the library to your dataset's materials. This will also update the material's density.");

        var datasetMaterials = ct.Materials.Where(m => m.ID != 0).ToList();
        if (datasetMaterials.Count == 0)
        {
            ImGui.TextDisabled("\nNo materials defined in this dataset yet. Use the Material Manager to add one.");
            return;
        }

        // --- 1. Select a material from the dataset ---
        ImGui.Spacing();
        ImGui.Text("Target Dataset Material:");
        var materialNames = datasetMaterials.Select(m => m.Name).ToArray();

        if (_selectedDatasetMaterialIndex >= materialNames.Length)
            _selectedDatasetMaterialIndex = 0;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##DatasetMaterial", ref _selectedDatasetMaterialIndex, materialNames, materialNames.Length))
        {
            // When selection changes, update the library dropdown to match the current assignment
            var selectedMat = datasetMaterials[_selectedDatasetMaterialIndex];
            UpdateLibrarySelection(selectedMat);
        }

        var targetMaterial = datasetMaterials[_selectedDatasetMaterialIndex];

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- 2. Select a physical material from the library ---
        ImGui.Text("Assign from Library:");
        var libraryMaterials = MaterialLibrary.Instance.Materials.ToList();
        var libraryNames = new List<string> { "None (Custom Properties)" };
        libraryNames.AddRange(libraryMaterials.Select(m => m.Name));

        // Ensure the library selection is up-to-date if it has not been set for the current material
        UpdateLibrarySelection(targetMaterial);

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##LibraryMaterial", ref _selectedLibraryMaterialIndex, libraryNames.ToArray(),
                libraryNames.Count))
            // Assign on change for immediate feedback
            AssignMaterial(ct, targetMaterial, libraryMaterials);

        ImGui.Separator();

        // --- 3. Display current properties ---
        ImGui.Text("Current Assigned Properties:");
        if (string.IsNullOrEmpty(targetMaterial.PhysicalMaterialName))
        {
            ImGui.TextDisabled("None. Using custom properties.");
            ImGui.BulletText($"Density: {targetMaterial.Density:F3} g/cm³ (Editable in Material Manager)");
        }
        else
        {
            var physMat = MaterialLibrary.Instance.Find(targetMaterial.PhysicalMaterialName);
            if (physMat != null)
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), physMat.Name);

                if (physMat.Density_kg_m3.HasValue)
                    ImGui.BulletText($"Density: {physMat.Density_kg_m3:F0} kg/m³");
                if (physMat.YoungModulus_GPa.HasValue)
                    ImGui.BulletText($"Young's Modulus: {physMat.YoungModulus_GPa:F1} GPa");
                if (physMat.PoissonRatio.HasValue)
                    ImGui.BulletText($"Poisson's Ratio: {physMat.PoissonRatio:F3}");
                if (physMat.Vp_m_s.HasValue)
                    ImGui.BulletText($"P-wave Velocity: {physMat.Vp_m_s:F0} m/s");
                if (physMat.Vs_m_s.HasValue)
                    ImGui.BulletText($"S-wave Velocity: {physMat.Vs_m_s:F0} m/s");

                ImGui.Unindent();
            }
            else
            {
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f),
                    $"Warning: Assigned material '{targetMaterial.PhysicalMaterialName}' not found in library.");
            }
        }
    }

    private void AssignMaterial(CtImageStackDataset ct, Material targetMaterial,
        List<PhysicalMaterial> libraryMaterials)
    {
        if (_selectedLibraryMaterialIndex == 0) // "None"
        {
            targetMaterial.PhysicalMaterialName = null;
            Logger.Log($"[PhysMat] Cleared physical material assignment for '{targetMaterial.Name}'.");
        }
        else
        {
            var assignedPhysMat = libraryMaterials[_selectedLibraryMaterialIndex - 1];
            targetMaterial.PhysicalMaterialName = assignedPhysMat.Name;

            // Auto-update density
            if (assignedPhysMat.Density_kg_m3.HasValue)
                targetMaterial.Density = assignedPhysMat.Density_kg_m3.Value / 1000.0; // to g/cm³
            Logger.Log(
                $"[PhysMat] Assigned '{assignedPhysMat.Name}' to '{targetMaterial.Name}'. Density updated to {targetMaterial.Density:F3} g/cm³.");
        }

        ct.SaveMaterials();
    }

    private void UpdateLibrarySelection(Material datasetMaterial)
    {
        if (string.IsNullOrEmpty(datasetMaterial.PhysicalMaterialName))
        {
            _selectedLibraryMaterialIndex = 0;
        }
        else
        {
            var index = MaterialLibrary.Instance.Materials
                .Select((m, i) => new { Material = m, Index = i + 1 })
                .FirstOrDefault(x => string.Equals(x.Material.Name, datasetMaterial.PhysicalMaterialName,
                    StringComparison.OrdinalIgnoreCase))?.Index ?? 0;
            _selectedLibraryMaterialIndex = index;
        }
    }
}