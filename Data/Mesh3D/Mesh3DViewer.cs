using System;
using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.Mesh3D
{
    /// <summary>
    /// 3D model viewer for Mesh3DDataset, with support for multiple models and interactive controls.
    /// </summary>
    public class Mesh3DViewer : IDatasetViewer, IDisposable
    {
        private readonly Mesh3DDataset _dataset;
        private readonly D3D11MeshRenderer _d3dRenderer;
        private readonly MetalMeshRenderer _metalRenderer;
        private readonly VulkanMeshRenderer _vulkanRenderer;

        // Camera parameters
        private Vector3 _cameraTarget = Vector3.Zero;
        private float _cameraYaw = -MathF.PI / 4f;
        private float _cameraPitch = MathF.PI / 6f;
        private float _cameraDistance = 2.0f;
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projMatrix;
        private bool _isDragging = false;
        private bool _isPanning = false;
        private Vector2 _lastMousePos;

        // Grid toggle
        private bool _showGrid = true;

        // Manage off-screen rendered texture for ImGui
        private TextureManager _textureManager;

        /// <summary>
        /// Initialize Mesh3DViewer for the given dataset.
        /// </summary>
        public Mesh3DViewer(Mesh3DDataset dataset)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _dataset.Load();  // Ensure data is loaded

            // Initialize platform-specific renderer based on backend
            var backend = VeldridManager.GraphicsDevice.BackendType;
            Logger.Log($"Initializing Mesh3D renderer for backend: {backend}");
            
            switch (backend)
            {
                case GraphicsBackend.Metal:
                    _metalRenderer = new MetalMeshRenderer();
                    _metalRenderer.Initialize(_dataset);
                    _textureManager = TextureManager.CreateFromTexture(_metalRenderer.ColorTarget);
                    break;
                    
                case GraphicsBackend.Direct3D11:
                    _d3dRenderer = new D3D11MeshRenderer();
                    _d3dRenderer.Initialize(_dataset);
                    _textureManager = TextureManager.CreateFromTexture(_d3dRenderer.ColorTarget);
                    break;
                    
                case GraphicsBackend.Vulkan:
                    _vulkanRenderer = new VulkanMeshRenderer();
                    _vulkanRenderer.Initialize(_dataset);
                    _textureManager = TextureManager.CreateFromTexture(_vulkanRenderer.ColorTarget);
                    break;
                    
                case GraphicsBackend.OpenGL:
                case GraphicsBackend.OpenGLES:
                    // For OpenGL, we can use the Vulkan renderer with GLSL shaders
                    // as Veldrid handles the translation appropriately
                    _vulkanRenderer = new VulkanMeshRenderer();
                    _vulkanRenderer.Initialize(_dataset);
                    _textureManager = TextureManager.CreateFromTexture(_vulkanRenderer.ColorTarget);
                    Logger.Log("Using Vulkan renderer for OpenGL backend");
                    break;
                    
                default:
                    throw new NotSupportedException($"Graphics backend {backend} is not supported for Mesh3D rendering");
            }

            // Initialize camera target at dataset center and set up initial matrices
            _cameraTarget = Vector3.Zero; // Using centered model (translated to origin in model transform)
            UpdateCameraMatrices();
        }

        /// <summary>
        /// Draw toolbar controls (buttons, checkboxes) for this viewer.
        /// </summary>
        public void DrawToolbarControls()
        {
            if (ImGui.Button("Reset Camera"))
            {
                // Reset camera parameters to default
                _cameraYaw = -MathF.PI / 4f;
                _cameraPitch = MathF.PI / 6f;
                _cameraDistance = 2.0f;
                _cameraTarget = Vector3.Zero;
                UpdateCameraMatrices();
            }
            ImGui.SameLine();
            bool grid = _showGrid;
            if (ImGui.Checkbox("Grid", ref grid))
            {
                _showGrid = grid;
            }
            
            // Show backend info in toolbar
            ImGui.SameLine();
            ImGui.TextDisabled($"[{VeldridManager.GraphicsDevice.BackendType}]");
        }

        /// <summary>
        /// Draw the main content (3D view) of the viewer. Responds to zoom, pan interactions.
        /// </summary>
        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            // Render the scene to the off-screen texture
            RenderScene();

            // Display the rendered image in ImGui
            IntPtr textureId = _textureManager.GetImGuiTextureId();
            if (textureId != IntPtr.Zero)
            {
                Vector2 availableSize = ImGui.GetContentRegionAvail();
                Vector2 imagePos = ImGui.GetCursorScreenPos();
                
                // Place an invisible button over the image to capture mouse input
                // This prevents the window from being dragged when clicking on the 3D view
                ImGui.InvisibleButton("Mesh3DViewArea", availableSize);
                bool isHovered = ImGui.IsItemHovered();
                bool isActive = ImGui.IsItemActive();
                
                // Draw the image at the same position as the invisible button
                ImGui.SetCursorScreenPos(imagePos);
                // Flip UVs vertically (Veldrid's texture coordinate difference)
                ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));
                
                // Handle mouse interaction if hovering the invisible button area
                if (isHovered || isActive || _isDragging || _isPanning)
                {
                    HandleMouseInput();
                }
                
                // Right-click context menu for additional options
                if (ImGui.BeginPopupContextItem("Mesh3DViewerContextMenu"))
                {
                    if (ImGui.MenuItem("Reset Camera")) 
                    {
                        _cameraYaw = -MathF.PI / 4f;
                        _cameraPitch = MathF.PI / 6f;
                        _cameraDistance = 2.0f;
                        _cameraTarget = Vector3.Zero;
                        UpdateCameraMatrices();
                    }
                    if (ImGui.MenuItem("Toggle Grid", null, _showGrid))
                    {
                        _showGrid = !_showGrid;
                    }
                    ImGui.EndPopup();
                }
            }
        }

        /// <summary>
        /// Dispose of GPU resources used by the viewer.
        /// </summary>
        public void Dispose()
        {
            _textureManager?.Dispose();
            _d3dRenderer?.Dispose();
            _metalRenderer?.Dispose();
            _vulkanRenderer?.Dispose();
        }

        /// <summary>
        /// Render the 3D scene (all models and grid) to the off-screen framebuffer.
        /// </summary>
        private void RenderScene()
        {
            if (_d3dRenderer != null)
            {
                // Use Direct3D11 renderer
                _d3dRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
            }
            else if (_metalRenderer != null)
            {
                // Use Metal renderer
                _metalRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
            }
            else if (_vulkanRenderer != null)
            {
                // Use Vulkan renderer
                _vulkanRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
            }
        }

        /// <summary>
        /// Update _cameraPosition, _viewMatrix, and _projMatrix based on yaw, pitch, distance.
        /// </summary>
        private void UpdateCameraMatrices()
        {
            // Calculate camera position in world from spherical angles around target (Y-up coordinate system)
            Vector3 offset;
            offset.X = MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch);
            offset.Y = MathF.Sin(_cameraPitch);
            offset.Z = MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch);
            offset *= _cameraDistance;
            Vector3 cameraPosition = _cameraTarget + offset;
            _viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, _cameraTarget, Vector3.UnitY);
            
            // 45-degree field of view, aspect ratio based on current render target size
            float aspect = 1.0f;
            if (_d3dRenderer != null)
                aspect = _d3dRenderer.Width / (float)_d3dRenderer.Height;
            else if (_metalRenderer != null)
                aspect = _metalRenderer.Width / (float)_metalRenderer.Height;
            else if (_vulkanRenderer != null)
                aspect = _vulkanRenderer.Width / (float)_vulkanRenderer.Height;
                
            _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 1000f);
        }

        /// <summary>
        /// Handle mouse input for orbit rotation (left drag), panning (middle/right drag), and zoom (wheel).
        /// </summary>
        private void HandleMouseInput()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            
            // Mouse wheel zoom
            if (io.MouseWheel != 0)
            {
                _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.5f, 20.0f);
                UpdateCameraMatrices();
            }
            
            // Check if we should start dragging
            if (!_isDragging && !_isPanning)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _isDragging = true;
                    _lastMousePos = io.MousePos;
                }
                else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _isPanning = true;
                    _lastMousePos = io.MousePos;
                }
            }
            
            // Handle ongoing drag operations
            if (_isDragging)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Vector2 delta = io.MousePos - _lastMousePos;
                    // Orbit rotation (adjust yaw and pitch)
                    _cameraYaw -= delta.X * 0.01f;
                    _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
                    _lastMousePos = io.MousePos;
                    UpdateCameraMatrices();
                }
                else
                {
                    _isDragging = false;
                }
            }
            
            if (_isPanning)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || ImGui.IsMouseDown(ImGuiMouseButton.Right))
                {
                    Vector2 delta = io.MousePos - _lastMousePos;
                    // Pan: translate camera target in view plane
                    Matrix4x4.Invert(_viewMatrix, out Matrix4x4 invView);
                    // Right vector (world) and Up vector (world) from inverse view
                    Vector3 right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13));
                    Vector3 up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23));
                    float panSpeed = _cameraDistance * 0.001f;
                    _cameraTarget -= right * delta.X * panSpeed;
                    _cameraTarget += up * delta.Y * panSpeed;
                    _lastMousePos = io.MousePos;
                    UpdateCameraMatrices();
                }
                else
                {
                    _isPanning = false;
                }
            }
        }
    }
}