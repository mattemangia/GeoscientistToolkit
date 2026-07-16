namespace GAIA.Business;

/// <summary>Policy for engineering-scale geomechanics retired from GAIA.</summary>
public static class MacroGeomechanicsRetirement
{
    public const string Message =
        "Engineering-scale geomechanics execution has moved to PRISM. " +
        "Export or exchange the model with PRISM to run slope stability, 2D FEM/SRM, DEM or THM analyses. " +
        "GAIA keeps this legacy model read-only for project compatibility.";

    public static NotSupportedException CreateException() => new(Message);
}
