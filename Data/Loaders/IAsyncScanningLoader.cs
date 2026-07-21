// GAIA/Data/Loaders/IAsyncScanningLoader.cs

namespace GAIA.Data.Loaders;

/// <summary>
///     Implemented by import loaders that scan a folder on a background thread, so their
///     <c>CanImport</c>/preview only become valid once the scan finishes. The import modal polls
///     <see cref="IsScanning"/> to keep re-evaluating the Import button while a scan is in flight
///     instead of caching the still-scanning (disabled) state.
/// </summary>
public interface IAsyncScanningLoader
{
    bool IsScanning { get; }
}
