using GAIA.Util;
using GAIA.Data.PhysicoChem;
using OpenTK.Graphics.OpenGL;

namespace GAIA.UI.Diagnostics;

/// <summary>Fast context-bound diagnostic covering renderer shader compilation and framebuffer creation.</summary>
internal static class OpenTkGraphicsSelfTest
{
    public static void RunOrThrow()
    {
        GL.GetError();
        using (var texture = TextureManager.CreateFromPixelData(new byte[4 * 4 * 4], 4, 4))
        {
            if (!texture.IsValid || texture.GetImGuiTextureId() == IntPtr.Zero)
                throw new InvalidOperationException("OpenGL texture manager did not create a shared texture.");
            texture.UpdateFromPixelData(Enumerable.Repeat((byte)255, 8 * 8 * 4).ToArray(), 8, 8);
            if (texture.Width != 8 || texture.Height != 8)
                throw new InvalidOperationException("OpenGL texture resize/update failed.");
        }
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
        using (var reactor = new OpenTkPhysicoChemRenderer())
        {
            reactor.Initialize();
            reactor.Resize(64, 64);
            var dataset = new PhysicoChemDataset("OpenGL self-test");
            dataset.Mesh.Cells["center"] = new Cell
            {
                ID = "center", Center = (0, 0, 0), Volume = 0.25, MaterialID = "Default",
                InitialConditions = new InitialConditions()
            };
            reactor.Upload(dataset);
            reactor.Render(System.Numerics.Matrix4x4.Identity, System.Numerics.Matrix4x4.Identity, RenderMode.Solid);
            if (reactor.ColorTexture == 0 || reactor.Pick(32, 32) != "center")
                throw new InvalidOperationException("PhysicoChem depth/picking framebuffer self-test failed.");
        }
        var error = GL.GetError();
        if (error != ErrorCode.NoError)
            throw new InvalidOperationException($"OpenGL self-test ended with {error}.");
        Logger.Log("[OpenTK self-test] PNM, geothermal and PhysicoChem depth/picking checks passed.");
    }
}
