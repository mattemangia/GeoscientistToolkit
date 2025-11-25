// GeoscientistToolkit/UI/ClipboardService.cs

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.Borehole;

namespace GeoscientistToolkit.UI;

/// <summary>
/// Provides static methods for clipboard operations (copy, cut, paste) for LithologyUnit objects.
/// </summary>
public static class ClipboardService
{
    private static List<LithologyUnit> _clipboard;
    private static bool _isCut;

    /// <summary>
    /// Copies a list of LithologyUnit objects to the clipboard.
    /// </summary>
    /// <param name="units">The list of LithologyUnit objects to copy.</param>
    public static void Copy(List<LithologyUnit> units)
    {
        _clipboard = new List<LithologyUnit>();
        foreach (var unit in units)
        {
            _clipboard.Add(Clone(unit));
        }
        _isCut = false;
    }

    /// <summary>
    /// Cuts a list of LithologyUnit objects to the clipboard.
    /// </summary>
    /// <param name="units">The list of LithologyUnit objects to cut.</param>
    public static void Cut(List<LithologyUnit> units, BoreholeDataset dataset)
    {
        Copy(units);
        dataset.RemoveLithologyUnits(units);
        _isCut = true;
    }

    /// <summary>
    /// Retrieves the list of LithologyUnit objects from the clipboard for pasting.
    /// </summary>
    /// <returns>A new list of LithologyUnit objects from the clipboard.</returns>
    public static List<LithologyUnit> Paste()
    {
        if (_clipboard == null)
        {
            return new List<LithologyUnit>();
        }

        var pastedUnits = new List<LithologyUnit>();
        foreach (var unit in _clipboard)
        {
            pastedUnits.Add(Clone(unit));
        }
        return pastedUnits;
    }

    /// <summary>
    /// Checks if the last operation was a cut.
    /// </summary>
    /// <returns>True if the last operation was a cut, otherwise false.</returns>
    public static bool IsCut()
    {
        return _isCut;
    }

    /// <summary>
    /// Clears the clipboard after a cut and paste operation.
    /// </summary>
    public static void Clear()
    {
        _clipboard = null;
        _isCut = false;
    }

    /// <summary>
    /// Creates a deep copy of a LithologyUnit object.
    /// </summary>
    /// <param name="unit">The LithologyUnit object to clone.</param>
    /// <returns>A new instance of the LithologyUnit object with the same properties.</returns>
    private static LithologyUnit Clone(LithologyUnit unit)
    {
        return new LithologyUnit
        {
            ID = Guid.NewGuid().ToString(),
            Name = unit.Name,
            LithologyType = unit.LithologyType,
            DepthFrom = unit.DepthFrom,
            DepthTo = unit.DepthTo,
            UpperContactType = unit.UpperContactType,
            LowerContactType = unit.LowerContactType,
            Color = unit.Color,
            Description = unit.Description,
            GrainSize = unit.GrainSize,
            ParameterSources = new Dictionary<string, ParameterSource>(unit.ParameterSources),
            Parameters = new Dictionary<string, float>(unit.Parameters)
        };
    }
}
