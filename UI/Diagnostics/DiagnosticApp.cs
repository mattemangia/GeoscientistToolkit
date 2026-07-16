using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using GAIA.Tools.CtImageStack.AISegmentation;
using GAIA.UI.OpenTk;
using GAIA.Util;
using ImGuiNET;
using Microsoft.ML.OnnxRuntime;
using OpenTK.Graphics.OpenGL;
using Vector2i = OpenTK.Mathematics.Vector2i;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace GAIA.UI.Diagnostics;

/// <summary>OpenTK diagnostic console for AI, GUI/OpenGL and verification tests.</summary>
public sealed class DiagnosticApp : GameWindow
{
    private readonly DiagnosticOptions _options;
    private readonly ConcurrentQueue<(string Message,bool Error)> _messages = new();
    private readonly List<(string Message,bool Error)> _visible = new();
    private ImGuiController _imGui;
    private CancellationTokenSource _cancellation;
    private Process _process;
    private bool _started, _finished;

    public DiagnosticApp(DiagnosticOptions options) : base(GameWindowSettings.Default,new NativeWindowSettings
    {
        ClientSize=new Vector2i(1000,700),Title="GAIA Diagnostics — OpenTK",APIVersion=new Version(3,3),
        Profile=ContextProfile.Core,Flags=ContextFlags.ForwardCompatible
    }) => _options=options;

    protected override void OnLoad()
    {
        base.OnLoad();_imGui=new ImGuiController(ClientSize.X,ClientSize.Y,FramebufferSize.X,FramebufferSize.Y);
        OpenTkManager.MainWindow=this;OpenTkManager.ImGuiController=_imGui;TextInput+=OnTextInput;
        _cancellation=new CancellationTokenSource();
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);_imGui.Update(this,(float)Math.Max(args.Time,1e-6));
        if(!_started){_started=true;_=RunDiagnosticsAsync(_cancellation.Token);}
        while(_messages.TryDequeue(out var message))_visible.Add(message);
        DrawUi();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,FramebufferSize.X,FramebufferSize.Y);
        GL.ClearColor(.07f,.075f,.095f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
        _imGui.Render();SwapBuffers();
    }

    private void DrawUi()
    {
        ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new Vector2(ClientSize.X,ClientSize.Y));
        ImGui.Begin("Diagnostics",ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoResize);
        if(ImGui.Button(_finished?"Close":"Cancel")){if(_finished)Close();else{_cancellation.Cancel();StopProcess();}}
        ImGui.SameLine();ImGui.Text(_finished?"Completed":"Running diagnostics…");ImGui.Separator();
        ImGui.BeginChild("log",new Vector2(0,0),ImGuiChildFlags.Border);
        foreach(var (text,error) in _visible){if(error)ImGui.PushStyleColor(ImGuiCol.Text,new Vector4(1,.3f,.3f,1));ImGui.TextWrapped(text);if(error)ImGui.PopStyleColor();}
        if(_messages.Count>0)ImGui.SetScrollHereY(1);ImGui.EndChild();ImGui.End();
    }

    private async Task RunDiagnosticsAsync(CancellationToken token)
    {
        Log("OpenTK diagnostics started.");
        try
        {
            if(_options.RunGuiDiagnostic){OpenTkGraphicsSelfTest.RunOrThrow();Log($"OpenGL {GL.GetString(StringName.Version)} — {GL.GetString(StringName.Renderer)}");}
            if(_options.RunAiDiagnostic)await RunAiDiagnosticAsync(token);
            if(_options.RunTests)await RunTestsAsync(token);
            Log("Diagnostics completed successfully.");
        }
        catch(OperationCanceledException){Log("Diagnostics canceled.",true);}
        catch(Exception ex){Log($"Diagnostics failed: {ex}",true);}
        finally{_finished=true;}
    }

    private Task RunAiDiagnosticAsync(CancellationToken token)
    {
        return Task.Run(()=>
        {
            token.ThrowIfCancellationRequested();
            Log($"ONNX Runtime {typeof(InferenceSession).Assembly.GetName().Version}; providers: {string.Join(", ",OrtEnv.Instance().GetAvailableProviders())}");
            var s=AISegmentationSettings.Instance;
            var models=new[]{("SAM2 encoder",s.Sam2EncoderPath),("SAM2 decoder",s.Sam2DecoderPath),("MicroSAM encoder",s.MicroSamEncoderPath),("MicroSAM decoder",s.MicroSamDecoderPath),("Grounding DINO",s.GroundingDinoModelPath)};
            foreach(var (name,path) in models)Log($"{name}: {(File.Exists(path)?"available":string.IsNullOrWhiteSpace(path)?"not configured":"missing")}",!string.IsNullOrWhiteSpace(path)&&!File.Exists(path));
        },token);
    }

    private async Task RunTestsAsync(CancellationToken token)
    {
        var project=Path.Combine("Tests","VerificationTests","VerificationTests.csproj");
        if(!File.Exists(project)){Log("Verification test project not found.",true);return;}
        var filter=_options.TestFilters is {Length:>0}?$" --filter \"{string.Join("|",_options.TestFilters.Select(x=>$"FullyQualifiedName~{x}"))}\"":"";
        var psi=new ProcessStartInfo("dotnet",$"test \"{project}\" --nologo{filter}"){WorkingDirectory=Directory.GetCurrentDirectory(),RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        _process=Process.Start(psi)??throw new InvalidOperationException("Could not start dotnet test.");
        var stdout=PumpAsync(_process.StandardOutput,false,token);var stderr=PumpAsync(_process.StandardError,true,token);
        await _process.WaitForExitAsync(token);await Task.WhenAll(stdout,stderr);
        if(_process.ExitCode!=0)throw new InvalidOperationException($"Verification tests exited with {_process.ExitCode}.");
        _process=null;
    }

    private async Task PumpAsync(StreamReader reader,bool error,CancellationToken token){while(await reader.ReadLineAsync(token) is { } line)Log(line,error);}
    private void Log(string message,bool error=false)=>_messages.Enqueue((message,error));
    private void StopProcess(){try{if(_process is {HasExited:false})_process.Kill(true);}catch{} }
    private void OnTextInput(TextInputEventArgs e)=>_imGui?.PressChar((char)e.Unicode);
    protected override void OnUnload(){_cancellation?.Cancel();StopProcess();TextInput-=OnTextInput;_imGui?.Dispose();OpenTkManager.ImGuiController=null;OpenTkManager.MainWindow=null;base.OnUnload();}
}
