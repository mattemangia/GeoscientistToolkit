using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Microsoft.ML.OnnxRuntime;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace GeoscientistToolkit.UI.Diagnostics;

public sealed class DiagnosticApp
{
    private readonly DiagnosticOptions _options;
    private readonly List<LogEntry> _logEntries = new();
    private readonly object _logLock = new();
    private bool _exitRequested;
    private bool _scrollToBottom;
    private bool _diagnosticsStarted;
    private CancellationTokenSource _cancellation;
    private Process _runningProcess;

    private GraphicsDevice _graphicsDevice;
    private CommandList _commandList;
    private ImGuiController _imGuiController;
    private Sdl2Window _window;

    public DiagnosticApp(DiagnosticOptions options)
    {
        _options = options;
    }

    public void Run()
    {
        CreateWindow();

        _commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        _imGuiController = new ImGuiController(
            _graphicsDevice,
            _graphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            _window.Width,
            _window.Height);

        while (_window.Exists && !_exitRequested)
        {
            var snapshot = _window.PumpEvents();
            _imGuiController.Update(1f / 60f, snapshot);

            if (!_diagnosticsStarted)
            {
                _diagnosticsStarted = true;
                _cancellation = new CancellationTokenSource();
                _ = RunDiagnosticsAsync(_cancellation.Token);
            }

            DrawUi();

            _commandList.Begin();
            _commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.08f, 0.08f, 0.1f, 1f));
            _imGuiController.Render(_graphicsDevice, _commandList);
            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
        }

        _cancellation?.Cancel();
        TryStopProcess();

        _graphicsDevice.WaitForIdle();
        _imGuiController.Dispose();
        _commandList.Dispose();
        _graphicsDevice.Dispose();
    }

    private async Task RunDiagnosticsAsync(CancellationToken token)
    {
        LogInfo("Diagnostics started.");

        if (_options.RunAiDiagnostic)
        {
            LogInfo("Running AI diagnostic...");
            await RunAiDiagnosticAsync(token);
        }

        if (_options.RunGuiDiagnostic)
        {
            LogInfo("Running GUI diagnostic...");
            await RunGuiDiagnosticAsync(token);
        }

        if (_options.RunTests)
        {
            LogInfo("Running test suite...");
            await RunTestDiagnosticsAsync(token);
        }

        LogInfo("Diagnostics finished.");
    }

    private async Task RunAiDiagnosticAsync(CancellationToken token)
    {
        try
        {
            var settings = AISegmentationSettings.Instance;
            var models = new Dictionary<string, string>
            {
                { "SAM2 Encoder", settings.Sam2EncoderPath },
                { "SAM2 Decoder", settings.Sam2DecoderPath },
                { "MicroSAM Encoder", settings.MicroSamEncoderPath },
                { "MicroSAM Decoder", settings.MicroSamDecoderPath },
                { "Grounding DINO", settings.GroundingDinoModelPath }
            };

            foreach (var (name, path) in models)
            {
                if (token.IsCancellationRequested) return;

                if (!File.Exists(path))
                {
                    LogError($"Missing model file: {name} ({path})");
                    continue;
                }

                LogInfo($"Loading model: {name}");
                using var sessionOptions = new SessionOptions();
                using var session = new InferenceSession(path, sessionOptions);
                LogInfo($"Loaded model: {name}");
            }

            var vocabPath = settings.GroundingDinoVocabPath;
            if (!File.Exists(vocabPath))
                LogError($"Missing vocab file: Grounding DINO ({vocabPath})");
            else
                LogInfo("Found Grounding DINO vocab file.");
        }
        catch (Exception ex)
        {
            LogError($"AI diagnostic failed: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private async Task RunGuiDiagnosticAsync(CancellationToken token)
    {
        try
        {
            LogInfo($"Graphics backend: {_graphicsDevice.BackendType}");
            LogInfo($"Device type: {_graphicsDevice.GetType().Name}");

            var factory = _graphicsDevice.ResourceFactory;
            using var texture = factory.CreateTexture(TextureDescription.Texture2D(
                4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            _graphicsDevice.UpdateTexture(texture, new RgbaByte[]
            {
                new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255), new(255, 255, 255, 255),
                new(255, 255, 0, 255), new(0, 255, 255, 255), new(255, 0, 255, 255), new(128, 128, 128, 255),
                new(0, 0, 0, 255), new(64, 64, 64, 255), new(128, 0, 0, 255), new(0, 128, 0, 255),
                new(0, 0, 128, 255), new(128, 128, 0, 255), new(0, 128, 128, 255), new(128, 0, 128, 255)
            }, 0, 0, 0, 4, 4, 1, 0, 0);

            LogInfo("2D texture upload succeeded.");

            using var offscreenColor = factory.CreateTexture(TextureDescription.Texture2D(
                64, 64, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, offscreenColor));

            var shaders = CreateDiagnosticShaders(factory);
            using var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = DepthStencilStateDescription.Disabled,
                RasterizerState = new RasterizerStateDescription(
                    FaceCullMode.None,
                    PolygonFillMode.Solid,
                    FrontFace.CounterClockwise,
                    true,
                    false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = Array.Empty<ResourceLayout>(),
                ShaderSet = new ShaderSetDescription(
                    new[] { new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4)) },
                    shaders),
                Outputs = framebuffer.OutputDescription
            });

            using var vertexBuffer = factory.CreateBuffer(new BufferDescription(3 * 6 * sizeof(float), BufferUsage.VertexBuffer));
            var vertices = new[]
            {
                new DiagnosticVertex(new Vector2(-0.5f, -0.5f), new Vector4(1f, 0f, 0f, 1f)),
                new DiagnosticVertex(new Vector2(0.5f, -0.5f), new Vector4(0f, 1f, 0f, 1f)),
                new DiagnosticVertex(new Vector2(0f, 0.5f), new Vector4(0f, 0f, 1f, 1f))
            };

            _graphicsDevice.UpdateBuffer(vertexBuffer, 0, vertices);

            _commandList.Begin();
            _commandList.SetFramebuffer(framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1f));
            _commandList.SetPipeline(pipeline);
            _commandList.SetVertexBuffer(0, vertexBuffer);
            _commandList.Draw(3);
            _commandList.End();
            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.WaitForIdle();

            foreach (var shader in shaders) shader.Dispose();

            LogInfo("3D render pipeline draw succeeded.");
        }
        catch (Exception ex)
        {
            LogError($"GUI diagnostic failed: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private Shader[] CreateDiagnosticShaders(ResourceFactory factory)
    {
        var vertexShaderGlsl = @"
#version 450
layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;
layout(location = 0) out vec4 fsin_Color;
void main()
{
    fsin_Color = Color;
    gl_Position = vec4(Position, 0, 1);
}
";

        var fragmentShaderGlsl = @"
#version 450
layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;
void main()
{
    fsout_Color = fsin_Color;
}
";

        var options = new CrossCompileOptions();
        if (_graphicsDevice.BackendType is GraphicsBackend.Metal or GraphicsBackend.Direct3D11)
        {
            options.FixClipSpaceZ = true;
            options.InvertVertexOutputY = false;
        }

        return factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexShaderGlsl), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentShaderGlsl), "main"),
            options);
    }

    private async Task RunTestDiagnosticsAsync(CancellationToken token)
    {
        var command = BuildTestCommand();
        if (command == null)
        {
            LogError("Test run failed: unable to locate compiled tests or project files.");
            return;
        }

        var testArgs = command.Arguments;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = testArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = command.WorkingDirectory
        };
        ApplyTestHostArchitecture(startInfo);

        LogInfo($"Executing: dotnet {testArgs}");

        try
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _runningProcess = process;

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data)) LogInfo(args.Data);
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data)) LogError(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!process.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    TryStopProcess();
                    LogError("Test run canceled.");
                    return;
                }

                await Task.Delay(200, token);
            }

            LogInfo($"Test run completed with exit code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            LogError($"Test run failed: {ex.Message}");
        }
        finally
        {
            _runningProcess = null;
        }
    }

    private TestRunCommand? BuildTestCommand()
    {
        var packagedTestAssembly = ResolvePackagedTestAssemblyPath();
        var builder = new StringBuilder();
        string workingDirectory;

        if (!string.IsNullOrWhiteSpace(packagedTestAssembly))
        {
            builder.Append($"vstest \"{packagedTestAssembly}\"");
            workingDirectory = Path.GetDirectoryName(packagedTestAssembly) ?? AppContext.BaseDirectory;
        }
        else
        {
            var projectPath = Path.Combine("Tests", "VerificationTests", "VerificationTests.csproj");
            if (!File.Exists(projectPath))
                return null;

            builder.Append($"test {projectPath}");
            workingDirectory = Directory.GetCurrentDirectory();
        }

        if (_options.TestFilters is { Length: > 0 })
        {
            var filter = string.Join("|", _options.TestFilters.Select(t => $"FullyQualifiedName~{t}"));
            if (!string.IsNullOrWhiteSpace(packagedTestAssembly))
                builder.Append($" --TestCaseFilter:\"{filter}\"");
            else
                builder.Append($" --filter \"{filter}\"");
        }

        return new TestRunCommand(builder.ToString(), workingDirectory);
    }

    private static string? ResolvePackagedTestAssemblyPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "VerificationTests", "VerificationTests.dll"),
            Path.Combine(baseDirectory, "Tests", "VerificationTests", "VerificationTests.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void ApplyTestHostArchitecture(ProcessStartInfo startInfo)
    {
        if (startInfo.Environment.ContainsKey("VSTEST_HOST_ARCHITECTURE"))
            return;

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        startInfo.Environment["VSTEST_HOST_ARCHITECTURE"] = architecture;
    }

    private void DrawUi()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.Begin("Diagnostics", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        if (ImGui.Button("Close"))
        {
            _exitRequested = true;
            _window.Close();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel"))
        {
            _cancellation?.Cancel();
            TryStopProcess();
        }

        ImGui.Separator();

        ImGui.BeginChild("##diagnostic_log", new Vector2(0, 0), ImGuiChildFlags.Border);
        lock (_logLock)
        {
            foreach (var entry in _logEntries)
            {
                if (entry.IsError)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.25f, 0.25f, 1f));
                    ImGui.TextUnformatted(entry.Message);
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.TextUnformatted(entry.Message);
                }
            }
        }

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void CreateWindow()
    {
        var windowInfo = new WindowCreateInfo
        {
            X = 50,
            Y = 50,
            WindowWidth = 1400,
            WindowHeight = 900,
            WindowTitle = "GeoscientistToolkit Diagnostics"
        };

        GraphicsDeviceOptions options;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            options = new GraphicsDeviceOptions(
                false,
                PixelFormat.D24_UNorm_S8_UInt,
                true,
                ResourceBindingModel.Default,
                preferStandardClipSpaceYDirection: false,
                preferDepthRangeZeroToOne: false);
        }
        else
        {
            options = new GraphicsDeviceOptions(
                true,
                null,
                true,
                ResourceBindingModel.Improved,
                preferStandardClipSpaceYDirection: true,
                preferDepthRangeZeroToOne: true);
        }

        var backend = GetPlatformDefaultBackend();
        VeldridStartup.CreateWindowAndGraphicsDevice(windowInfo, options, backend, out _window, out _graphicsDevice);
    }

    private GraphicsBackend GetPlatformDefaultBackend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GraphicsBackend.Direct3D11;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return GraphicsBackend.Metal;
        return GraphicsBackend.Vulkan;
    }

    private void LogInfo(string message) => Log(message, false);

    private void LogError(string message) => Log(message, true);

    private void Log(string message, bool isError)
    {
        lock (_logLock)
        {
            _logEntries.Add(new LogEntry(message, isError));
            _scrollToBottom = true;
        }
    }

    private void TryStopProcess()
    {
        try
        {
            if (_runningProcess is { HasExited: false })
            {
                _runningProcess.Kill(entireProcessTree: true);
                _runningProcess.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to stop process: {ex.Message}");
        }
    }

    private readonly record struct LogEntry(string Message, bool IsError);

    private sealed record TestRunCommand(string Arguments, string WorkingDirectory);

    private readonly struct DiagnosticVertex
    {
        public DiagnosticVertex(Vector2 position, Vector4 color)
        {
            Position = position;
            Color = color;
        }

        public Vector2 Position { get; }
        public Vector4 Color { get; }
    }
}
