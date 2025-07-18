// GeoscientistToolkit/Util/VeldridManager.cs
using Veldrid;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// A simple static class to hold global Veldrid resources.
    /// In a larger application, this might be a more formal service.
    /// </summary>
    public static class VeldridManager
    {
        public static GraphicsDevice GraphicsDevice { get; set; }
        public static ResourceFactory Factory => GraphicsDevice.ResourceFactory;
        public static ImGuiController ImGuiController { get; set; }
    }
}