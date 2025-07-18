// GeoscientistToolkit/UI/MainWindow.cs — safeguarded + correct multi-panel docking
// -----------------------------------------------------------------------------
// ✔  Compiles on *any* ImGui.NET build:
//      •  If DockBuilder symbols are present **and** IMGUI_HAS_DOCK_BUILDER is
//         defined → automatic 4-way split (Datasets-left, Properties-right, Log-bottom).
//      •  Otherwise → program launches with panels floating; user can arrange
//         manually. (No compiler errors.)
// ✔  Resets visibility flags every time the layout is rebuilt so all three
//    panels appear even if you previously closed them.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class MainWindow
    {
        // ──────────────────────────────────────────────────────────────────────
        // Fields & state
        // ──────────────────────────────────────────────────────────────────────
        private readonly DatasetPanel    _datasets   = new();
        private readonly PropertiesPanel _properties = new();
        private readonly LogPanel        _log        = new();
        private readonly ImportDataModal _import     = new();
        private readonly ToolsPanel      _tools      = new();

        private readonly List<DatasetViewPanel> _viewers = new();

        private bool _layoutBuilt;
        private bool _showDatasets   = true;
        private bool _showProperties = true;
        private bool _showLog        = true;
        private bool _showTools      = true;
        private bool _showWelcome    = true;

        private Dataset? _selectedDataset;

        // ──────────────────────────────────────────────────────────────────────
        // Per-frame entry
        // ──────────────────────────────────────────────────────────────────────
        public void SubmitUI()
        {
            var vp = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(vp.WorkPos);
            ImGui.SetNextWindowSize(vp.WorkSize);
            ImGui.SetNextWindowViewport(vp.ID);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,  Vector2.Zero);

            const ImGuiWindowFlags rootFlags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
                                               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
                                               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                               ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.MenuBar;

            ImGui.Begin("GeoscientistToolkit DockSpace", rootFlags);
            ImGui.PopStyleVar(3);

            SubmitMainMenu();

            uint dockspaceId = ImGui.GetID("RootDockSpace");
            ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);

            // On first run (or after View ➜ Reset Layout) build/rebuild the dock tree.
            if (!_layoutBuilt)
            {
                // Ensure panels start visible after a reset.
                _showDatasets   = true;
                _showProperties = true;
                _showLog        = true;
                _showTools      = true;

                TryBuildDockLayout(dockspaceId, vp.WorkSize);
                _layoutBuilt = true;
            }

            // Panels -----------------------------------------------------------
            if (_showDatasets)   _datasets.Submit(ref _showDatasets, OnDatasetSelected, () => _import.Open());
            if (_showProperties) _properties.Submit(ref _showProperties, _selectedDataset);
            if (_showLog)        _log.Submit(ref _showLog);
            if (_showTools)      _tools.Submit(ref _showTools, _selectedDataset);


            // Viewers ----------------------------------------------------------
            for (int i = _viewers.Count - 1; i >= 0; --i)
            {
                bool open = true;
                _viewers[i].Submit(ref open);
                if (!open)
                {
                    _viewers[i].Dispose(); // Clean up Veldrid resources
                    _viewers.RemoveAt(i);
                }
            }

            // Other UI ---------------------------------------------------------
            _import.Submit(); // Always submit to handle popup logic
            SubmitPopups();

            ImGui.End(); // root window
        }

        // ──────────────────────────────────────────────────────────────────────
        // DockBuilder (conditional)
        // ──────────────────────────────────────────────────────────────────────
        private static void TryBuildDockLayout(uint rootId, Vector2 size)
        {
            var io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
                io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

#if IMGUI_HAS_DOCK_BUILDER
            ImGui.DockBuilderRemoveNode(rootId);
            ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.DockSpace);
            ImGui.DockBuilderSetNodeSize(rootId, size);

            uint left   = ImGui.DockBuilderSplitNode(rootId,       ImGuiDir.Left,  0.20f, out uint rem1);
            uint right  = ImGui.DockBuilderSplitNode(rem1,         ImGuiDir.Right, 0.25f, out uint rem2);
            uint bottom = ImGui.DockBuilderSplitNode(rem2,         ImGuiDir.Down,  0.25f, out uint center);
            uint right_top = ImGui.DockBuilderSplitNode(right, ImGuiDir.Up, 0.6f, out uint right_bottom);

            ImGui.DockBuilderDockWindow("Datasets",   left);
            ImGui.DockBuilderDockWindow("Properties", right_top);
            ImGui.DockBuilderDockWindow("Tools",      right_bottom);
            ImGui.DockBuilderDockWindow("Log",        bottom);
            // Center is left for viewers.

            ImGui.DockBuilderFinish(rootId);
#else
            DockBuilderStub.WarnOnce();
#endif
        }

#if !IMGUI_HAS_DOCK_BUILDER
        private static class DockBuilderStub
        {
            private static bool _warned;
            public static void WarnOnce()
            {
                if (_warned) return;
                _warned = true;
                System.Diagnostics.Debug.WriteLine("[MainWindow] DockBuilder API not available. " +
                                                  "Panels will float — upgrade to a docking build and define IMGUI_HAS_DOCK_BUILDER.");
            }
        }
#endif

        // ──────────────────────────────────────────────────────────────────────
        // Menu-bar
        // ──────────────────────────────────────────────────────────────────────
        private void SubmitMainMenu()
        {
            if (!ImGui.BeginMenuBar()) return;

            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Project")) ProjectManager.Instance.NewProject();
                if (ImGui.MenuItem("Import Data")) _import.Open();
                ImGui.Separator();
                if (ImGui.MenuItem("Exit")) Environment.Exit(0);
                ImGui.EndMenu();
            }
            
            if (ImGui.BeginMenu("Edit"))
            {
                ImGui.MenuItem("Tools Panel", string.Empty, ref _showTools);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("Datasets Panel",   string.Empty, ref _showDatasets);
                ImGui.MenuItem("Properties Panel", string.Empty, ref _showProperties);
                ImGui.MenuItem("Log Panel",        string.Empty, ref _showLog);
                ImGui.Separator();
                if (ImGui.MenuItem("Reset Layout")) _layoutBuilt = false;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About")) ImGui.OpenPopup("About GeoscientistToolkit");
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Pop-ups & callbacks
        // ──────────────────────────────────────────────────────────────────────
        private void OnDatasetSelected(Dataset ds)
        {
            _selectedDataset = ds;
            if (_viewers.All(v => v.Dataset != ds))
                _viewers.Add(new DatasetViewPanel(ds));
        }

        private void SubmitPopups()
        {
            // Welcome once per session
            if (_showWelcome)
            {
                ImGui.OpenPopup("Welcome!");
                _showWelcome = false;
            }

            bool welcome = true;
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
            if (ImGui.BeginPopupModal("Welcome!", ref welcome, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Welcome to GeoscientistToolkit!");
                ImGui.Separator();
                ImGui.TextWrapped("Import data via File → Import Data. Use the 🔲 button to pop-out panels.");
                ImGui.Spacing();
                if (ImGui.Button("Let's go!", new Vector2(100, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }

            bool about = true;
            if (ImGui.BeginPopupModal("About GeoscientistToolkit", ref about, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("GeoscientistToolkit – Preview Build");
                ImGui.Separator();
                ImGui.TextWrapped("Open-source toolkit for geoscience data visualisation, built with Veldrid + ImGui.NET.");
                ImGui.Spacing();
                if (ImGui.Button("OK", new Vector2(100, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }
    }
}