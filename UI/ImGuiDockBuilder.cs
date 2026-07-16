// GAIA/UI/ImGuiDockBuilder.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;

namespace GAIA.UI;

/// <summary>
///     P/Invoke bindings for the DockBuilder API.
///     ImGui.NET does not surface these because they live in imgui_internal.h, but the
///     cimgui native library shipped with the package exports them, so they can be called directly.
///     Signatures follow cimgui 1.90.8, matching the ImGui.NET version referenced by the project.
/// </summary>
internal static class ImGuiDockBuilder
{
    private const string LibName = "cimgui";

    /// <summary>
    ///     ImGuiDockNodeFlags_DockSpace (1 &lt;&lt; 10) from imgui_internal.h: marks a node as occupying
    ///     space inside an existing window instead of floating in its own. The public
    ///     ImGuiDockNodeFlags enum bound by ImGui.NET only covers bits 0-7, so it is declared here.
    /// </summary>
    public const ImGuiDockNodeFlags DockSpaceFlag = (ImGuiDockNodeFlags)(1 << 10);

    /// <summary>
    ///     True when the DockBuilder symbols could be resolved in the native library.
    ///     A cimgui built without docking support would throw <see cref="EntryPointNotFoundException" />,
    ///     so probe once and let callers fall back to floating panels rather than crashing at startup.
    /// </summary>
    public static bool IsAvailable => _available.Value;

    private static readonly Lazy<bool> _available = new(() =>
    {
        try
        {
            // GetNode on a non-existent id is side-effect free and returns null.
            igDockBuilderGetNode(0);
            return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    });

    /// <summary>
    ///     True when a dock node with this id currently exists, i.e. ImGui restored one from the ini.
    ///     Must be queried before calling <see cref="ImGui.DockSpace(uint)" />, which creates the node on demand.
    /// </summary>
    public static bool HasNode(uint nodeId) => IsAvailable && igDockBuilderGetNode(nodeId) != IntPtr.Zero;

    public static uint AddNode(uint nodeId, ImGuiDockNodeFlags flags) => igDockBuilderAddNode(nodeId, flags);

    public static void RemoveNode(uint nodeId) => igDockBuilderRemoveNode(nodeId);

    public static void SetNodeSize(uint nodeId, Vector2 size) => igDockBuilderSetNodeSize(nodeId, size);

    public static uint SplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir,
        out uint outIdAtDir, out uint outIdAtOppositeDir)
        => igDockBuilderSplitNode(nodeId, splitDir, sizeRatioForNodeAtDir, out outIdAtDir, out outIdAtOppositeDir);

    public static void DockWindow(string windowName, uint nodeId)
        => igDockBuilderDockWindow(ToNullTerminatedUtf8(windowName), nodeId);

    public static void Finish(uint nodeId) => igDockBuilderFinish(nodeId);

    /// <summary>
    ///     cimgui expects a null-terminated UTF-8 string; the default marshaller would use
    ///     the platform ANSI encoding, which is not UTF-8 everywhere.
    /// </summary>
    private static byte[] ToNullTerminatedUtf8(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        return bytes;
    }

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igDockBuilderGetNode(uint node_id);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderAddNode(uint node_id, ImGuiDockNodeFlags flags);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderRemoveNode(uint node_id);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodeSize(uint node_id, Vector2 size);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderSplitNode(uint node_id, ImGuiDir split_dir,
        float size_ratio_for_node_at_dir, out uint out_id_at_dir, out uint out_id_at_opposite_dir);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow(byte[] window_name, uint node_id);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint node_id);
}
