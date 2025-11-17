namespace GeoscientistToolkit.Installer.Utilities;

internal static class RuntimeHelper
{
    public static string GetCurrentRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";
        }

        return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
            ? "linux-arm64"
            : "linux-x64";
    }
}
