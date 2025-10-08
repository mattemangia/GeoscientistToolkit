// GeoscientistToolkit/Util/FileAssociation.cs

#if WINDOWS
using Microsoft.Win32;
using System;
using System.Diagnostics;
#endif

namespace GeoscientistToolkit.Util;

/// <summary>
///     Manages .gtp file association on Windows.
/// </summary>
public static class FileAssociation
{
    private const string Extension = ".gtp";
    private const string ProgId = "GeoscientistToolkit.Project";
    private const string FileDescription = "GeoscientistToolkit Project File";

    public static bool IsAssociationSupported =>
#if WINDOWS
            true;
#else
        false;
#endif

    public static bool IsAssociated()
    {
#if WINDOWS
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Extension}");
                return key?.GetValue(null)?.ToString() == ProgId;
            }
            catch
            {
                return false;
            }
#else
        return false;
#endif
    }

    public static void Register()
    {
#if WINDOWS
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                // Create the extension key
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Extension}", true);
                extKey.SetValue(null, ProgId);

                // Create the program ID key
                using var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}", true);
                progKey.SetValue(null, FileDescription);
                using var iconKey = progKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue(null, $"\"{exePath}\",0");
                using var shellKey = progKey.CreateSubKey("shell\\open\\command");
                shellKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                
                Logger.Log("Successfully registered .gtp file association.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to register file association (requires admin rights?): {ex.Message}");
                throw;
            }
#endif
    }

    public static void Unregister()
    {
#if WINDOWS
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Extension}", false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
                Logger.Log("Successfully unregistered .gtp file association.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to unregister file association: {ex.Message}");
                throw;
            }
#endif
    }
}