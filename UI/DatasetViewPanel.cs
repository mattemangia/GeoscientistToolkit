// GeoscientistToolkit/UI/DatasetViewPanel.cs (Updated with Status Bar Background)

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Borehole;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

// For Math

namespace GeoscientistToolkit.UI;

public class DatasetViewPanel : BasePanel
{
    private readonly IDatasetViewer _viewer;
    private Vector2 _pan = Vector2.Zero;

    private float _zoom = 1.0f;

    public DatasetViewPanel(Dataset dataset) : base(dataset.Name, new Vector2(800, 600))
    {
        Dataset = dataset;
        _viewer = DatasetUIFactory.CreateViewer(dataset);
    }

    public Dataset Dataset { get; }

    public static void CloseViewFor(Dataset datasetToClose)
    {
        foreach (var panel in AllPanels.ToList())
            if (panel is DatasetViewPanel dvp && dvp.Dataset == datasetToClose)
                dvp.Close();
    }

    public void Submit(ref bool pOpen)
    {
        base.Submit(ref pOpen);
    }

    protected override void DrawContent()
    {
        if (_viewer == null) return;

        // --- FIX START ---
        // Removed the section that notified the viewer of the pop-out state.
        // This allows child panels to manage their own state.
        // --- FIX END ---

        _viewer.DrawToolbarControls();
        ImGui.Separator();

        var contentSize = ImGui.GetContentRegionAvail();

        if (_viewer is BoreholeViewer boreholeViewer && boreholeViewer.ShowLegend)
        {
            if (ImGui.BeginTable("DatasetViewerLayout", 2, ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Viewer", ImGuiTableColumnFlags.WidthStretch, 3f);
                ImGui.TableSetupColumn("Legend", ImGuiTableColumnFlags.WidthStretch, 1.2f);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.BeginChild("ViewerContent", new Vector2(0, contentSize.Y));
                _viewer.DrawContent(ref _zoom, ref _pan);
                ImGui.EndChild();

                ImGui.TableSetColumnIndex(1);
                boreholeViewer.DrawLegendPanel(new Vector2(0, contentSize.Y));

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.BeginChild("ViewerContent", contentSize);
            _viewer.DrawContent(ref _zoom, ref _pan);
            ImGui.EndChild();
        }
    }

    private void DrawToolbar()
    {
        _viewer.DrawToolbarControls();

        // Add some spacing if the viewer has controls
        if (ImGui.GetCursorPosX() > 10) // Simple check if anything was drawn
        {
            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
        }

        if (ImGui.Button("-", new Vector2(25, 0))) _zoom = Math.Max(0.1f, _zoom - 0.1f);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##zoom", ref _zoom, 0.1f, 10.0f, "%.1fx");
        ImGui.SameLine();
        if (ImGui.Button("+", new Vector2(25, 0))) _zoom = Math.Min(10.0f, _zoom + 0.1f);
        ImGui.SameLine();
        if (ImGui.Button("Fit", new Vector2(40, 0)))
        {
            _zoom = 1.0f;
            _pan = Vector2.Zero;
        }
    }

    private void DrawStatusBar()
    {
        ImGui.Separator();

        // Prepare text content
        var statusBarText = $"Dataset: {Dataset.Name} | Type: {Dataset.Type} | Zoom: {_zoom:F1}Ã—";
        if (Dataset is CtImageStackDataset ct && ct.Width > 0)
            statusBarText += $" | Size: {ct.Width}Ã—{ct.Height}Ã—{ct.Depth}";

        // --- Draw a background for the status bar text ---
        var padding = 4f;
        var lineHeight = ImGui.GetTextLineHeight();
        var contentRegionAvail = ImGui.GetContentRegionAvail();

        var startPos = ImGui.GetCursorScreenPos();
        var bgSize = new Vector2(contentRegionAvail.X, lineHeight + padding * 2);

        // Draw background rectangle using a standard theme color for consistency
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(startPos, startPos + bgSize, ImGui.GetColorU32(ImGuiCol.FrameBg));

        // Draw the text on top of the background, with padding
        drawList.AddText(startPos + new Vector2(padding, padding), ImGui.GetColorU32(ImGuiCol.Text), statusBarText);

        // Advance the ImGui cursor manually since we used the low-level draw list
        ImGui.Dummy(bgSize);
    }


    public override void Dispose()
    {
        _viewer?.Dispose();
        base.Dispose();
    }
}