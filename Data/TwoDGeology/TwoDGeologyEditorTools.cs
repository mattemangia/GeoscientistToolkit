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
///     Allows manipulation of layers, faults, and structural elements.
/// </summary>
public class TwoDGeologyEditorTools : IDatasetTools
{
    private readonly FaultEditor _faultEditor = new();
    private readonly LayerManipulator _layerManipulator = new();
    private readonly StructuralElementCreator _structuralCreator = new();

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

            // Vertical offset
            ImGui.Text("Vertical Offset:");
            if (ImGui.SliderFloat("##VertOffset", ref _verticalOffset, -500f, 500f, "%.1f m"))
                ApplyVerticalOffset(selectedFormation, _verticalOffset);

            // Horizontal offset
            ImGui.Text("Horizontal Offset:");
            if (ImGui.SliderFloat("##HorizOffset", ref _horizontalOffset, -1000f, 1000f, "%.1f m"))
                ApplyHorizontalOffset(selectedFormation, _horizontalOffset);

            ImGui.Separator();

            // Rotation
            ImGui.Text("Rotation (Dip Change):");
            if (ImGui.SliderFloat("##Rotation", ref _rotationAngle, -45f, 45f, "%.1f°"))
                ApplyRotation(selectedFormation, _rotationAngle, _rotationPivot);

            ImGui.Text("Pivot Point:");
            ImGui.InputFloat2("##Pivot", ref _rotationPivot);
            if (ImGui.Button("Set Pivot to Center", new Vector2(-1, 0)))
                _rotationPivot = CalculateFormationCenter(selectedFormation);

            ImGui.Separator();

            // Thickness adjustment
            ImGui.Text("Thickness Scale:");
            if (ImGui.SliderFloat("##ThicknessScale", ref _thicknessScale, 0.1f, 3.0f, "%.2fx"))
                ApplyThicknessScale(selectedFormation, _thicknessScale);

            ImGui.Separator();

            // Action buttons
            if (ImGui.Button("Reset All Transforms", new Vector2(-1, 0))) ResetTransforms();

            if (ImGui.Button("Duplicate Formation", new Vector2(-1, 0))) DuplicateFormation(dataset, selectedFormation);

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

        private void ApplyVerticalOffset(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float offset)
        {
            var baseTop = formation.TopBoundary.ToList();
            var baseBottom = formation.BottomBoundary.ToList();

            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            foreach (var point in baseTop) formation.TopBoundary.Add(new Vector2(point.X, point.Y + offset));

            foreach (var point in baseBottom) formation.BottomBoundary.Add(new Vector2(point.X, point.Y + offset));
        }

        private void ApplyHorizontalOffset(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float offset)
        {
            var baseTop = formation.TopBoundary.ToList();
            var baseBottom = formation.BottomBoundary.ToList();

            formation.TopBoundary.Clear();
            formation.BottomBoundary.Clear();

            foreach (var point in baseTop) formation.TopBoundary.Add(new Vector2(point.X + offset, point.Y));

            foreach (var point in baseBottom) formation.BottomBoundary.Add(new Vector2(point.X + offset, point.Y));
        }

        private void ApplyRotation(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float angleDegrees, Vector2 pivot)
        {
            var angleRad = angleDegrees * MathF.PI / 180f;
            var rotation = Matrix3x2.CreateRotation(angleRad, pivot);

            for (var i = 0; i < formation.TopBoundary.Count; i++)
                formation.TopBoundary[i] = Vector2.Transform(formation.TopBoundary[i], rotation);

            for (var i = 0; i < formation.BottomBoundary.Count; i++)
                formation.BottomBoundary[i] = Vector2.Transform(formation.BottomBoundary[i], rotation);
        }

        private void ApplyThicknessScale(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
            float scale)
        {
            // Scale bottom boundary away from top boundary
            for (var i = 0; i < Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count); i++)
            {
                var top = formation.TopBoundary[i];
                var bottom = formation.BottomBoundary[i];
                var thickness = bottom - top;
                formation.BottomBoundary[i] = top + thickness * scale;
            }
        }

        private Vector2 CalculateFormationCenter(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
        {
            var allPoints = formation.TopBoundary.Concat(formation.BottomBoundary).ToList();
            if (allPoints.Count == 0) return Vector2.Zero;

            var sumX = allPoints.Sum(p => p.X);
            var sumY = allPoints.Sum(p => p.Y);
            return new Vector2(sumX / allPoints.Count, sumY / allPoints.Count);
        }

        private void DuplicateFormation(TwoDGeologyDataset dataset,
            GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
        {
            var duplicate = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
            {
                Name = formation.Name + " (Copy)",
                Color = formation.Color,
                TopBoundary = new List<Vector2>(formation.TopBoundary),
                BottomBoundary = new List<Vector2>(formation.BottomBoundary)
            };

            // Offset slightly to make it visible
            for (var i = 0; i < duplicate.TopBoundary.Count; i++) duplicate.TopBoundary[i] += new Vector2(100, 50);
            for (var i = 0; i < duplicate.BottomBoundary.Count; i++)
                duplicate.BottomBoundary[i] += new Vector2(100, 50);

            dataset.ProfileData.Formations.Add(duplicate);
            Logger.Log($"Duplicated formation: {formation.Name}");
        }

        private void DeleteFormation(TwoDGeologyDataset dataset, int index)
        {
            if (index >= 0 && index < dataset.ProfileData.Formations.Count)
            {
                var name = dataset.ProfileData.Formations[index].Name;
                dataset.ProfileData.Formations.RemoveAt(index);
                Logger.Log($"Deleted formation: {name}");
            }
        }
    }

    #endregion

    #region Fault Editor

    private class FaultEditor
    {
        private float _newDip = 60f;
        private float _newDisplacement = 100f;

        private GeologicalMapping.GeologicalFeatureType _newFaultType =
            GeologicalMapping.GeologicalFeatureType.Fault_Normal;

        private int _selectedFaultIndex = -1;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null) return;

            ImGui.TextWrapped("Edit fault properties and geometry.");
            ImGui.Separator();

            var faults = dataset.ProfileData.Faults;
            if (faults.Count == 0)
            {
                ImGui.TextDisabled("No faults available.");
                return;
            }

            // Fault selection
            ImGui.Text("Select Fault:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FaultSelect",
                    _selectedFaultIndex >= 0 && _selectedFaultIndex < faults.Count
                        ? $"Fault {_selectedFaultIndex + 1} ({faults[_selectedFaultIndex].Type})"
                        : "None"))
            {
                for (var i = 0; i < faults.Count; i++)
                {
                    var label = $"Fault {i + 1} ({faults[i].Type})";
                    if (ImGui.Selectable(label, _selectedFaultIndex == i))
                    {
                        _selectedFaultIndex = i;
                        LoadFaultProperties(faults[i]);
                    }
                }

                ImGui.EndCombo();
            }

            if (_selectedFaultIndex < 0 || _selectedFaultIndex >= faults.Count)
            {
                ImGui.TextDisabled("Select a fault to edit.");
                return;
            }

            var selectedFault = faults[_selectedFaultIndex];

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Fault Properties");

            // Fault type
            ImGui.Text("Fault Type:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FaultType", _newFaultType.ToString()))
            {
                var faultTypes = new[]
                {
                    GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                    GeologicalMapping.GeologicalFeatureType.Fault_Reverse,
                    GeologicalMapping.GeologicalFeatureType.Fault_Thrust,
                    GeologicalMapping.GeologicalFeatureType.Fault_Transform,
                    GeologicalMapping.GeologicalFeatureType.Fault_Detachment
                };

                foreach (var type in faultTypes)
                    if (ImGui.Selectable(type.ToString(), _newFaultType == type))
                    {
                        _newFaultType = type;
                        selectedFault.Type = type;
                    }

                ImGui.EndCombo();
            }

            // Dip angle
            ImGui.Text("Dip Angle:");
            if (ImGui.SliderFloat("##Dip", ref _newDip, 0f, 90f, "%.1f°"))
            {
                selectedFault.Dip = _newDip;
                UpdateFaultGeometry(selectedFault);
            }

            // Displacement
            ImGui.Text("Displacement:");
            if (ImGui.SliderFloat("##Displacement", ref _newDisplacement, 0f, 2000f, "%.1f m"))
                selectedFault.Displacement = _newDisplacement;

            ImGui.Separator();

            // Geometry info
            ImGui.Text($"Fault trace points: {selectedFault.FaultTrace.Count}");
            if (selectedFault.FaultTrace.Count >= 2)
            {
                var length = CalculateFaultLength(selectedFault);
                ImGui.Text($"Fault length: {length:F1} m");
            }

            ImGui.Separator();

            // Actions
            if (ImGui.Button("Reverse Fault Direction", new Vector2(-1, 0))) ReverseFaultDirection(selectedFault);

            if (ImGui.Button("Extend to Basement", new Vector2(-1, 0))) ExtendFaultToDepth(selectedFault, -5000f);

            if (ImGui.Button("Make Listric", new Vector2(-1, 0))) MakeFaultListric(selectedFault);

            if (ImGui.Button("Delete Fault", new Vector2(-1, 0)))
            {
                dataset.ProfileData.Faults.RemoveAt(_selectedFaultIndex);
                _selectedFaultIndex = -1;
                Logger.Log("Deleted fault");
            }
        }

        private void LoadFaultProperties(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            _newDip = fault.Dip;
            _newDisplacement = fault.Displacement ?? 100f;
            _newFaultType = fault.Type;
        }

        private void UpdateFaultGeometry(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            // Adjust fault trace to match new dip
            if (fault.FaultTrace.Count >= 2)
            {
                var surfacePoint = fault.FaultTrace[0];
                var dipRad = fault.Dip * MathF.PI / 180f;

                for (var i = 1; i < fault.FaultTrace.Count; i++)
                {
                    var verticalDist = surfacePoint.Y - fault.FaultTrace[i].Y;
                    var horizontalDist = verticalDist / MathF.Tan(dipRad);
                    fault.FaultTrace[i] = new Vector2(
                        surfacePoint.X + horizontalDist,
                        surfacePoint.Y - verticalDist);
                }
            }
        }

        private float CalculateFaultLength(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            var length = 0f;
            for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
                length += Vector2.Distance(fault.FaultTrace[i], fault.FaultTrace[i + 1]);
            return length;
        }

        private void ReverseFaultDirection(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            fault.FaultTrace.Reverse();
            Logger.Log("Reversed fault direction");
        }

        private void ExtendFaultToDepth(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault,
            float targetDepth)
        {
            if (fault.FaultTrace.Count < 2) return;

            var lastPoint = fault.FaultTrace[^1];
            var dipRad = fault.Dip * MathF.PI / 180f;
            var depthToGo = lastPoint.Y - targetDepth;
            var horizontalExtent = depthToGo / MathF.Tan(dipRad);

            fault.FaultTrace.Add(new Vector2(lastPoint.X + horizontalExtent, targetDepth));
            Logger.Log($"Extended fault to depth: {targetDepth} m");
        }

        private void MakeFaultListric(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
        {
            if (fault.FaultTrace.Count < 2) return;

            // Convert planar fault to listric (curved) geometry
            var surfacePoint = fault.FaultTrace[0];
            var initialDip = fault.Dip;
            var detachmentDepth = -3000f;

            fault.FaultTrace.Clear();
            fault.FaultTrace.Add(surfacePoint);

            var segments = 10;
            for (var i = 1; i <= segments; i++)
            {
                var depth = (surfacePoint.Y - detachmentDepth) * i / segments;

                // Dip decreases exponentially with depth (typical listric geometry)
                var dip = initialDip * MathF.Exp(-depth / 2000f);
                var dipRad = dip * MathF.PI / 180f;

                var x = surfacePoint.X + depth / MathF.Tan(dipRad);
                var y = surfacePoint.Y - depth;

                fault.FaultTrace.Add(new Vector2(x, y));
            }

            Logger.Log("Converted fault to listric geometry");
        }
    }

    #endregion

    #region Structural Element Creator

    private class StructuralElementCreator
    {
        private float _displacement = 500f;
        private float _faultDip = 60f;
        private float _positionX = 5000f;
        private float _structureDepth = 1000f;
        private StructureType _structureType = StructureType.Graben;
        private float _structureWidth = 2000f;
        private float _topElevation;

        public void Draw(TwoDGeologyDataset dataset)
        {
            if (dataset?.ProfileData == null) return;

            ImGui.TextWrapped("Create complex geological structures automatically.");
            ImGui.Separator();

            // Structure type selection
            ImGui.Text("Structure Type:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##StructType", _structureType.ToString()))
            {
                foreach (var type in Enum.GetValues<StructureType>())
                    if (ImGui.Selectable(type.ToString(), _structureType == type))
                        _structureType = type;

                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Common parameters
            ImGui.Text("Position (X):");
            ImGui.SliderFloat("##PosX", ref _positionX, 0f, 20000f, "%.0f m");

            ImGui.Text("Top Elevation:");
            ImGui.SliderFloat("##TopElev", ref _topElevation, -1000f, 1000f, "%.0f m");

            ImGui.Text("Structure Width:");
            ImGui.SliderFloat("##Width", ref _structureWidth, 500f, 5000f, "%.0f m");

            ImGui.Text("Structure Depth:");
            ImGui.SliderFloat("##Depth", ref _structureDepth, 500f, 5000f, "%.0f m");

            ImGui.Text("Fault Dip:");
            ImGui.SliderFloat("##FaultDip", ref _faultDip, 30f, 90f, "%.1f°");

            if (_structureType != StructureType.FoldPair)
            {
                ImGui.Text("Displacement:");
                ImGui.SliderFloat("##Disp", ref _displacement, 100f, 2000f, "%.0f m");
            }

            ImGui.Separator();

            // Structure-specific info
            DrawStructureInfo();

            ImGui.Separator();

            // Create button
            if (ImGui.Button($"Create {_structureType}", new Vector2(-1, 0))) CreateStructure(dataset);
        }

        private void DrawStructureInfo()
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), "Structure Info:");

            switch (_structureType)
            {
                case StructureType.Graben:
                    ImGui.BulletText("Downthrown block between two normal faults");
                    ImGui.BulletText("Creates subsidence basin");
                    break;
                case StructureType.Horst:
                    ImGui.BulletText("Uplifted block between two normal faults");
                    ImGui.BulletText("Creates structural high");
                    break;
                case StructureType.ThrustSystem:
                    ImGui.BulletText("Imbricate thrust faults");
                    ImGui.BulletText("Creates compressional structures");
                    break;
                case StructureType.FoldPair:
                    ImGui.BulletText("Anticline and syncline pair");
                    ImGui.BulletText("No faulting, pure folding");
                    break;
                case StructureType.Detachment:
                    ImGui.BulletText("Low-angle detachment fault");
                    ImGui.BulletText("Creates extensional province");
                    break;
            }
        }

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
        }

        private void CreateGraben(TwoDGeologyDataset dataset)
        {
            // Create two normal faults dipping toward each other
            var leftFault = CreateFault(
                new Vector2(_positionX - _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                true);

            var rightFault = CreateFault(
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
            // Create two normal faults dipping away from each other
            var leftFault = CreateFault(
                new Vector2(_positionX - _structureWidth / 2, _topElevation),
                _faultDip,
                _structureDepth,
                GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                false);

            var rightFault = CreateFault(
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
            // Create imbricate thrust system
            var numFaults = 3;
            var spacing = _structureWidth / numFaults;

            for (var i = 0; i < numFaults; i++)
            {
                var thrust = CreateFault(
                    new Vector2(_positionX + i * spacing, _topElevation),
                    30f, // Typical thrust dip
                    _structureDepth,
                    GeologicalMapping.GeologicalFeatureType.Fault_Thrust,
                    false);

                thrust.Displacement = _displacement / (i + 1); // Decreasing displacement

                dataset.ProfileData.Faults.Add(thrust);
            }

            Logger.Log($"Created thrust system with {numFaults} faults");
        }

        private void CreateFoldPair(TwoDGeologyDataset dataset)
        {
            // Create anticline-syncline pair by deforming existing formations
            var wavelength = _structureWidth;
            var amplitude = _displacement;

            foreach (var formation in dataset.ProfileData.Formations)
                ApplyFoldToFormation(formation, _positionX, wavelength, amplitude);

            Logger.Log($"Created fold pair at X={_positionX}");
        }

        private void CreateDetachment(TwoDGeologyDataset dataset)
        {
            // Create low-angle detachment fault
            var detachment = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
            {
                Type = GeologicalMapping.GeologicalFeatureType.Fault_Detachment,
                Dip = 15f, // Low-angle
                Displacement = _displacement,
                FaultTrace = new List<Vector2>()
            };

            // Create gently dipping surface
            detachment.FaultTrace.Add(new Vector2(_positionX, _topElevation));
            detachment.FaultTrace.Add(new Vector2(_positionX + _structureWidth, _topElevation - 500f));
            detachment.FaultTrace.Add(new Vector2(_positionX + _structureWidth * 2, _topElevation - _structureDepth));

            dataset.ProfileData.Faults.Add(detachment);

            Logger.Log("Created detachment fault");
        }

        private GeologicalMapping.CrossSectionGenerator.ProjectedFault CreateFault(
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
}