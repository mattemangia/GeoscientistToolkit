using GAIA.Util;
using OpenTK.Graphics.OpenGL;

namespace GAIA.UI.Diagnostics;

/// <summary>Fast context-bound diagnostic covering renderer shader compilation and framebuffer creation.</summary>
internal static class OpenTkGraphicsSelfTest
{
    public static void RunOrThrow()
    {
        GL.GetError();
        using (var pnm = new OpenTkPnmRenderer())
        {
            pnm.Resize(64, 64);
            pnm.Upload(Array.Empty<OpenTkPnmRenderer.PoreGpuData>(),
                Array.Empty<OpenTkPnmRenderer.ThroatGpuData>());
            pnm.Render(System.Numerics.Matrix4x4.Identity, System.Numerics.Vector3.UnitZ,
                0, 1, 1, true, true);
        }
        using (var geothermal = new GeothermalVisualization3D())
        {
            geothermal.Resize(64, 64);
            geothermal.Render();
            if (geothermal.GetRenderTargetImGuiBinding() == IntPtr.Zero)
                throw new InvalidOperationException("Geothermal renderer did not create a color target.");
        }
        var error = GL.GetError();
        if (error != ErrorCode.NoError)
            throw new InvalidOperationException($"OpenGL self-test ended with {error}.");
        Logger.Log("[OpenTK self-test] PNM and geothermal renderer checks passed.");
    }
}
