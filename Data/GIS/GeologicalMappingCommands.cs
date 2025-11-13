// GeoscientistToolkit/UI/GIS/GeologicalMappingCommands.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Commands specific to geological mapping operations
/// </summary>
public static class GeologicalMappingCommands
{
    /// <summary>
    /// Captured state of a formation for undo/redo
    /// </summary>
    public class FormationState  // ✅ CHANGED from private to public
    {
        public string Name { get; set; }
        public Vector4 Color { get; set; }
        public List<Vector2> TopBoundary { get; set; }
        public List<Vector2> BottomBoundary { get; set; }
        public FoldStyle? FoldStyle { get; set; }
        
        public static FormationState Capture(CrossSectionGenerator.ProjectedFormation formation)
        {
            return new FormationState
            {
                Name = formation.Name,
                Color = formation.Color,
                TopBoundary = new List<Vector2>(formation.TopBoundary),
                BottomBoundary = new List<Vector2>(formation.BottomBoundary),
                FoldStyle = formation.FoldStyle
            };
        }
        
        public void Apply(CrossSectionGenerator.ProjectedFormation formation)
        {
            formation.Name = Name;
            formation.Color = Color;
            formation.TopBoundary = new List<Vector2>(TopBoundary);
            formation.BottomBoundary = new List<Vector2>(BottomBoundary);
            formation.FoldStyle = FoldStyle;
        }
    }
    
    /// <summary>
    /// Captured state of a fault for undo/redo
    /// </summary>
    public class FaultState  // ✅ CHANGED from private to public
    {
        public GeologicalFeatureType Type { get; set; }
        public List<Vector2> FaultTrace { get; set; }
        public float Dip { get; set; }
        public string DipDirection { get; set; }
        public float? Displacement { get; set; }
        
        public static FaultState Capture(CrossSectionGenerator.ProjectedFault fault)
        {
            return new FaultState
            {
                Type = fault.Type,
                FaultTrace = new List<Vector2>(fault.FaultTrace),
                Dip = fault.Dip,
                DipDirection = fault.DipDirection,
                Displacement = fault.Displacement
            };
        }
        
        public void Apply(CrossSectionGenerator.ProjectedFault fault)
        {
            fault.Type = Type;
            fault.FaultTrace = new List<Vector2>(FaultTrace);
            fault.Dip = Dip;
            fault.DipDirection = DipDirection;
            fault.Displacement = Displacement;
        }
    }
    
    /// <summary>
    /// Command for modifying a formation
    /// </summary>
    public class ModifyFormationCommand : CommandBase
    {
        private readonly CrossSectionGenerator.ProjectedFormation _formation;
        private readonly FormationState _oldState;
        private readonly FormationState _newState;
        
        public ModifyFormationCommand(
            CrossSectionGenerator.ProjectedFormation formation,
            FormationState oldState = null)
        {
            _formation = formation;
            _oldState = oldState ?? FormationState.Capture(formation);
            _newState = FormationState.Capture(formation);
        }
        
        public override void Execute() => _newState.Apply(_formation);
        public override void Undo() => _oldState.Apply(_formation);
        public override string Description => $"Modify Formation '{_formation.Name}'";
    }
    
    /// <summary>
    /// Command for modifying a fault
    /// </summary>
    public class ModifyFaultCommand : CommandBase
    {
        private readonly CrossSectionGenerator.ProjectedFault _fault;
        private readonly FaultState _oldState;
        private readonly FaultState _newState;
        
        public ModifyFaultCommand(
            CrossSectionGenerator.ProjectedFault fault,
            FaultState oldState = null)
        {
            _fault = fault;
            _oldState = oldState ?? FaultState.Capture(fault);
            _newState = FaultState.Capture(fault);
        }
        
        public override void Execute() => _newState.Apply(_fault);
        public override void Undo() => _oldState.Apply(_fault);
        public override string Description => $"Modify Fault ({_fault.Type})";
    }
    
    /// <summary>
    /// Command for moving points in a boundary
    /// </summary>
    public class MoveBoundaryPointsCommand : CommandBase
    {
        private readonly List<Vector2> _boundary;
        private readonly Dictionary<int, Vector2> _oldPositions;
        private readonly Dictionary<int, Vector2> _newPositions;
        
        public MoveBoundaryPointsCommand(
            List<Vector2> boundary,
            Dictionary<int, Vector2> pointIndicesAndOldPositions,
            Dictionary<int, Vector2> pointIndicesAndNewPositions)
        {
            _boundary = boundary;
            _oldPositions = new Dictionary<int, Vector2>(pointIndicesAndOldPositions);
            _newPositions = new Dictionary<int, Vector2>(pointIndicesAndNewPositions);
        }
        
        public override void Execute()
        {
            foreach (var kvp in _newPositions)
                if (kvp.Key >= 0 && kvp.Key < _boundary.Count)
                    _boundary[kvp.Key] = kvp.Value;
        }
        
        public override void Undo()
        {
            foreach (var kvp in _oldPositions)
                if (kvp.Key >= 0 && kvp.Key < _boundary.Count)
                    _boundary[kvp.Key] = kvp.Value;
        }
        
        public override string Description => $"Move {_newPositions.Count} Boundary Point(s)";
        
        public override bool CanMergeWith(ICommand next)
        {
            // Allow merging consecutive move operations on the same points
            return next is MoveBoundaryPointsCommand moveCmd &&
                   moveCmd._boundary == _boundary &&
                   moveCmd._oldPositions.Keys.SequenceEqual(_oldPositions.Keys);
        }
        
        public override void MergeWith(ICommand next)
        {
            if (next is MoveBoundaryPointsCommand moveCmd)
            {
                // Update new positions with the latest movement
                foreach (var kvp in moveCmd._newPositions)
                    _newPositions[kvp.Key] = kvp.Value;
            }
        }
    }
}