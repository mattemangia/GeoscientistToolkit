// GeoscientistToolkit/UI/GIS/TwoDGeologyEditorTools.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
///     Advanced editing tools for 2D geological cross-sections.
///     Now properly synchronized with the viewer.
/// </summary>
public class TwoDGeologyEditorTools : IDatasetTools
{
    private readonly FaultEditor _faultEditor = new();
    private readonly LayerManipulator _layerManipulator = new();
    private readonly StructuralElementCreator _structuralCreator = new();
    private readonly StructuralRestorationTool _restorationTool = new();

    public void Draw(Dataset dataset)
    {
        if (dataset is not TwoDGeologyDataset twoDDataset)
        {
            ImGui.TextDisabled("Tools are only available for 2D Geology datasets.");
            return;
        }

        if (ImGui.BeginTabBar("EditorToolsTabs"))
        {
            if (ImGui.BeginTabItem("Layer Tools"))
            {
                _layerManipulator.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Fault Editor"))
            {
                _faultEditor.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Create Structures"))
            {
                _structuralCreator.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Structural Restoration"))
            {
                _restorationTool.Draw(twoDDataset);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    #region Layer Manipulator

    private class LayerManipulator
    {
        private float _horizontalOffset;
        private float _rotationAngle;
        private Vector2 _rotationPivot = Vector2.Zero;
        private int _selectedFormationIndex = -1;
        private float _thicknessScale = 1.0f;
        private float _verticalOffset;
        
        // Store original state for undo
        private List<Vector2> _originalTopBoundary;
        private List<Vector2> _originalBottomBoundary;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null) return;

            ImGui.TextWrapped("Select and manipulate entire formations as units.");
            ImGui.Separator();

            var formations = dataset.ProfileData.Formations;
            if (formations.Count == 0)
            {
                ImGui.TextDisabled("No formations available.");
                return;
            }

            // Formation selection
            ImGui.Text("Select Formation:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FormationSelect",
                    _selectedFormationIndex >= 0 && _selectedFormationIndex < formations.Count
                        ? formations[_selectedFormationIndex].Name
                        : "None"))
            {
                for (var i = 0; i < formations.Count; i++)
                    if (ImGui.Selectable(formations[i].Name, _selectedFormationIndex == i))
                    {
                        _selectedFormationIndex = i;
                        ResetTransforms();
                        // Store original state
                        var formation = formations[i];
                        _originalTopBoundary = new List<Vector2>(formation.TopBoundary);
                        _originalBottomBoundary = new List<Vector2>(formation.BottomBoundary);
                        
                        // Sync with viewer
                        dataset.GetViewer()?.Tools.SetSelectedFormation(formation);
                    }

                ImGui.EndCombo();
            }

            if (_selectedFormationIndex < 0 || _selectedFormationIndex >= formations.Count)
            {
                ImGui.TextDisabled("Select a formation to edit.");
                return;
            }

            var selectedFormation = formations[_selectedFormationIndex];

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "Layer Transformations");
            
            // Check for overlap and show warning
            bool hasOverlap = GeologicalConstraints.DoesFormationOverlapAny(selectedFormation,
                formations.Where(f => f != selectedFormation).ToList());
            
            if (hasOverlap)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.TextWrapped("⚠ WARNING: Formation currently overlaps with another!");
                ImGui.PopStyleColor();
                ImGui.Separator();
            };

            // Vertical offset
            ImGui.Text("Vertical Offset:");
            if (ImGui.SliderFloat("##VertOffset", ref _verticalOffset, -500f, 500f, "%.1f m"))
            {
                ApplyVerticalOffset(selectedFormation, _verticalOffset);
                dataset.MarkAsModified();
            }

            // Horizontal offset
            ImGui.Text("Horizontal Offset:");
            if (ImGui.SliderFloat("##HorizOffset", ref _horizontalOffset, -1000f, 1000f, "%.1f m"))
            {
                ApplyHorizontalOffset(selectedFormation, _horizontalOffset);
                dataset.MarkAsModified();
            }

            ImGui.Separator();

            // Rotation
            ImGui.Text("Rotation (Dip Change):");
            if (ImGui.SliderFloat("##Rotation", ref _rotationAngle, -45f, 45f, "%.1f°"))
            {
                ApplyRotation(selectedFormation, _rotationAngle, _rotationPivot);
                dataset.MarkAsModified();
            }

            ImGui.Text("Pivot Point:");
            ImGui.InputFloat2("##Pivot", ref _rotationPivot);
            if (ImGui.Button("Set Pivot to Center", new Vector2(-1, 0)))
                _rotationPivot = CalculateFormationCenter(selectedFormation);

            ImGui.Separator();

            // Thickness adjustment
            ImGui.Text("Thickness Scale:");
            if (ImGui.SliderFloat("##ThicknessScale", ref _thicknessScale, 0.1f, 3.0f, "%.2fx"))
            {
                ApplyThicknessScale(selectedFormation, _thicknessScale);
                dataset.MarkAsModified();
            }

            ImGui.Separator();

            // Action buttons
            if (ImGui.Button("Apply and Reset", new Vector2(-1, 0)))
            {
                // Check for overlaps before applying
                if (GeologicalConstraints.DoesFormationOverlapAny(selectedFormation, 
                    formations.Where(f => f != selectedFormation).ToList()))
                {
                    Logger.LogWarning("Cannot apply: Transformation would create overlaps");
                    // Revert to original
                    if (_originalTopBoundary != null && _originalBottomBoundary != null)
                    {
                        selectedFormation.TopBoundary = new List<Vector2>(_originalTopBoundary);
                        selectedFormation.BottomBoundary = new List<Vector2>(_originalBottomBoundary);
                    }
                }
                else
                {
                    ResetTransforms();
                    _originalTopBoundary = new List<Vector2>(selectedFormation.TopBoundary);
                    _originalBottomBoundary = new List<Vector2>(selectedFormation.BottomBoundary);
                    Logger.Log("Transformations applied successfully");
                }
            }
            
            if (ImGui.Button("Revert to Original", new Vector2(-1, 0)))
            {
                if (_originalTopBoundary != null && _originalBottomBoundary != null)
                {
                    selectedFormation.TopBoundary = new List<Vector2>(_originalTopBoundary);
                    selectedFormation.BottomBoundary = new List<Vector2>(_originalBottomBoundary);
                    ResetTransforms();
                    dataset.MarkAsModified();
                }
            }

            if (ImGui.Button("Duplicate Formation", new Vector2(-1, 0)))
            {
                DuplicateFormation(dataset, selectedFormation);
            }

            if (ImGui.Button("Delete Formation", new Vector2(-1, 0)))
            {
                DeleteFormation(dataset, _selectedFormationIndex);
                _selectedFormationIndex = -1;
            }
        }

        private void ResetTransforms()
        {
            _verticalOffset = 0f;
            _horizontalOffset = 0f;
            _rotationAngle = 0f;
            _thicknessScale = 1.0f;
        }

        private void ApplyVerticalOffset(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation, float offset)
        {
            if (_originalTopBoundary == null || _originalBottomBoundary == null) return;
            
            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            foreach (var point in _originalTopBoundary)
                formation.TopBoundary.Add(new Vector2(point.X, point.Y + offset));

            foreach (var point in _originalBottomBoundary)
                formation.BottomBoundary.Add(new Vector2(point.X, point.Y + offset));
        }

        private void ApplyHorizontalOffset(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation, float offset)
        {
            if (_originalTopBoundary == null || _originalBottomBoundary == null) return;
            
            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            foreach (var point in _originalTopBoundary)
                formation.TopBoundary.Add(new Vector2(point.X + offset, point.Y));

            foreach (var point in _originalBottomBoundary)
                formation.BottomBoundary.Add(new Vector2(point.X + offset, point.Y));
        }

        private void ApplyRotation(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float angleDegrees, Vector2 pivot)
        {
            if (_originalTopBoundary == null || _originalBottomBoundary == null) return;
            
            var angleRad = angleDegrees * MathF.PI / 180f;
            var rotation = Matrix3x2.CreateRotation(angleRad, pivot);

            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            foreach (var point in _originalTopBoundary)
                formation.TopBoundary.Add(Vector2.Transform(point, rotation));

            foreach (var point in _originalBottomBoundary)
                formation.BottomBoundary.Add(Vector2.Transform(point, rotation));
        }

        private void ApplyThicknessScale(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation, float scale)
        {
            if (_originalTopBoundary == null || _originalBottomBoundary == null) return;
            
            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            // Copy original top
            foreach (var point in _originalTopBoundary)
                formation.TopBoundary.Add(point);

            // Scale bottom relative to top
            for (var i = 0; i < Math.Min(_originalTopBoundary.Count, _originalBottomBoundary.Count); i++)
            {
                var top = _originalTopBoundary[i];
                var bottom = _originalBottomBoundary[i];
                var diff = bottom - top;
                var scaled = top + diff * scale;
                formation.BottomBoundary.Add(scaled);
            }
        }

        private Vector2 CalculateFormationCenter(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
        {
            if (formation.TopBoundary.Count == 0) return Vector2.Zero;

            var avgX = formation.TopBoundary.Average(p => p.X);
            var avgTopY = formation.TopBoundary.Average(p => p.Y);
            var avgBottomY = formation.BottomBoundary.Average(p => p.Y);
            var avgY = (avgTopY + avgBottomY) / 2;

            return new Vector2(avgX, avgY);
        }

        private void DuplicateFormation(TwoDGeologyDataset dataset,
            GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
        {
            var duplicate = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = formation.Name + " (Copy)",
                Color = formation.Color,
                TopBoundary = new List<Vector2>(formation.TopBoundary),
                BottomBoundary = new List<Vector2>(formation.BottomBoundary),
                FoldStyle = formation.FoldStyle
            };

            // Offset slightly
            for (var i = 0; i < duplicate.TopBoundary.Count; i++)
                duplicate.TopBoundary[i] += new Vector2(0, 100f);

            for (var i = 0; i < duplicate.BottomBoundary.Count; i++)
                duplicate.BottomBoundary[i] += new Vector2(0, 100f);

            dataset.ProfileData.Formations.Add(duplicate);
            dataset.MarkAsModified();
            Logger.Log($"Duplicated formation '{formation.Name}'");
        }

        private void DeleteFormation(TwoDGeologyDataset dataset, int index)
        {
            if (index >= 0 && index < dataset.ProfileData.Formations.Count)
            {
                var name = dataset.ProfileData.Formations[index].Name;
                dataset.ProfileData.Formations.RemoveAt(index);
                dataset.MarkAsModified();
                
                // Sync with viewer
                dataset.GetViewer()?.Tools.ClearSelection();
                
                Logger.Log($"Deleted formation '{name}'");
            }
        }
    }

    #endregion

    #region Fault Editor

    private class FaultEditor
    {
        private int _selectedFaultIndex = -1;
        private float _dipAngle = 60f;
        private float _displacement = 100f;
        private string _dipDirection = "E";

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null) return;

            ImGui.TextWrapped("Edit existing faults or create new ones with finite extent.");
            ImGui.Separator();

            var faults = dataset.ProfileData.Faults;

            // Fault selection
            ImGui.Text("Select Fault:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FaultSelect",
                    _selectedFaultIndex >= 0 && _selectedFaultIndex < faults.Count
                        ? $"{faults[_selectedFaultIndex].Type.ToString().Replace("Fault_", "")} Fault"
                        : "None"))
            {
                for (var i = 0; i < faults.Count; i++)
                {
                    var fault = faults[i];
                    string label = $"{fault.Type.ToString().Replace("Fault_", "")} - Dip: {fault.Dip:F0}°";
                    if (ImGui.Selectable(label, _selectedFaultIndex == i))
                    {
                        _selectedFaultIndex = i;
                        _dipAngle = fault.Dip;
                        _displacement = fault.Displacement ?? 0f;
                        _dipDirection = fault.DipDirection ?? "E";
                        
                        // Sync with viewer
                        dataset.GetViewer()?.Tools.SetSelectedFault(fault);
                    }
                }

                ImGui.EndCombo();
            }

            if (_selectedFaultIndex >= 0 && _selectedFaultIndex < faults.Count)
            {
                var selectedFault = faults[_selectedFaultIndex];

                ImGui.Separator();
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.7f, 1f), "Fault Properties");

                // Dip angle
                ImGui.Text("Dip Angle:");
                if (ImGui.SliderFloat("##Dip", ref _dipAngle, 0f, 90f, "%.1f°"))
                {
                    selectedFault.Dip = _dipAngle;
                    UpdateFaultGeometry(selectedFault);
                    dataset.MarkAsModified();
                }

                // Dip direction
                ImGui.Text("Dip Direction:");
                ImGui.InputText("##DipDir", ref _dipDirection, 64);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    selectedFault.DipDirection = _dipDirection;
                    dataset.MarkAsModified();
                }

                // Displacement
                ImGui.Text("Displacement:");
                if (ImGui.InputFloat("##Displacement", ref _displacement))
                {
                    selectedFault.Displacement = _displacement;
                    
                    // Apply displacement to formations following geological constraints
                    if (_displacement != 0 && selectedFault.FaultTrace.Count >= 2)
                    {
                        GeologicalConstraints.ApplyFaultDisplacement(
                            selectedFault,
                            dataset.ProfileData.Formations,
                            _displacement
                        );
                    }
                    
                    dataset.MarkAsModified();
                }

                // Fault type
                ImGui.Text("Fault Type:");
                var currentType = selectedFault.Type;
                if (ImGui.BeginCombo("##FaultType", currentType.ToString().Replace("Fault_", "")))
                {
                    foreach (var type in Enum.GetValues<GeologicalMapping.GeologicalFeatureType>()
                        .Where(t => t.ToString().StartsWith("Fault_")))
                    {
                        if (ImGui.Selectable(type.ToString().Replace("Fault_", ""), currentType == type))
                        {
                            selectedFault.Type = type;
                            dataset.MarkAsModified();
                        }
                    }
                    ImGui.EndCombo();
                }

                ImGui.Separator();

                // Info
                ImGui.Text($"Vertices: {selectedFault.FaultTrace.Count}");
                ImGui.TextWrapped("Use the viewer to add/remove vertices by right-clicking on the fault.");

                ImGui.Separator();

                if (ImGui.Button("Delete Fault", new Vector2(-1, 0)))
                {
                    dataset.ProfileData.Faults.RemoveAt(_selectedFaultIndex);
                    _selectedFaultIndex = -1;
                    dataset.MarkAsModified();
                    
                    // Sync with viewer
                    dataset.GetViewer()?.Tools.ClearSelection();
                    
                    Logger.Log("Deleted fault");
                }
            }
            else
            {
                ImGui.TextDisabled("Select a fault to edit, or draw one in the viewer.");
            }
        }

        private void UpdateFaultGeometry(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            // Update the fault trace based on the new dip angle
            // Keep the start point, recalculate the end point
            if (fault.FaultTrace.Count >= 2)
            {
                var start = fault.FaultTrace[0];
                var length = Vector2.Distance(start, fault.FaultTrace[^1]);
                
                var dipRad = fault.Dip * MathF.PI / 180f;
                var dx = length * MathF.Cos(dipRad);
                var dy = length * MathF.Sin(dipRad);
                
                // Assuming dipping to the right for simplicity
                var end = new Vector2(start.X + dx, start.Y - dy);
                fault.FaultTrace[^1] = end;
            }
        }
    }

    #endregion

    #region Structural Element Creator

    private class StructuralElementCreator
    {
        private StructureType _structureType = StructureType.Graben;
        private float _positionX = 5000f;
        private float _topElevation = 0f;
        private float _structureWidth = 2000f;
        private float _structureDepth = 1500f;
        private float _faultDip = 60f;
        private float _displacement = 200f;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null) return;

            ImGui.TextWrapped("Create complete structural features like grabens, horsts, and thrust systems.");
            ImGui.Separator();

            // Structure type
            ImGui.Text("Structure Type:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##StructType", _structureType.ToString()))
            {
                foreach (var type in Enum.GetValues<StructureType>())
                {
                    if (ImGui.Selectable(type.ToString(), _structureType == type))
                    {
                        _structureType = type;
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Parameters
            ImGui.Text("Position along profile:");
            ImGui.SliderFloat("##PosX", ref _positionX, 0f, dataset.ProfileData.Profile.TotalDistance, "%.0f m");

            ImGui.Text("Top Elevation:");
            ImGui.InputFloat("##TopElev", ref _topElevation);

            ImGui.Text("Structure Width:");
            ImGui.SliderFloat("##Width", ref _structureWidth, 500f, 5000f, "%.0f m");

            ImGui.Text("Structure Depth:");
            ImGui.SliderFloat("##Depth", ref _structureDepth, 500f, 3000f, "%.0f m");

            if (_structureType != StructureType.FoldPair)
            {
                ImGui.Text("Fault Dip:");
                ImGui.SliderFloat("##FDip", ref _faultDip, 30f, 90f, "%.0f°");
            }

            ImGui.Text("Displacement/Amplitude:");
            ImGui.InputFloat("##Disp", ref _displacement);

            ImGui.Separator();

            // Create button
            if (ImGui.Button("Create Structure", new Vector2(-1, 0)))
            {
                CreateStructure(dataset);
            }

            // Description
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), GetStructureDescription(_structureType));
        }

        private string GetStructureDescription(StructureType type) => type switch
        {
            StructureType.Graben => "Down-dropped block between two normal faults dipping towards each other",
            StructureType.Horst => "Up-thrown block between two normal faults dipping away from each other",
            StructureType.ThrustSystem => "Imbricate thrust faults with decreasing displacement",
            StructureType.FoldPair => "Anticline-syncline pair by folding existing formations",
            StructureType.Detachment => "Low-angle extensional detachment fault",
            _ => ""
        };

        private void CreateStructure(TwoDGeologyDataset dataset)
        {
            switch (_structureType)
            {
                case StructureType.Graben:
                    CreateGraben(dataset);
                    break;
                case StructureType.Horst:
                    CreateHorst(dataset);
                    break;
                case StructureType.ThrustSystem:
                    CreateThrustSystem(dataset);
                    break;
                case StructureType.FoldPair:
                    CreateFoldPair(dataset);
                    break;
                case StructureType.Detachment:
                    CreateDetachment(dataset);
                    break;
            }

            dataset.MarkAsModified();
        }

        private void CreateGraben(TwoDGeologyDataset dataset)
        {
            var leftFault = CreateFiniteFault(
                new Vector2(_positionX - _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                true);

            var rightFault = CreateFiniteFault(
                new Vector2(_positionX + _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                false);

            leftFault.Displacement = _displacement;
            rightFault.Displacement = _displacement;

            dataset.ProfileData.Faults.Add(leftFault);
            dataset.ProfileData.Faults.Add(rightFault);

            Logger.Log($"Created graben structure at X={_positionX}");
        }

        private void CreateHorst(TwoDGeologyDataset dataset)
        {
            var leftFault = CreateFiniteFault(
                new Vector2(_positionX - _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                false);

            var rightFault = CreateFiniteFault(
                new Vector2(_positionX + _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                true);

            leftFault.Displacement = _displacement;
            rightFault.Displacement = _displacement;

            dataset.ProfileData.Faults.Add(leftFault);
            dataset.ProfileData.Faults.Add(rightFault);

            Logger.Log($"Created horst structure at X={_positionX}");
        }

        private void CreateThrustSystem(TwoDGeologyDataset dataset)
        {
            var numFaults = 3;
            var spacing = _structureWidth / numFaults;

            for (var i = 0; i < numFaults; i++)
            {
                var thrust = CreateFiniteFault(
                    new Vector2(_positionX + i * spacing, _topElevation),
                    30f,
                    _structureDepth,
                    GeologicalMapping.GeologicalFeatureType.Fault_Thrust,
                    false);

                thrust.Displacement = _displacement / (i + 1);
                dataset.ProfileData.Faults.Add(thrust);
            }

            Logger.Log($"Created thrust system with {numFaults} faults");
        }

        private void CreateFoldPair(TwoDGeologyDataset dataset)
        {
            var wavelength = _structureWidth;
            var amplitude = _displacement;

            foreach (var formation in dataset.ProfileData.Formations)
                ApplyFoldToFormation(formation, _positionX, wavelength, amplitude);

            Logger.Log($"Created fold pair at X={_positionX}");
        }

        private void CreateDetachment(TwoDGeologyDataset dataset)
        {
            var detachment = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
            {
                Type = GeologicalMapping.GeologicalFeatureType.Fault_Detachment,
                Dip = 15f,
                Displacement = _displacement,
                FaultTrace = new List<Vector2>()
            };

            detachment.FaultTrace.Add(new Vector2(_positionX, _topElevation));
            detachment.FaultTrace.Add(new Vector2(_positionX + _structureWidth, _topElevation - 500f));
            detachment.FaultTrace.Add(new Vector2(_positionX + _structureWidth * 2, _topElevation - _structureDepth));

            dataset.ProfileData.Faults.Add(detachment);
            Logger.Log("Created detachment fault");
        }

        private GeologicalMapping.CrossSectionGenerator.ProjectedFault CreateFiniteFault(
            Vector2 surfacePoint, float dip, float depth,
            GeologicalMapping.GeologicalFeatureType type, bool dippingRight)
        {
            var fault = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
            {
                Type = type,
                Dip = dip,
                FaultTrace = new List<Vector2>()
            };

            fault.FaultTrace.Add(surfacePoint);

            var dipRad = dip * MathF.PI / 180f;
            var horizontalExtent = depth / MathF.Tan(dipRad);

            if (!dippingRight)
                horizontalExtent = -horizontalExtent;

            fault.FaultTrace.Add(new Vector2(
                surfacePoint.X + horizontalExtent,
                surfacePoint.Y - depth));

            return fault;
        }

        private void ApplyFoldToFormation(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float centerX, float wavelength, float amplitude)
        {
            for (var i = 0; i < formation.TopBoundary.Count; i++)
            {
                var point = formation.TopBoundary[i];
                var phase = 2f * MathF.PI * (point.X - centerX) / wavelength;
                var offset = amplitude * MathF.Sin(phase);
                formation.TopBoundary[i] = new Vector2(point.X, point.Y + offset);
            }

            for (var i = 0; i < formation.BottomBoundary.Count; i++)
            {
                var point = formation.BottomBoundary[i];
                var phase = 2f * MathF.PI * (point.X - centerX) / wavelength;
                var offset = amplitude * MathF.Sin(phase);
                formation.BottomBoundary[i] = new Vector2(point.X, point.Y + offset);
            }
        }

        private enum StructureType
        {
            Graben,
            Horst,
            ThrustSystem,
            FoldPair,
            Detachment
        }
    }

    #endregion

    #region Structural Restoration Tool

    private class StructuralRestorationTool
    {
        private float _restorationPercentage = 0f;
        private StructuralRestoration _restorationProcessor;
        private GeologicalMapping.CrossSectionGenerator.CrossSection _lastRestoredSection;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null)
            {
                ImGui.TextDisabled("Load a profile to use restoration tools.");
                return;
            }

            ImGui.TextWrapped("Unfold and unfault the cross-section to its pre-deformation state. The result is shown as a semi-transparent overlay in the viewer.");
            ImGui.Separator();

            // Initialize the processor if needed
            if (_restorationProcessor == null)
            {
                _restorationProcessor = new StructuralRestoration(dataset.ProfileData);
            }

            if (ImGui.SliderFloat("Restoration %", ref _restorationPercentage, 0f, 100f, "%.0f%%"))
            {
                _restorationProcessor.Restore(_restorationPercentage);
                _lastRestoredSection = _restorationProcessor.RestoredSection;
                dataset.GetViewer()?.SetRestorationData(_lastRestoredSection);
            }

            ImGui.Separator();

            if (ImGui.Button("Clear Overlay", new Vector2(-1, 0)))
            {
                dataset.GetViewer()?.ClearRestorationData();
                _restorationPercentage = 0f;
            }

            if (ImGui.Button("Apply Restored State", new Vector2(-1, 0)))
            {
                if (_lastRestoredSection != null)
                {
                    dataset.ProfileData = _lastRestoredSection;
                    dataset.MarkAsModified();

                    _restorationProcessor = new StructuralRestoration(dataset.ProfileData);
                    _lastRestoredSection = null;
                    _restorationPercentage = 0f;
                    dataset.GetViewer()?.ClearRestorationData();
                    Logger.Log("Applied restored state to the current profile.");
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Warning: This will replace the current section with the restored version.");
            }
        }
    }

    #endregion
}