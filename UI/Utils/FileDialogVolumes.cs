// GAIA/UI/Utils/FileDialogVolumes.cs

using System.Runtime.InteropServices;

namespace GAIA.UI.Utils;

/// <summary>
///     A volume for the file dialogs' drive panel.
/// </summary>
public sealed class VolumeInfo
{
    public string Path { get; set; }
    public string DisplayName { get; set; }
    public string VolumeLabel { get; set; }
    public DriveType DriveType { get; set; }
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public bool IsReady { get; set; }

    /// <summary>True while the drive is still being probed, so its label and size are not known yet.</summary>
    public bool Probing { get; set; }
}

/// <summary>
///     The drive list shown by every file dialog, scanned once in the background and shared by all
///     of them.
///     Reading a drive's IsReady/VolumeLabel/TotalSize blocks until the device answers, and a
///     disconnected network drive takes ~20 s to time out. The dialogs are constructed eagerly while
///     the main window is built, so scanning per instance blocked startup long enough for Windows to
///     mark the window as not responding. Nothing here ever blocks the caller: the panel draws the
///     latest snapshot and drives fill in as their probes land.
/// </summary>
public static class FileDialogVolumes
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();

    private static VolumeInfo[] _snapshot = Array.Empty<VolumeInfo>();
    private static DateTime _scannedUtc = DateTime.MinValue;
    private static bool _scanning;

    /// <summary>
    ///     The most recent snapshot. Empty until the first scan reports back. Never blocks.
    /// </summary>
    public static IReadOnlyList<VolumeInfo> Current
    {
        get
        {
            lock (Gate) return _snapshot;
        }
    }

    /// <summary>
    ///     True while a scan is in flight, so the panel can say so rather than look empty.
    /// </summary>
    public static bool IsScanning
    {
        get
        {
            lock (Gate) return _scanning;
        }
    }

    /// <summary>
    ///     Kicks off a background scan unless one is already running or the snapshot is still fresh.
    ///     Pass <paramref name="force" /> to rescan regardless of age (the panel's Refresh button).
    /// </summary>
    public static void Refresh(bool force = false)
    {
        lock (Gate)
        {
            if (_scanning) return;
            if (!force && _scannedUtc != DateTime.MinValue && DateTime.UtcNow - _scannedUtc < StaleAfter) return;
            _scanning = true;
        }

        Task.Run(() =>
        {
            try
            {
                Scan();
            }
            catch
            {
                // Keep whatever snapshot we already had rather than blanking the panel.
            }
            finally
            {
                lock (Gate)
                {
                    _scannedUtc = DateTime.UtcNow;
                    _scanning = false;
                }
            }
        });
    }

    private static void Scan()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ScanWindowsDrives();
        else
            Publish(ScanUnixMounts());
    }

    private static void ScanWindowsDrives()
    {
        var drives = DriveInfo.GetDrives();

        // Enumerating the letters is cheap, so publish them right away and let each probe fill in
        // the details; only the probe below can block.
        var volumes = new VolumeInfo[drives.Length];
        for (var i = 0; i < drives.Length; i++)
            volumes[i] = new VolumeInfo
            {
                Path = SafeRootPath(drives[i]),
                DisplayName = drives[i].Name.TrimEnd('\\'),
                DriveType = SafeDriveType(drives[i]),
                VolumeLabel = string.Empty,
                IsReady = false,
                Probing = true
            };

        Publish(volumes);

        // Probe in parallel: one unreachable network drive must not hold up the local disks.
        Parallel.For(0, drives.Length, i =>
        {
            var probed = Probe(drives[i]);
            lock (Gate)
            {
                volumes[i] = probed;
                _snapshot = (VolumeInfo[])volumes.Clone();
            }
        });
    }

    private static VolumeInfo Probe(DriveInfo drive)
    {
        var volume = new VolumeInfo
        {
            Path = SafeRootPath(drive),
            DisplayName = drive.Name.TrimEnd('\\'),
            DriveType = SafeDriveType(drive),
            Probing = false
        };

        try
        {
            // IsReady re-probes the device on every read, so ask once.
            volume.IsReady = drive.IsReady;
            if (volume.IsReady)
            {
                volume.VolumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                volume.TotalBytes = drive.TotalSize;
                volume.AvailableBytes = drive.AvailableFreeSpace;
            }
            else
            {
                volume.VolumeLabel = DriveTypeLabel(volume.DriveType);
            }
        }
        catch
        {
            volume.IsReady = false;
            volume.VolumeLabel = DriveTypeLabel(volume.DriveType);
        }

        return volume;
    }

    private static string SafeRootPath(DriveInfo drive)
    {
        try
        {
            return drive.RootDirectory.FullName;
        }
        catch
        {
            return drive.Name;
        }
    }

    private static DriveType SafeDriveType(DriveInfo drive)
    {
        try
        {
            return drive.DriveType;
        }
        catch
        {
            return DriveType.Unknown;
        }
    }

    private static VolumeInfo[] ScanUnixMounts()
    {
        var volumes = new List<VolumeInfo>();

        AddUnixVolume(volumes, "/", "Root");
        AddUnixVolume(volumes, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home");

        var mountDirs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new[] { "/Volumes" }
            : new[] { "/media", "/mnt", "/run/media" };

        foreach (var mountDir in mountDirs)
        {
            if (!Directory.Exists(mountDir)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(mountDir))
                    AddUnixVolume(volumes, dir, Path.GetFileName(dir));
            }
            catch
            {
            }
        }

        return volumes.ToArray();
    }

    private static void AddUnixVolume(List<VolumeInfo> volumes, string path, string name)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (volumes.Any(v => string.Equals(v.Path, path, StringComparison.Ordinal))) return;

        try
        {
            var volume = new VolumeInfo
            {
                Path = path,
                DisplayName = name,
                VolumeLabel = name,
                DriveType = DriveType.Fixed,
                IsReady = true
            };

            try
            {
                var driveInfo = new DriveInfo(path);
                if (driveInfo.IsReady)
                {
                    volume.TotalBytes = driveInfo.TotalSize;
                    volume.AvailableBytes = driveInfo.AvailableFreeSpace;
                }
            }
            catch
            {
                // Space info is optional; the volume is still usable without it.
            }

            volumes.Add(volume);
        }
        catch
        {
        }
    }

    private static void Publish(VolumeInfo[] volumes)
    {
        lock (Gate) _snapshot = volumes;
    }

    public static string DriveTypeLabel(DriveType type)
    {
        return type switch
        {
            DriveType.Removable => "Removable",
            DriveType.Fixed => "Local Disk",
            DriveType.Network => "Network Drive",
            DriveType.CDRom => "CD/DVD",
            DriveType.Ram => "RAM Disk",
            _ => "Unknown"
        };
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
