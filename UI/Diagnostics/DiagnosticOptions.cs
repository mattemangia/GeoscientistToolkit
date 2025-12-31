namespace GeoscientistToolkit.UI.Diagnostics;

public sealed class DiagnosticOptions
{
    public bool RunAiDiagnostic { get; init; }
    public bool RunGuiDiagnostic { get; init; }
    public bool RunTests { get; init; }
    public string[] TestFilters { get; init; } = Array.Empty<string>();
}
