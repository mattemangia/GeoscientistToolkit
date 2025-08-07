// GeoscientistToolkit/Data/Mesh3D/Mesh3DTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Data.Mesh3D
{
    /// <summary>
    /// Provides transformation tools (scale, rotation) for Mesh3DDataset in the Tools panel.
    /// </summary>
    public class Mesh3DTools : IDatasetTools
    {
        private static readonly Dictionary<Mesh3DDataset, Vector3> _rotationByDataset = new();

        public void Draw(Dataset dataset)
        {
            if (dataset is not Mesh3DDataset mesh) return;

            if (!_rotationByDataset.ContainsKey(mesh))
                _rotationByDataset[mesh] = Vector3.Zero;

            Vector3 rot = _rotationByDataset[mesh];
            float scale = mesh.Scale;

            if (ImGui.CollapsingHeader("Transform Tools", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                if (ImGui.SliderFloat("Scale", ref scale, 0.01f, 10f, "%.2f×"))
                    mesh.Scale = scale;

                if (ImGui.SliderFloat3("Rotation (X/Y/Z)", ref rot, -180f, 180f, "%.0f°"))
                    _rotationByDataset[mesh] = rot;

                if (ImGui.Button("Reset Transform"))
                {
                    mesh.Scale = 1.0f;
                    _rotationByDataset[mesh] = Vector3.Zero;
                }

                ImGui.Unindent();
            }

            if (!mesh.IsLoaded)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Model not loaded");
                if (ImGui.Button("Load Model"))
                {
                    mesh.Load();
                }
            }
        }

        public static Vector3 GetRotation(Mesh3DDataset dataset)
        {
            if (_rotationByDataset.TryGetValue(dataset, out var rot))
                return rot;
            return Vector3.Zero;
        }
    }
}
