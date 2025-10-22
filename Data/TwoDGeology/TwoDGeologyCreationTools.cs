// GeoscientistToolkit/UI/GIS/TwoDGeologyCreationTools.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
///     Tools for creating and setting up 2D geology profiles from scratch.
///     Provides a complete workflow for building geological cross-sections without field data.
/// </summary>
public class TwoDGeologyCreationTools : IDatasetTools
{
    private readonly ProfileSetupTool _profileSetup = new();
    private readonly StratigraphyBuilderTool _stratigraphyBuilder = new();
    private readonly QuickTemplatesTool _templates = new();
    private readonly TopographyEditorTool _topographyEditor = new();

    public void Draw(Dataset dataset)
    {
        if (dataset is not TwoDGeologyDataset twoDDataset)
        {
            ImGui.TextDisabled("Creation tools are only available for 2D Geology datasets.");
            return;
        }

        if (ImGui.BeginTabBar("CreationToolsTabs"))
        {
            if (ImGui.BeginTabItem("Profile Setup"))
            {
                _profileSetup.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Topography"))
            {
                _topographyEditor.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Stratigraphy"))
            {
                _stratigraphyBuilder.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Templates"))
            {
                _templates.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    #region Profile Setup Tool

    private class ProfileSetupTool
    {
        private float _maxElevation = 1000f;
        private float _minElevation = -2000f;
        private float _profileLength = 10000f;
        private string _profileName = "Cross-Section";
        private float _verticalExaggeration = 2.0f;

        public void Draw(TwoDGeologyDataset dataset)
        {
            ImGui.TextWrapped("Configure the basic parameters of your geological profile.");
            ImGui.Separator();

            ImGui.Text("Profile Name:");
            ImGui.InputText("##ProfileName", ref _profileName, 128);

            ImGui.Spacing();
            ImGui.Text("Profile Length (m):");
            ImGui.SliderFloat("##Length", ref _profileLength, 1000f, 50000f, "%.0f m");

            ImGui.Spacing();
            ImGui.Text("Elevation Range:");
            ImGui.SliderFloat("Maximum (m)##MaxElev", ref _maxElevation, -1000f, 5000f, "%.0f m");
            ImGui.SliderFloat("Minimum (m)##MinElev", ref _minElevation, -10000f, 0f, "%.0f m");

            if (_minElevation >= _maxElevation)
                _minElevation = _maxElevation - 100f;

            ImGui.Spacing();
            ImGui.Text("Vertical Exaggeration:");
            ImGui.SliderFloat("##VE", ref _verticalExaggeration, 1f, 10f, "%.1fx");

            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Apply Configuration", new Vector2(-1, 0))) ApplyConfiguration(dataset);

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Current Configuration:");
            if (dataset.ProfileData?.Profile != null)
            {
                var profile = dataset.ProfileData.Profile;
                ImGui.BulletText($"Length: {profile.TotalDistance:F0} m");
                ImGui.BulletText($"Elevation: {profile.MinElevation:F0} to {profile.MaxElevation:F0} m");
                ImGui.BulletText($"VE: {dataset.ProfileData.VerticalExaggeration:F1}x");
            }
            else
            {
                ImGui.BulletText("No profile configured yet");
            }
        }

        private void ApplyConfiguration(TwoDGeologyDataset dataset)
        {
            if (dataset.ProfileData == null)
            {
                // Create new profile from scratch
                dataset.ProfileData = new GeologicalMapping.CrossSectionGenerator.CrossSection
                {
                    Profile = new GeologicalMapping.ProfileGenerator.TopographicProfile
                    {
                        Name = _profileName,
                        TotalDistance = _profileLength,
                        MinElevation = _minElevation,
                        MaxElevation = _maxElevation,
                        StartPoint = new Vector2(0, 0),
                        EndPoint = new Vector2(_profileLength, 0),
                        CreatedAt = DateTime.Now,
                        VerticalExaggeration = _verticalExaggeration
                    },
                    VerticalExaggeration = _verticalExaggeration
                };

                // Generate default flat topography
                GenerateDefaultTopography(dataset.ProfileData.Profile);

                Logger.Log($"Created new profile: {_profileName}, Length: {_profileLength}m");
            }
            else
            {
                // Update existing profile
                dataset.ProfileData.Profile.Name = _profileName;
                dataset.ProfileData.Profile.TotalDistance = _profileLength;
                dataset.ProfileData.Profile.MinElevation = _minElevation;
                dataset.ProfileData.Profile.MaxElevation = _maxElevation;
                dataset.ProfileData.Profile.EndPoint = new Vector2(_profileLength, 0);
                dataset.ProfileData.VerticalExaggeration = _verticalExaggeration;

                Logger.Log("Updated profile configuration");
            }
        }

        private void GenerateDefaultTopography(GeologicalMapping.ProfileGenerator.TopographicProfile profile)
        {
            profile.Points.Clear();
            var numPoints = 50;
            var meanElevation = (profile.MaxElevation + profile.MinElevation) / 2;

            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
                {
                    Position = new Vector2(distance, meanElevation),
                    Distance = distance,
                    Elevation = meanElevation
                });
            }
        }
    }

    #endregion

    #region Topography Editor Tool

    private class TopographyEditorTool
    {
        private readonly int _selectedPointIndex = -1;
        private float _amplitude = 200f;
        private string _presetName = "Flat";
        private float _wavelength = 2000f;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset.ProfileData?.Profile == null)
            {
                ImGui.TextDisabled("Configure profile setup first.");
                return;
            }

            var profile = dataset.ProfileData.Profile;

            ImGui.TextWrapped("Edit the topographic surface of your profile.");
            ImGui.Separator();

            ImGui.Text($"Points: {profile.Points.Count}");
            ImGui.Spacing();

            // Presets
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Quick Presets:");

            if (ImGui.BeginCombo("##TopoPreset", _presetName))
            {
                if (ImGui.Selectable("Flat", _presetName == "Flat"))
                {
                    _presetName = "Flat";
                    ApplyFlatTopography(profile);
                }

                if (ImGui.Selectable("Gentle Hills", _presetName == "Gentle Hills"))
                {
                    _presetName = "Gentle Hills";
                    ApplyHillyTopography(profile, 100f, 3000f);
                }

                if (ImGui.Selectable("Mountains", _presetName == "Mountains"))
                {
                    _presetName = "Mountains";
                    ApplyHillyTopography(profile, 400f, 2000f);
                }

                if (ImGui.Selectable("Valley", _presetName == "Valley"))
                {
                    _presetName = "Valley";
                    ApplyValleyTopography(profile);
                }

                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Custom sine wave generator
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Custom Sine Wave:");

            ImGui.Text("Amplitude (m):");
            ImGui.SliderFloat("##Amplitude", ref _amplitude, 50f, 1000f, "%.0f m");

            ImGui.Text("Wavelength (m):");
            ImGui.SliderFloat("##Wavelength", ref _wavelength, 500f, 10000f, "%.0f m");

            if (ImGui.Button("Apply Sine Wave", new Vector2(-1, 0)))
                ApplyHillyTopography(profile, _amplitude, _wavelength);

            ImGui.Separator();

            // Manual editing info
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Manual Editing:");
            ImGui.TextWrapped("Use the viewer to click and drag topography points.");

            if (_selectedPointIndex >= 0 && _selectedPointIndex < profile.Points.Count)
            {
                var point = profile.Points[_selectedPointIndex];
                ImGui.Text($"Selected Point {_selectedPointIndex}:");
                ImGui.BulletText($"Distance: {point.Distance:F1} m");

                var elevation = point.Elevation;
                if (ImGui.SliderFloat("Elevation##SelectedPoint", ref elevation,
                        profile.MinElevation, profile.MaxElevation, "%.1f m"))
                {
                    point.Elevation = elevation;
                    point.Position = new Vector2(point.Distance, elevation);
                    profile.Points[_selectedPointIndex] = point;
                }
            }
        }

        private void ApplyFlatTopography(GeologicalMapping.ProfileGenerator.TopographicProfile profile)
        {
            var meanElevation = (profile.MaxElevation + profile.MinElevation) / 2;
            for (var i = 0; i < profile.Points.Count; i++)
            {
                var point = profile.Points[i];
                point.Elevation = meanElevation;
                point.Position = new Vector2(point.Distance, meanElevation);
                profile.Points[i] = point;
            }

            Logger.Log("Applied flat topography");
        }

        private void ApplyHillyTopography(GeologicalMapping.ProfileGenerator.TopographicProfile profile,
            float amplitude, float wavelength)
        {
            var meanElevation = (profile.MaxElevation + profile.MinElevation) / 2;
            for (var i = 0; i < profile.Points.Count; i++)
            {
                var point = profile.Points[i];
                var phase = 2f * MathF.PI * point.Distance / wavelength;
                var elevation = meanElevation + amplitude * MathF.Sin(phase);

                point.Elevation = elevation;
                point.Position = new Vector2(point.Distance, elevation);
                profile.Points[i] = point;
            }

            Logger.Log($"Applied hilly topography: amplitude={amplitude}m, wavelength={wavelength}m");
        }

        private void ApplyValleyTopography(GeologicalMapping.ProfileGenerator.TopographicProfile profile)
        {
            var meanElevation = (profile.MaxElevation + profile.MinElevation) / 2;
            var centerX = profile.TotalDistance / 2;

            for (var i = 0; i < profile.Points.Count; i++)
            {
                var point = profile.Points[i];
                var distFromCenter = Math.Abs(point.Distance - centerX);
                var normalized = distFromCenter / (profile.TotalDistance / 2);
                var elevation = meanElevation - 300f * (1 - normalized);

                point.Elevation = elevation;
                point.Position = new Vector2(point.Distance, elevation);
                profile.Points[i] = point;
            }

            Logger.Log("Applied valley topography");
        }
    }

    #endregion

    #region Stratigraphy Builder Tool

    private class StratigraphyBuilderTool
    {
        private float _basementDepth = 3000f;
        private Vector4 _newLayerColor = new(0.8f, 0.7f, 0.5f, 1f);
        private string _newLayerName = "New Formation";
        private float _newLayerThickness = 200f;
        private int _selectedLayerIndex = -1;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset.ProfileData?.Profile == null)
            {
                ImGui.TextDisabled("Configure profile setup first.");
                return;
            }

            var section = dataset.ProfileData;

            ImGui.TextWrapped("Build your stratigraphic column by adding horizontal layers.");
            ImGui.Separator();

            // Basement depth
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Basement Configuration:");
            ImGui.Text("Basement Depth (m below sea level):");
            if (ImGui.SliderFloat("##BasementDepth", ref _basementDepth, 1000f, 10000f, "%.0f m"))
                UpdateBasement(section);

            if (ImGui.Button("Set Basement", new Vector2(-1, 0))) UpdateBasement(section);

            ImGui.Separator();

            // Layer list
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Stratigraphic Layers:");
            ImGui.Text($"Layers: {section.Formations.Count}");
            ImGui.Spacing();

            // Display existing layers (top to bottom)
            var nonBasementLayers = section.Formations.Where(f => f.Name != "Basement").ToList();
            for (var i = nonBasementLayers.Count - 1; i >= 0; i--)
            {
                var formation = nonBasementLayers[i];
                var actualIndex = section.Formations.IndexOf(formation);
                var isSelected = actualIndex == _selectedLayerIndex;

                if (isSelected)
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.6f, 1f, 1f));

                var buttonLabel = $"{formation.Name}##Layer{actualIndex}";
                if (ImGui.Button(buttonLabel, new Vector2(-40, 0))) _selectedLayerIndex = actualIndex;

                if (isSelected)
                    ImGui.PopStyleColor();

                ImGui.SameLine();
                if (ImGui.Button($"X##Del{actualIndex}", new Vector2(30, 0)))
                {
                    section.Formations.RemoveAt(actualIndex);
                    _selectedLayerIndex = -1;
                    Logger.Log($"Removed formation: {formation.Name}");
                }

                // Show color swatch
                var colorButtonSize = new Vector2(20, 20);
                ImGui.SameLine();
                ImGui.ColorButton($"##Color{actualIndex}", formation.Color, ImGuiColorEditFlags.NoTooltip,
                    colorButtonSize);
            }

            ImGui.Separator();

            // Add new layer
            ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "Add New Layer:");

            ImGui.Text("Name:");
            ImGui.InputText("##NewLayerName", ref _newLayerName, 64);

            ImGui.Text("Thickness (m):");
            ImGui.SliderFloat("##NewLayerThickness", ref _newLayerThickness, 50f, 1000f, "%.0f m");

            ImGui.Text("Color:");
            ImGui.ColorEdit4("##NewLayerColor", ref _newLayerColor);

            if (ImGui.Button("Add Layer to Top", new Vector2(-1, 0))) AddLayer(section, true);

            if (ImGui.Button("Add Layer to Bottom", new Vector2(-1, 0))) AddLayer(section, false);

            ImGui.Separator();

            // Quick stack generator
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Quick Layer Stack:");

            if (ImGui.Button("Generate Standard Stack (5 layers)", new Vector2(-1, 0))) GenerateStandardStack(section);

            if (ImGui.Button("Clear All Layers", new Vector2(-1, 0)))
            {
                section.Formations.Clear();
                _selectedLayerIndex = -1;
                Logger.Log("Cleared all formations");
            }
        }

        private void UpdateBasement(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            var profile = section.Profile;
            var topElevation = -100f; // Start basement 100m below sea level
            var bottomElevation = -_basementDepth;

            // Create or update basement formation
            var basement = section.Formations.FirstOrDefault(f => f.Name == "Basement");
            if (basement == null)
            {
                basement = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
                {
                    Name = "Basement",
                    Color = new Vector4(0.6f, 0.3f, 0.3f, 1f)
                };
                section.Formations.Insert(0, basement); // Add at bottom
            }

            // Generate basement boundaries
            basement.TopBoundary.Clear();
            basement.BottomBoundary.Clear();

            var numPoints = 20;
            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                basement.TopBoundary.Add(new Vector2(distance, topElevation));
                basement.BottomBoundary.Add(new Vector2(distance, bottomElevation));
            }

            Logger.Log($"Set basement depth to {_basementDepth}m");
        }

        private void AddLayer(GeologicalMapping.CrossSectionGenerator.CrossSection section, bool toTop)
        {
            var profile = section.Profile;
            var newFormation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = _newLayerName,
                Color = _newLayerColor,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            // Determine elevation
            float topElevation, bottomElevation;

            var nonBasementFormations = section.Formations.Where(f => f.Name != "Basement").ToList();

            if (toTop)
            {
                // Add above existing layers
                var topPoints = nonBasementFormations.SelectMany(f => f.TopBoundary);
                if (topPoints.Any())
                    topElevation = topPoints.Max(p => p.Y);
                else
                    topElevation = 0f;
                bottomElevation = topElevation - _newLayerThickness;
            }
            else
            {
                // Add below existing layers (above basement)
                var bottomPoints = nonBasementFormations.SelectMany(f => f.BottomBoundary);
                if (bottomPoints.Any())
                    bottomElevation = bottomPoints.Min(p => p.Y);
                else
                    bottomElevation = -500f;
                topElevation = bottomElevation + _newLayerThickness;
            }

            // Generate horizontal boundaries
            var numPoints = 20;
            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                newFormation.TopBoundary.Add(new Vector2(distance, topElevation));
                newFormation.BottomBoundary.Add(new Vector2(distance, bottomElevation));
            }

            section.Formations.Add(newFormation);
            Logger.Log($"Added formation: {_newLayerName}, thickness: {_newLayerThickness}m");
        }

        private void GenerateStandardStack(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();

            var layers = new[]
            {
                ("Quaternary", 100f, new Vector4(0.95f, 0.9f, 0.7f, 1f)),
                ("Upper Sandstone", 300f, new Vector4(0.9f, 0.8f, 0.5f, 1f)),
                ("Shale Unit", 200f, new Vector4(0.7f, 0.7f, 0.6f, 1f)),
                ("Lower Sandstone", 400f, new Vector4(0.85f, 0.75f, 0.45f, 1f)),
                ("Limestone", 500f, new Vector4(0.6f, 0.8f, 0.9f, 1f))
            };

            var profile = section.Profile;
            var currentTop = 0f;

            foreach (var (name, thickness, color) in layers)
            {
                var formation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
                {
                    Name = name,
                    Color = color,
                    TopBoundary = new List<Vector2>(),
                    BottomBoundary = new List<Vector2>()
                };

                var numPoints = 20;
                for (var i = 0; i <= numPoints; i++)
                {
                    var distance = i / (float)numPoints * profile.TotalDistance;
                    formation.TopBoundary.Add(new Vector2(distance, currentTop));
                    formation.BottomBoundary.Add(new Vector2(distance, currentTop - thickness));
                }

                section.Formations.Add(formation);
                currentTop -= thickness;
            }

            Logger.Log("Generated standard 5-layer stack");
        }
    }

    #endregion

    #region Quick Templates Tool

    private class QuickTemplatesTool
    {
        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset.ProfileData?.Profile == null)
            {
                ImGui.TextDisabled("Configure profile setup first.");
                return;
            }

            ImGui.TextWrapped("Apply pre-configured geological scenarios to quickly build complete profiles.");
            ImGui.Separator();

            if (ImGui.Button("Flat-Lying Sediments", new Vector2(-1, 0))) ApplyFlatTemplate(dataset.ProfileData);
            ImGui.TextWrapped("→ Simple horizontal stratigraphy with no deformation");
            ImGui.Spacing();

            if (ImGui.Button("Gentle Monocline", new Vector2(-1, 0))) ApplyMonoclineTemplate(dataset.ProfileData);
            ImGui.TextWrapped("→ Layers dipping gently to one side");
            ImGui.Spacing();

            if (ImGui.Button("Simple Anticline", new Vector2(-1, 0))) ApplyAnticlineTemplate(dataset.ProfileData);
            ImGui.TextWrapped("→ Symmetrical upward fold");
            ImGui.Spacing();

            if (ImGui.Button("Normal Fault Block", new Vector2(-1, 0))) ApplyNormalFaultTemplate(dataset.ProfileData);
            ImGui.TextWrapped("→ Extensional faulting with downthrown block");
            ImGui.Spacing();

            if (ImGui.Button("Thrust Fault System", new Vector2(-1, 0))) ApplyThrustTemplate(dataset.ProfileData);
            ImGui.TextWrapped("→ Compressional tectonics with overthrust");
            ImGui.Spacing();

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "Note:");
            ImGui.TextWrapped(
                "Templates will replace current formations and faults. Use as starting points and modify as needed.");
        }

        private void ApplyFlatTemplate(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();
            section.Faults.Clear();

            // Create 4 flat layers
            var layers = new[]
            {
                ("Sandstone", 0f, -300f, new Vector4(0.9f, 0.8f, 0.5f, 1f)),
                ("Shale", -300f, -500f, new Vector4(0.7f, 0.7f, 0.6f, 1f)),
                ("Limestone", -500f, -900f, new Vector4(0.6f, 0.8f, 0.9f, 1f)),
                ("Basement", -900f, -3000f, new Vector4(0.6f, 0.3f, 0.3f, 1f))
            };

            foreach (var (name, top, bottom, color) in layers)
                AddHorizontalFormation(section, name, top, bottom, color);

            Logger.Log("Applied flat-lying sediments template");
        }

        private void ApplyMonoclineTemplate(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();
            section.Faults.Clear();

            var dipAngle = 10f * MathF.PI / 180f; // 10 degree dip
            var layers = new[]
            {
                ("Sandstone", 0f, 300f, new Vector4(0.9f, 0.8f, 0.5f, 1f)),
                ("Shale", 300f, 200f, new Vector4(0.7f, 0.7f, 0.6f, 1f)),
                ("Limestone", 200f, 400f, new Vector4(0.6f, 0.8f, 0.9f, 1f))
            };

            foreach (var (name, topZ, thickness, color) in layers)
                AddDippingFormation(section, name, topZ, thickness, dipAngle, color);

            Logger.Log("Applied monocline template");
        }

        private void ApplyAnticlineTemplate(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();
            section.Faults.Clear();

            var profile = section.Profile;
            var centerX = profile.TotalDistance / 2;
            var amplitude = 400f;
            var wavelength = profile.TotalDistance;

            var layers = new[]
            {
                ("Upper Unit", 200f, new Vector4(0.9f, 0.8f, 0.5f, 1f)),
                ("Middle Unit", 300f, new Vector4(0.7f, 0.7f, 0.6f, 1f)),
                ("Lower Unit", 400f, new Vector4(0.6f, 0.8f, 0.9f, 1f))
            };

            var cumulativeDepth = 0f;
            foreach (var (name, thickness, color) in layers)
            {
                AddFoldedFormation(section, name, cumulativeDepth, thickness, amplitude, wavelength, centerX, color);
                cumulativeDepth += thickness;
            }

            Logger.Log("Applied anticline template");
        }

        private void ApplyNormalFaultTemplate(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();
            section.Faults.Clear();

            // Create flat layers
            ApplyFlatTemplate(section);

            // Add normal fault
            var profile = section.Profile;
            var faultX = profile.TotalDistance * 0.4f;
            var displacement = 200f;

            var fault = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
            {
                Type = GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                Dip = 60f,
                Displacement = displacement,
                FaultTrace = new List<Vector2>
                {
                    new(faultX, 0),
                    new(faultX - 1000, -1732f) // 60 degree dip
                }
            };

            section.Faults.Add(fault);

            // Offset hanging wall
            foreach (var formation in section.Formations)
            {
                for (var i = 0; i < formation.TopBoundary.Count; i++)
                    if (formation.TopBoundary[i].X > faultX)
                        formation.TopBoundary[i] -= new Vector2(0, displacement);
                for (var i = 0; i < formation.BottomBoundary.Count; i++)
                    if (formation.BottomBoundary[i].X > faultX)
                        formation.BottomBoundary[i] -= new Vector2(0, displacement);
            }

            Logger.Log("Applied normal fault template");
        }

        private void ApplyThrustTemplate(GeologicalMapping.CrossSectionGenerator.CrossSection section)
        {
            section.Formations.Clear();
            section.Faults.Clear();

            // Create flat layers
            ApplyFlatTemplate(section);

            // Add thrust fault
            var profile = section.Profile;
            var faultX = profile.TotalDistance * 0.6f;
            var displacement = 1500f;

            var fault = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
            {
                Type = GeologicalMapping.GeologicalFeatureType.Fault_Thrust,
                Dip = 30f,
                Displacement = displacement,
                FaultTrace = new List<Vector2>
                {
                    new(faultX, 0),
                    new(faultX - 2600f, -1500f) // 30 degree ramp
                }
            };

            section.Faults.Add(fault);

            // Offset hanging wall (left side moves over right)
            foreach (var formation in section.Formations)
            {
                for (var i = 0; i < formation.TopBoundary.Count; i++)
                    if (formation.TopBoundary[i].X < faultX)
                        formation.TopBoundary[i] += new Vector2(displacement, 0);
                for (var i = 0; i < formation.BottomBoundary.Count; i++)
                    if (formation.BottomBoundary[i].X < faultX)
                        formation.BottomBoundary[i] += new Vector2(displacement, 0);
            }

            Logger.Log("Applied thrust fault template");
        }

        private void AddHorizontalFormation(GeologicalMapping.CrossSectionGenerator.CrossSection section,
            string name, float topElev, float bottomElev, Vector4 color)
        {
            var formation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = name,
                Color = color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            var numPoints = 20;
            var profile = section.Profile;
            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                formation.TopBoundary.Add(new Vector2(distance, topElev));
                formation.BottomBoundary.Add(new Vector2(distance, bottomElev));
            }

            section.Formations.Add(formation);
        }

        private void AddDippingFormation(GeologicalMapping.CrossSectionGenerator.CrossSection section,
            string name, float startTopElev, float thickness, float dipAngle, Vector4 color)
        {
            var formation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = name,
                Color = color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            var numPoints = 20;
            var profile = section.Profile;
            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                var topElev = startTopElev - distance * MathF.Tan(dipAngle);
                var bottomElev = topElev - thickness;

                formation.TopBoundary.Add(new Vector2(distance, topElev));
                formation.BottomBoundary.Add(new Vector2(distance, bottomElev));
            }

            section.Formations.Add(formation);
        }

        private void AddFoldedFormation(GeologicalMapping.CrossSectionGenerator.CrossSection section,
            string name, float baseDepth, float thickness, float amplitude, float wavelength, float centerX,
            Vector4 color)
        {
            var formation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = name,
                Color = color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            var numPoints = 40;
            var profile = section.Profile;
            for (var i = 0; i <= numPoints; i++)
            {
                var distance = i / (float)numPoints * profile.TotalDistance;
                var phase = 2f * MathF.PI * (distance - centerX) / wavelength;
                var foldOffset = amplitude * MathF.Sin(phase);

                var topElev = -baseDepth + foldOffset;
                var bottomElev = topElev - thickness;

                formation.TopBoundary.Add(new Vector2(distance, topElev));
                formation.BottomBoundary.Add(new Vector2(distance, bottomElev));
            }

            section.Formations.Add(formation);
        }
    }

    #endregion
}