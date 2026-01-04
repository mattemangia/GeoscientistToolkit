using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows
{
    /// <summary>
    /// Node-based visual editor for building ORC (Organic Rankine Cycle) circuits.
    /// Allows drag-and-drop placement of components and connection of fluid circuits.
    /// </summary>
    public class ORCCircuitBuilder
    {
        private bool _isOpen = true;
        private readonly List<ORCNode> _nodes = new();
        private readonly List<ORCConnection> _connections = new();

        private ORCNode? _selectedNode;
        private ORCPort? _connectingFrom;
        private Vector2 _scrollOffset = Vector2.Zero;
        private float _zoom = 1.0f;

        private int _nextNodeId = 1;

        // Node palette
        private readonly string[] _componentTypes = {
            "Heat Exchanger (Evaporator)",
            "Turbine/Expander",
            "Condenser",
            "Pump",
            "Recuperator",
            "Separator",
            "Accumulator",
            "Valve"
        };

        public bool IsOpen => _isOpen;

        public ORCCircuitBuilder()
        {
            // Create default ORC cycle template
            CreateDefaultORCCycle();
        }

        private void CreateDefaultORCCycle()
        {
            // Create a basic ORC cycle as starting template
            var evaporator = CreateNode(ORCComponentType.Evaporator, new Vector2(100, 200));
            var turbine = CreateNode(ORCComponentType.Turbine, new Vector2(350, 150));
            var condenser = CreateNode(ORCComponentType.Condenser, new Vector2(550, 200));
            var pump = CreateNode(ORCComponentType.Pump, new Vector2(350, 350));

            // Connect them in a cycle
            ConnectNodes(evaporator.Outputs[0], turbine.Inputs[0]); // Evap -> Turbine
            ConnectNodes(turbine.Outputs[0], condenser.Inputs[0]);   // Turbine -> Condenser
            ConnectNodes(condenser.Outputs[0], pump.Inputs[0]);      // Condenser -> Pump
            ConnectNodes(pump.Outputs[0], evaporator.Inputs[0]);     // Pump -> Evaporator
        }

        public void Draw()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("ORC Circuit Builder", ref _isOpen, ImGuiWindowFlags.MenuBar))
            {
                DrawMenuBar();
                DrawToolbar();

                // Split view: palette on left, canvas on right
                ImGui.Columns(2, "CircuitBuilderColumns", true);
                ImGui.SetColumnWidth(0, 200);

                DrawComponentPalette();

                ImGui.NextColumn();

                DrawCanvas();

                ImGui.Columns(1);

                // Properties panel at bottom
                DrawPropertiesPanel();
            }
            ImGui.End();
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New Circuit")) ClearCircuit();
                    if (ImGui.MenuItem("Load Template...")) { }
                    if (ImGui.MenuItem("Save Circuit...")) { }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Export to Simulation")) ExportToSimulation();
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Delete Selected", "Del", false, _selectedNode != null))
                        DeleteSelectedNode();
                    if (ImGui.MenuItem("Clear All Connections")) ClearConnections();
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Templates"))
                {
                    if (ImGui.MenuItem("Basic ORC Cycle")) CreateDefaultORCCycle();
                    if (ImGui.MenuItem("Recuperated ORC")) CreateRecuperatedORC();
                    if (ImGui.MenuItem("Two-Stage ORC")) CreateTwoStageORC();
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    if (ImGui.MenuItem("Reset View"))
                    {
                        _scrollOffset = Vector2.Zero;
                        _zoom = 1.0f;
                    }
                    if (ImGui.MenuItem("Fit to Window")) FitToWindow();
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }
        }

        private void DrawToolbar()
        {
            if (ImGui.Button("Validate Circuit"))
            {
                ValidateCircuit();
            }
            ImGui.SameLine();
            if (ImGui.Button("Auto-Layout"))
            {
                AutoLayoutNodes();
            }
            ImGui.SameLine();
            if (ImGui.Button("Calculate Cycle"))
            {
                CalculateCycle();
            }
            ImGui.SameLine();

            ImGui.Text($"| Zoom: {_zoom:P0}");
            ImGui.SameLine();
            if (ImGui.Button("-")) _zoom = Math.Max(0.25f, _zoom - 0.25f);
            ImGui.SameLine();
            if (ImGui.Button("+")) _zoom = Math.Min(2.0f, _zoom + 0.25f);

            ImGui.Separator();
        }

        private void DrawComponentPalette()
        {
            ImGui.Text("Components");
            ImGui.Separator();

            ImGui.BeginChild("ComponentPalette", new Vector2(0, 300));

            for (int i = 0; i < _componentTypes.Length; i++)
            {
                var componentType = (ORCComponentType)i;
                var color = GetComponentColor(componentType);

                ImGui.PushStyleColor(ImGuiCol.Button, color);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * 1.2f);

                if (ImGui.Button(_componentTypes[i], new Vector2(-1, 30)))
                {
                    // Add component at center of view
                    var pos = new Vector2(400, 300) - _scrollOffset;
                    CreateNode(componentType, pos);
                }

                ImGui.PopStyleColor(2);
            }

            ImGui.EndChild();

            ImGui.Separator();
            ImGui.Text("Instructions:");
            ImGui.TextWrapped("1. Click component to add\n2. Drag ports to connect\n3. Right-click node for options");
        }

        private void DrawCanvas()
        {
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            canvasSize.Y -= 120; // Leave space for properties

            var drawList = ImGui.GetWindowDrawList();

            // Canvas background
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.18f, 1.0f)));

            // Draw grid
            DrawGrid(drawList, canvasPos, canvasSize);

            // Handle canvas interaction
            ImGui.InvisibleButton("canvas", canvasSize);
            var canvasHovered = ImGui.IsItemHovered();

            if (canvasHovered)
            {
                // Scroll with middle mouse
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
                {
                    _scrollOffset += ImGui.GetIO().MouseDelta;
                }

                // Zoom with wheel
                var wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0)
                {
                    _zoom = Math.Clamp(_zoom + wheel * 0.1f, 0.25f, 2.0f);
                }
            }

            // Draw connections first (behind nodes)
            foreach (var conn in _connections)
            {
                DrawConnection(drawList, canvasPos, conn);
            }

            // Draw connecting line if dragging
            if (_connectingFrom != null)
            {
                var startPos = canvasPos + (_connectingFrom.Owner.Position + _connectingFrom.Offset + _scrollOffset) * _zoom;
                var endPos = ImGui.GetMousePos();
                drawList.AddBezierCubic(startPos, startPos + new Vector2(50, 0), endPos - new Vector2(50, 0), endPos,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 0.8f)), 2.0f);
            }

            // Draw nodes
            foreach (var node in _nodes)
            {
                DrawNode(drawList, canvasPos, node, canvasHovered);
            }
        }

        private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            float gridStep = 50 * _zoom;
            uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.5f));

            for (float x = (_scrollOffset.X * _zoom) % gridStep; x < canvasSize.X; x += gridStep)
            {
                drawList.AddLine(canvasPos + new Vector2(x, 0), canvasPos + new Vector2(x, canvasSize.Y), gridColor);
            }
            for (float y = (_scrollOffset.Y * _zoom) % gridStep; y < canvasSize.Y; y += gridStep)
            {
                drawList.AddLine(canvasPos + new Vector2(0, y), canvasPos + new Vector2(canvasSize.X, y), gridColor);
            }
        }

        private void DrawNode(ImDrawListPtr drawList, Vector2 canvasPos, ORCNode node, bool canvasHovered)
        {
            var nodePos = canvasPos + (node.Position + _scrollOffset) * _zoom;
            var nodeSize = node.Size * _zoom;

            // Node background
            var bgColor = GetComponentColor(node.ComponentType);
            if (_selectedNode == node)
                bgColor = new Vector4(bgColor.X + 0.2f, bgColor.Y + 0.2f, bgColor.Z + 0.2f, 1.0f);

            drawList.AddRectFilled(nodePos, nodePos + nodeSize, ImGui.ColorConvertFloat4ToU32(bgColor), 8.0f);
            drawList.AddRect(nodePos, nodePos + nodeSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f)), 8.0f, ImDrawFlags.None, 2.0f);

            // Node title
            drawList.AddText(nodePos + new Vector2(10, 5) * _zoom, 0xFFFFFFFF, node.Name);

            // Node parameters (simplified)
            if (node.Parameters.Count > 0)
            {
                float yOffset = 25 * _zoom;
                foreach (var param in node.Parameters)
                {
                    drawList.AddText(nodePos + new Vector2(10, yOffset), 0xFFCCCCCC, $"{param.Key}: {param.Value:F1}");
                    yOffset += 15 * _zoom;
                }
            }

            // Draw ports
            foreach (var port in node.Inputs)
            {
                var portPos = nodePos + port.Offset * _zoom;
                var portColor = port.IsConnected ? new Vector4(0.2f, 0.8f, 0.2f, 1.0f) : new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                drawList.AddCircleFilled(portPos, 6 * _zoom, ImGui.ColorConvertFloat4ToU32(portColor));

                // Check for port interaction
                if (Vector2.Distance(ImGui.GetMousePos(), portPos) < 10 * _zoom)
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _connectingFrom != null && _connectingFrom.IsOutput)
                    {
                        ConnectNodes(_connectingFrom, port);
                        _connectingFrom = null;
                    }
                }
            }

            foreach (var port in node.Outputs)
            {
                var portPos = nodePos + port.Offset * _zoom;
                var portColor = port.IsConnected ? new Vector4(0.8f, 0.2f, 0.2f, 1.0f) : new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                drawList.AddCircleFilled(portPos, 6 * _zoom, ImGui.ColorConvertFloat4ToU32(portColor));

                // Check for port interaction
                if (Vector2.Distance(ImGui.GetMousePos(), portPos) < 10 * _zoom)
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _connectingFrom = port;
                    }
                }
            }

            // Node drag
            if (canvasHovered && ImGui.IsMouseHoveringRect(nodePos, nodePos + nodeSize))
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectedNode = node;
                }
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && _selectedNode == node && _connectingFrom == null)
                {
                    node.Position += ImGui.GetIO().MouseDelta / _zoom;
                }

                // Right-click context menu
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _selectedNode = node;
                    ImGui.OpenPopup("NodeContextMenu");
                }
            }
        }

        private void DrawConnection(ImDrawListPtr drawList, Vector2 canvasPos, ORCConnection conn)
        {
            var startPos = canvasPos + (conn.From.Owner.Position + conn.From.Offset + _scrollOffset) * _zoom;
            var endPos = canvasPos + (conn.To.Owner.Position + conn.To.Offset + _scrollOffset) * _zoom;

            // Color based on fluid state
            Vector4 color = conn.FluidState switch
            {
                FluidState.Liquid => new Vector4(0.2f, 0.4f, 1.0f, 1.0f), // Blue
                FluidState.Vapor => new Vector4(1.0f, 0.4f, 0.2f, 1.0f),  // Orange
                FluidState.TwoPhase => new Vector4(0.8f, 0.2f, 0.8f, 1.0f), // Purple
                _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
            };

            drawList.AddBezierCubic(startPos, startPos + new Vector2(50 * _zoom, 0),
                endPos - new Vector2(50 * _zoom, 0), endPos,
                ImGui.ColorConvertFloat4ToU32(color), 3.0f);

            // Arrow at end
            var dir = Vector2.Normalize(endPos - startPos);
            var perp = new Vector2(-dir.Y, dir.X);
            drawList.AddTriangleFilled(
                endPos,
                endPos - dir * 10 * _zoom + perp * 5 * _zoom,
                endPos - dir * 10 * _zoom - perp * 5 * _zoom,
                ImGui.ColorConvertFloat4ToU32(color));
        }

        private void DrawPropertiesPanel()
        {
            ImGui.BeginChild("PropertiesPanel", new Vector2(0, 100), ImGuiChildFlags.Border);

            if (_selectedNode != null)
            {
                ImGui.Text($"Selected: {_selectedNode.Name}");
                ImGui.SameLine(200);
                if (ImGui.Button("Delete")) DeleteSelectedNode();

                ImGui.Separator();

                ImGui.Columns(4, "PropsColumns", false);

                foreach (var param in _selectedNode.Parameters)
                {
                    ImGui.Text(param.Key);
                    ImGui.NextColumn();
                    float val = (float)param.Value;
                    if (ImGui.DragFloat($"##{param.Key}", ref val, 0.1f))
                    {
                        _selectedNode.Parameters[param.Key] = val;
                    }
                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Select a node to edit properties");
            }

            ImGui.EndChild();

            // Context menu
            if (ImGui.BeginPopup("NodeContextMenu"))
            {
                if (ImGui.MenuItem("Edit Properties")) { }
                if (ImGui.MenuItem("Duplicate")) DuplicateNode(_selectedNode);
                ImGui.Separator();
                if (ImGui.MenuItem("Delete")) DeleteSelectedNode();
                ImGui.EndPopup();
            }
        }

        private ORCNode CreateNode(ORCComponentType type, Vector2 position)
        {
            var node = new ORCNode
            {
                Id = _nextNodeId++,
                ComponentType = type,
                Name = GetDefaultName(type),
                Position = position,
                Size = new Vector2(150, 80)
            };

            // Add default ports based on component type
            switch (type)
            {
                case ORCComponentType.Evaporator:
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 40), "Liquid In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 40), "Vapor Out"));
                    node.Parameters["Pressure (bar)"] = 10.0;
                    node.Parameters["Pinch Point (°C)"] = 5.0;
                    break;

                case ORCComponentType.Turbine:
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 40), "Vapor In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 40), "Exhaust"));
                    node.Parameters["Efficiency (%)"] = 80.0;
                    node.Parameters["Pressure Ratio"] = 5.0;
                    break;

                case ORCComponentType.Condenser:
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 40), "Vapor In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 40), "Liquid Out"));
                    node.Parameters["Temperature (°C)"] = 30.0;
                    node.Parameters["Subcooling (°C)"] = 2.0;
                    break;

                case ORCComponentType.Pump:
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 40), "Liquid In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 40), "Pressurized"));
                    node.Parameters["Efficiency (%)"] = 75.0;
                    node.Parameters["Head (m)"] = 100.0;
                    break;

                case ORCComponentType.Recuperator:
                    node.Size = new Vector2(150, 100);
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 30), "Hot In"));
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 70), "Cold In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 30), "Hot Out"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 70), "Cold Out"));
                    node.Parameters["Effectiveness"] = 0.85;
                    break;

                default:
                    node.Inputs.Add(new ORCPort(node, false, new Vector2(0, 40), "In"));
                    node.Outputs.Add(new ORCPort(node, true, new Vector2(150, 40), "Out"));
                    break;
            }

            _nodes.Add(node);
            return node;
        }

        private void ConnectNodes(ORCPort from, ORCPort to)
        {
            if (from == null || to == null) return;
            if (!from.IsOutput || to.IsOutput) return; // Must be output -> input
            if (from.Owner == to.Owner) return; // Can't connect to self

            // Check if already connected
            foreach (var conn in _connections)
            {
                if (conn.To == to) return; // Input already connected
            }

            var connection = new ORCConnection
            {
                From = from,
                To = to,
                FluidState = FluidState.Unknown
            };

            from.IsConnected = true;
            to.IsConnected = true;
            _connections.Add(connection);
        }

        private void DeleteSelectedNode()
        {
            if (_selectedNode == null) return;

            // Remove connections
            _connections.RemoveAll(c => c.From.Owner == _selectedNode || c.To.Owner == _selectedNode);

            _nodes.Remove(_selectedNode);
            _selectedNode = null;
        }

        private void DuplicateNode(ORCNode? node)
        {
            if (node == null) return;
            var newNode = CreateNode(node.ComponentType, node.Position + new Vector2(50, 50));
            foreach (var param in node.Parameters)
            {
                newNode.Parameters[param.Key] = param.Value;
            }
        }

        private void ClearCircuit()
        {
            _nodes.Clear();
            _connections.Clear();
            _selectedNode = null;
            _nextNodeId = 1;
        }

        private void ClearConnections()
        {
            _connections.Clear();
            foreach (var node in _nodes)
            {
                foreach (var port in node.Inputs) port.IsConnected = false;
                foreach (var port in node.Outputs) port.IsConnected = false;
            }
        }

        private void CreateRecuperatedORC()
        {
            ClearCircuit();
            var evaporator = CreateNode(ORCComponentType.Evaporator, new Vector2(100, 150));
            var turbine = CreateNode(ORCComponentType.Turbine, new Vector2(350, 100));
            var recuperator = CreateNode(ORCComponentType.Recuperator, new Vector2(550, 200));
            var condenser = CreateNode(ORCComponentType.Condenser, new Vector2(350, 350));
            var pump = CreateNode(ORCComponentType.Pump, new Vector2(100, 350));

            ConnectNodes(evaporator.Outputs[0], turbine.Inputs[0]);
            ConnectNodes(turbine.Outputs[0], recuperator.Inputs[0]);
            ConnectNodes(recuperator.Outputs[0], condenser.Inputs[0]);
            ConnectNodes(condenser.Outputs[0], pump.Inputs[0]);
            ConnectNodes(pump.Outputs[0], recuperator.Inputs[1]);
            ConnectNodes(recuperator.Outputs[1], evaporator.Inputs[0]);
        }

        private void CreateTwoStageORC()
        {
            ClearCircuit();
            CreateNode(ORCComponentType.Evaporator, new Vector2(100, 100));
            CreateNode(ORCComponentType.Turbine, new Vector2(300, 80));
            CreateNode(ORCComponentType.Turbine, new Vector2(450, 120));
            CreateNode(ORCComponentType.Condenser, new Vector2(600, 200));
            CreateNode(ORCComponentType.Pump, new Vector2(300, 300));
        }

        private void ValidateCircuit()
        {
            var errors = new List<string>();

            // Check for required components
            bool hasEvaporator = _nodes.Exists(n => n.ComponentType == ORCComponentType.Evaporator);
            bool hasTurbine = _nodes.Exists(n => n.ComponentType == ORCComponentType.Turbine);
            bool hasCondenser = _nodes.Exists(n => n.ComponentType == ORCComponentType.Condenser);
            bool hasPump = _nodes.Exists(n => n.ComponentType == ORCComponentType.Pump);

            if (!hasEvaporator) errors.Add("Missing Evaporator");
            if (!hasTurbine) errors.Add("Missing Turbine");
            if (!hasCondenser) errors.Add("Missing Condenser");
            if (!hasPump) errors.Add("Missing Pump");

            // Check for unconnected ports
            foreach (var node in _nodes)
            {
                foreach (var port in node.Inputs)
                {
                    if (!port.IsConnected)
                        errors.Add($"{node.Name}: {port.Name} not connected");
                }
                foreach (var port in node.Outputs)
                {
                    if (!port.IsConnected)
                        errors.Add($"{node.Name}: {port.Name} not connected");
                }
            }

            // Show results
            string message = errors.Count == 0
                ? "Circuit is valid! All components connected."
                : $"Circuit has {errors.Count} issue(s):\n- " + string.Join("\n- ", errors);

            // Note: In real implementation, show as popup
            Console.WriteLine(message);
        }

        private void CalculateCycle()
        {
            // Simplified cycle calculation
            // In production, this would solve the thermodynamic equations
            Console.WriteLine("Calculating ORC cycle...");

            // Update connection fluid states based on component outputs
            foreach (var conn in _connections)
            {
                conn.FluidState = conn.From.Owner.ComponentType switch
                {
                    ORCComponentType.Evaporator => FluidState.Vapor,
                    ORCComponentType.Turbine => FluidState.TwoPhase,
                    ORCComponentType.Condenser => FluidState.Liquid,
                    ORCComponentType.Pump => FluidState.Liquid,
                    _ => FluidState.Unknown
                };
            }
        }

        private void AutoLayoutNodes()
        {
            // Simple auto-layout: arrange in a circle
            int count = _nodes.Count;
            float radius = 200;
            var center = new Vector2(350, 250);

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(2 * Math.PI * i / count - Math.PI / 2);
                _nodes[i].Position = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
            }
        }

        private void FitToWindow()
        {
            if (_nodes.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var node in _nodes)
            {
                minX = Math.Min(minX, node.Position.X);
                minY = Math.Min(minY, node.Position.Y);
                maxX = Math.Max(maxX, node.Position.X + node.Size.X);
                maxY = Math.Max(maxY, node.Position.Y + node.Size.Y);
            }

            _scrollOffset = new Vector2(-minX + 50, -minY + 50);
            _zoom = 1.0f;
        }

        private void ExportToSimulation()
        {
            Console.WriteLine("Exporting circuit to simulation parameters...");
            // This would export to ORCConfiguration format
        }

        private static Vector4 GetComponentColor(ORCComponentType type)
        {
            return type switch
            {
                ORCComponentType.Evaporator => new Vector4(0.8f, 0.4f, 0.2f, 1.0f),  // Orange
                ORCComponentType.Turbine => new Vector4(0.3f, 0.5f, 0.8f, 1.0f),     // Blue
                ORCComponentType.Condenser => new Vector4(0.3f, 0.7f, 0.9f, 1.0f),   // Light Blue
                ORCComponentType.Pump => new Vector4(0.6f, 0.3f, 0.7f, 1.0f),        // Purple
                ORCComponentType.Recuperator => new Vector4(0.5f, 0.7f, 0.3f, 1.0f), // Green
                ORCComponentType.Separator => new Vector4(0.6f, 0.6f, 0.3f, 1.0f),   // Yellow
                ORCComponentType.Accumulator => new Vector4(0.5f, 0.5f, 0.5f, 1.0f), // Gray
                ORCComponentType.Valve => new Vector4(0.7f, 0.3f, 0.3f, 1.0f),       // Red
                _ => new Vector4(0.4f, 0.4f, 0.4f, 1.0f)
            };
        }

        private static string GetDefaultName(ORCComponentType type)
        {
            return type switch
            {
                ORCComponentType.Evaporator => "Evaporator",
                ORCComponentType.Turbine => "Turbine",
                ORCComponentType.Condenser => "Condenser",
                ORCComponentType.Pump => "Pump",
                ORCComponentType.Recuperator => "Recuperator",
                ORCComponentType.Separator => "Separator",
                ORCComponentType.Accumulator => "Accumulator",
                ORCComponentType.Valve => "Valve",
                _ => "Component"
            };
        }
    }

    public enum ORCComponentType
    {
        Evaporator,
        Turbine,
        Condenser,
        Pump,
        Recuperator,
        Separator,
        Accumulator,
        Valve
    }

    public enum FluidState
    {
        Unknown,
        Liquid,
        Vapor,
        TwoPhase
    }

    public class ORCNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ORCComponentType ComponentType { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; } = new(150, 80);
        public List<ORCPort> Inputs { get; } = new();
        public List<ORCPort> Outputs { get; } = new();
        public Dictionary<string, double> Parameters { get; } = new();
    }

    public class ORCPort
    {
        public ORCNode Owner { get; }
        public bool IsOutput { get; }
        public Vector2 Offset { get; }
        public string Name { get; }
        public bool IsConnected { get; set; }

        public ORCPort(ORCNode owner, bool isOutput, Vector2 offset, string name)
        {
            Owner = owner;
            IsOutput = isOutput;
            Offset = offset;
            Name = name;
        }
    }

    public class ORCConnection
    {
        public ORCPort From { get; set; } = null!;
        public ORCPort To { get; set; } = null!;
        public FluidState FluidState { get; set; } = FluidState.Unknown;
    }
}
