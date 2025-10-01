// GeoscientistToolkit/UI/MaterialLibraryWindow.cs
//
// Material Library Editor (ImGui) — fixed "ref to property" issues for Name/Notes by using buffers.
// - Uses _nameBuf and _notesBuf instead of passing ref to m.Name / m.Notes.
// - Numeric fields use string buffers and parse to nullable doubles.
// - No IndexOf() — uses FindIndexByName().
// - Load/Save/Add/Duplicate/Delete, filter, custom params, sources, CSV export (SI units).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public sealed class MaterialLibraryWindow
    {
        private bool _open;
        private int _selectedIndex = -1;
        private string _filter = "";

        private string _pathBuf = "Materials.library.json";

        // The editable copy of the material
        private PhysicalMaterial _edit = new();

        // Buffers (so ImGui can take ref variables)
        private string _nameBuf = "";
        private string _notesBuf = "";
        private readonly NumericBuffers _buf = new();

        private string _newCustomKey = "";
        private string _newCustomVal = "";

        public void Open() => _open = true;

        public void Submit()
        {
            if (!_open) return;

            ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("Material Library", ref _open, ImGuiWindowFlags.NoDocking))
            {
                ImGui.End();
                return;
            }

            DrawToolbar();

            ImGui.Separator();
            float leftWidth = 320f;

            ImGui.BeginChild("left", new Vector2(leftWidth, 0), ImGuiChildFlags.Border);
            DrawList();
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("right", Vector2.Zero, ImGuiChildFlags.Border);
            DrawEditor();
            ImGui.EndChild();

            ImGui.End();
        }

        // ────────────────────────────────────────────────────────────
        // Toolbar
        // ────────────────────────────────────────────────────────────
        private void DrawToolbar()
        {
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("Path", ref _pathBuf, 512);

            ImGui.SameLine();
            if (ImGui.Button("Load"))
            {
                MaterialLibrary.Instance.SetLibraryFilePath(_pathBuf);
                MaterialLibrary.Instance.Load(_pathBuf);
                _selectedIndex = -1;
                _edit = new PhysicalMaterial();
                _nameBuf = "";
                _notesBuf = "";
                _buf.Clear();
            }

            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                MaterialLibrary.Instance.SetLibraryFilePath(_pathBuf);
                MaterialLibrary.Instance.Save(_pathBuf);
            }

            ImGui.SameLine();
            if (ImGui.Button("Export CSV"))
            {
                ExportCsv("Materials.export.csv");
            }

            ImGui.SameLine();
            ImGui.TextDisabled("|");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##filter", "Search…", ref _filter, 256);
        }

        // ────────────────────────────────────────────────────────────
        // Left list
        // ────────────────────────────────────────────────────────────
        private void DrawList()
        {
            if (ImGui.Button("+ Add New"))
            {
                var nm = UniqueName("New Material");
                var m = new PhysicalMaterial { Name = nm, IsUserMaterial = true };
                MaterialLibrary.Instance.AddOrUpdate(m);
                _selectedIndex = FindIndexByName(nm);
                CopyToEditor(m);
            }

            ImGui.SameLine();
            bool canDup = _selectedIndex >= 0 && _selectedIndex < MaterialLibrary.Instance.Materials.Count;
            if (!canDup) ImGui.BeginDisabled();
            if (ImGui.Button("Duplicate"))
            {
                var src = MaterialLibrary.Instance.Materials[_selectedIndex];
                var copy = Clone(src);
                copy.Name = UniqueName(src.Name + " Copy");
                copy.IsUserMaterial = true;
                MaterialLibrary.Instance.AddOrUpdate(copy);
                _selectedIndex = FindIndexByName(copy.Name);
                CopyToEditor(copy);
            }
            if (!canDup) ImGui.EndDisabled();

            ImGui.Separator();

            var mats = MaterialLibrary.Instance.Materials
                .Where(m => string.IsNullOrWhiteSpace(_filter) || m.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ImGui.BeginTable("matlist", 1, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Name");
                ImGui.TableHeadersRow();

                foreach (var view in mats)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    int realIdx = FindIndexByName(view.Name);
                    bool selected = realIdx == _selectedIndex;
                    if (ImGui.Selectable(view.Name, selected))
                    {
                        _selectedIndex = realIdx;
                        CopyToEditor(MaterialLibrary.Instance.Materials[_selectedIndex]);
                    }
                }
                ImGui.EndTable();
            }
        }

        // ────────────────────────────────────────────────────────────
        // Right editor
        // ────────────────────────────────────────────────────────────
        private void DrawEditor()
        {
            if (_selectedIndex < 0 || _selectedIndex >= MaterialLibrary.Instance.Materials.Count)
            {
                ImGui.TextDisabled("Select a material to edit.");
                return;
            }

            var m = _edit;

            // Name & Phase (Name uses buffer to avoid ref-to-property)
            ImGui.SetNextItemWidth(320);
            ImGui.InputText("Name", ref _nameBuf, 256);

            var phase = m.Phase;
            if (ImGui.BeginCombo("Phase", phase.ToString()))
            {
                foreach (PhaseType ph in Enum.GetValues(typeof(PhaseType)))
                {
                    bool sel = ph == phase;
                    if (ImGui.Selectable(ph.ToString(), sel))
                        m.Phase = ph;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Numeric fields via string buffers
            ImGui.Text("Core Properties");
            Numeric("Density (kg/m³)", ref _buf.Density, m.Density_kg_m3, v => m.Density_kg_m3 = v);
            Numeric("Mohs Hardness", ref _buf.Mohs, m.MohsHardness, v => m.MohsHardness = v);
            Numeric("Viscosity (Pa·s)", ref _buf.Viscosity, m.Viscosity_Pa_s, v => m.Viscosity_Pa_s = v);
            
            ImGui.Separator();
            ImGui.Text("Mechanical Properties");
            Numeric("Young Modulus (GPa)", ref _buf.YoungGPa, m.YoungModulus_GPa, v => m.YoungModulus_GPa = v);
            Numeric("Poisson Ratio", ref _buf.Nu, m.PoissonRatio, v => m.PoissonRatio = v);
            Numeric("Friction Angle (°)", ref _buf.FrictionAngle, m.FrictionAngle_deg, v => m.FrictionAngle_deg = v);
            Numeric("Compressive Strength (MPa)", ref _buf.CompressiveMPa, m.CompressiveStrength_MPa, v => m.CompressiveStrength_MPa = v);
            Numeric("Tensile Strength (MPa)", ref _buf.TensileMPa, m.TensileStrength_MPa, v => m.TensileStrength_MPa = v);
            Numeric("Yield Strength (MPa)", ref _buf.YieldMPa, m.YieldStrength_MPa, v => m.YieldStrength_MPa = v);

            ImGui.Separator();
            ImGui.Text("Thermal Properties");
            Numeric("Thermal Conductivity (W/m·K)", ref _buf.ThermalK, m.ThermalConductivity_W_mK, v => m.ThermalConductivity_W_mK = v);
            Numeric("Specific Heat Capacity (J/kg·K)", ref _buf.SpecificHeat, m.SpecificHeatCapacity_J_kgK, v => m.SpecificHeatCapacity_J_kgK = v);
            Numeric("Thermal Diffusivity (m²/s)", ref _buf.ThermalDiff, m.ThermalDiffusivity_m2_s, v => m.ThermalDiffusivity_m2_s = v);

            ImGui.Separator();
            ImGui.Text("Wettability / Porosity");
            Numeric("Contact Angle (°)", ref _buf.ContactAngle, m.TypicalWettability_contactAngle_deg, v => m.TypicalWettability_contactAngle_deg = v);
            Numeric("Porosity (fraction 0–1)", ref _buf.Porosity, m.TypicalPorosity_fraction, v => m.TypicalPorosity_fraction = v);

            ImGui.Separator();
            ImGui.Text("Acoustic Properties");
            Numeric("Vp (P-wave, m/s)", ref _buf.Vp, m.Vp_m_s, v => m.Vp_m_s = v);
            Numeric("Vs (S-wave, m/s)", ref _buf.Vs, m.Vs_m_s, v => m.Vs_m_s = v);
            Numeric("Vp/Vs Ratio", ref _buf.VpVs, m.VpVsRatio, v => m.VpVsRatio = v);
            Numeric("Acoustic Impedance (MRayl)", ref _buf.AcousticZ, m.AcousticImpedance_MRayl, v => m.AcousticImpedance_MRayl = v);

            ImGui.Separator();
            ImGui.Text("Electrical & Magnetic Properties");
            Numeric("Electrical Resistivity (Ω·m)", ref _buf.Resistivity, m.ElectricalResistivity_Ohm_m, v => m.ElectricalResistivity_Ohm_m = v);
            Numeric("Magnetic Susceptibility (SI)", ref _buf.MagSus, m.MagneticSusceptibility_SI, v => m.MagneticSusceptibility_SI = v);

            ImGui.Separator();

            // Custom parameters
            ImGui.Text("Custom Parameters");
            ImGui.SetNextItemWidth(160);
            ImGui.InputTextWithHint("Key", "e.g. BulkModulus_GPa", ref _newCustomKey, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.InputTextWithHint("Value", "numeric", ref _newCustomVal, 32);
            ImGui.SameLine();
            if (ImGui.Button("Add/Set") && !string.IsNullOrWhiteSpace(_newCustomKey))
            {
                if (TryParseNullable(_newCustomVal, out var d))
                {
                    m.Extra[_newCustomKey] = d ?? 0.0;
                    _newCustomKey = "";
                    _newCustomVal = "";
                }
            }

            if (m.Extra.Count > 0 && ImGui.BeginTable("extra", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Key");
                ImGui.TableSetupColumn("Value");
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableHeadersRow();

                foreach (var kv in m.Extra.ToList())
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(kv.Key);
                    ImGui.TableNextColumn();
                    string s = kv.Value.ToString(CultureInfo.InvariantCulture);
                    if (ImGui.InputText($"##val_{kv.Key}", ref s, 64, ImGuiInputTextFlags.CharsScientific))
                    {
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                            m.Extra[kv.Key] = dv;
                    }
                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##{kv.Key}"))
                    {
                        m.Extra.Remove(kv.Key);
                        break;
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Separator();

            // Notes uses buffer
            ImGui.Text("Notes");
            ImGui.InputTextMultiline("##notes", ref _notesBuf, 4096, new Vector2(0, 80));

            ImGui.Text("Sources (one per line)");
            string sourcesJoined = string.Join("\n", m.Sources ?? new List<string>());
            if (ImGui.InputTextMultiline("##srcs", ref sourcesJoined, 4096, new Vector2(0, 80)))
            {
                m.Sources = sourcesJoined.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                         .Select(s => s.Trim()).ToList();
            }

            ImGui.Separator();

            if (ImGui.Button("Save Changes"))
            {
                // Sync buffered text fields back to the material
                m.Name = _nameBuf;
                m.Notes = _notesBuf;

                // Auto-calculate convenience fields if possible
                if (!m.VpVsRatio.HasValue && m.Vp_m_s.HasValue && m.Vs_m_s.HasValue && m.Vs_m_s.Value > 1e-6)
                    m.VpVsRatio = m.Vp_m_s.Value / m.Vs_m_s.Value;
                if (!m.AcousticImpedance_MRayl.HasValue && m.Density_kg_m3.HasValue && m.Vp_m_s.HasValue)
                    m.AcousticImpedance_MRayl = (m.Density_kg_m3.Value * m.Vp_m_s.Value) / 1_000_000.0;
                if (!m.ThermalDiffusivity_m2_s.HasValue && m.ThermalConductivity_W_mK.HasValue && m.Density_kg_m3.HasValue && m.SpecificHeatCapacity_J_kgK.HasValue && (m.Density_kg_m3 * m.SpecificHeatCapacity_J_kgK) > 1e-6)
                    m.ThermalDiffusivity_m2_s = m.ThermalConductivity_W_mK.Value / (m.Density_kg_m3.Value * m.SpecificHeatCapacity_J_kgK.Value);

                MaterialLibrary.Instance.AddOrUpdate(Clone(m));
                _selectedIndex = FindIndexByName(m.Name);
            }

            ImGui.SameLine();
            if (ImGui.Button("Revert"))
            {
                var current = MaterialLibrary.Instance.Materials.ElementAtOrDefault(_selectedIndex);
                if (current != null) CopyToEditor(current);
            }

            ImGui.SameLine();
            bool canDelete = _selectedIndex >= 0 && _edit.IsUserMaterial;
            if (!canDelete) ImGui.BeginDisabled();
            if (ImGui.Button("Delete"))
            {
                var current = MaterialLibrary.Instance.Materials.ElementAtOrDefault(_selectedIndex);
                if (current != null)
                {
                    MaterialLibrary.Instance.Remove(current.Name);
                    _selectedIndex = -1;
                    _edit = new PhysicalMaterial();
                    _nameBuf = "";
                    _notesBuf = "";
                    _buf.Clear();
                }
            }
            if (!canDelete) ImGui.EndDisabled();
            if (!canDelete && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Default materials cannot be deleted.");
            }
        }

        // ────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────

        private static PhysicalMaterial Clone(PhysicalMaterial m)
        {
            return new PhysicalMaterial
            {
                Name = m.Name,
                Phase = m.Phase,
                Viscosity_Pa_s = m.Viscosity_Pa_s,
                MohsHardness = m.MohsHardness,
                Density_kg_m3 = m.Density_kg_m3,
                
                YoungModulus_GPa = m.YoungModulus_GPa,
                PoissonRatio = m.PoissonRatio,
                FrictionAngle_deg = m.FrictionAngle_deg,
                CompressiveStrength_MPa = m.CompressiveStrength_MPa,
                TensileStrength_MPa = m.TensileStrength_MPa,
                YieldStrength_MPa = m.YieldStrength_MPa,
                
                ThermalConductivity_W_mK = m.ThermalConductivity_W_mK,
                SpecificHeatCapacity_J_kgK = m.SpecificHeatCapacity_J_kgK,
                ThermalDiffusivity_m2_s = m.ThermalDiffusivity_m2_s,
                
                TypicalWettability_contactAngle_deg = m.TypicalWettability_contactAngle_deg,
                TypicalPorosity_fraction = m.TypicalPorosity_fraction,
                
                Vs_m_s = m.Vs_m_s,
                Vp_m_s = m.Vp_m_s,
                VpVsRatio = m.VpVsRatio,
                AcousticImpedance_MRayl = m.AcousticImpedance_MRayl,

                ElectricalResistivity_Ohm_m = m.ElectricalResistivity_Ohm_m,
                MagneticSusceptibility_SI = m.MagneticSusceptibility_SI,

                Extra = new Dictionary<string, double>(m.Extra),
                Notes = m.Notes,
                Sources = m.Sources != null ? new List<string>(m.Sources) : new List<string>(),
                IsUserMaterial = true
            };
        }

        private void CopyToEditor(PhysicalMaterial src)
        {
            _edit = Clone(src);
            _nameBuf = _edit.Name ?? "";
            _notesBuf = _edit.Notes ?? "";
            _buf.FromMaterial(_edit);
        }

        private int FindIndexByName(string name)
        {
            var list = MaterialLibrary.Instance.Materials;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private string UniqueName(string baseName)
        {
            string n = baseName;
            int i = 1;
            var mats = MaterialLibrary.Instance.Materials;
            while (mats.Any(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                n = $"{baseName} {i++}";
            }
            return n;
        }

        private static bool TryParseNullable(string s, out double? v)
        {
            if (string.IsNullOrWhiteSpace(s)) { v = null; return true; }
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                v = d;
                return true;
            }
            v = null;
            return false;
        }

        // Numeric field (string buffer -> parse -> assign action)
        private void Numeric(string label, ref string buf, double? current, Action<double?> assign)
        {
            if (buf == null) buf = current?.ToString("G", CultureInfo.InvariantCulture) ?? "";

            ImGui.SetNextItemWidth(220);
            string local = buf;
            if (ImGui.InputText(label, ref local, 64, ImGuiInputTextFlags.CharsScientific))
            {
                buf = local;
                if (TryParseNullable(local, out var val))
                {
                    assign(val);
                }
            }
        }

        private void ExportCsv(string path)
        {
            try
            {
                var cols = new[]
                {
                    "Name", "Phase", "Density_kg_m3", "MohsHardness", "Viscosity_Pa_s",
                    "YoungModulus_GPa", "PoissonRatio", "FrictionAngle_deg", "CompressiveStrength_MPa", "TensileStrength_MPa", "YieldStrength_MPa",
                    "ThermalConductivity_W_mK", "SpecificHeatCapacity_J_kgK", "ThermalDiffusivity_m2_s",
                    "ContactAngle_deg", "Porosity_fraction",
                    "Vp_m_s", "Vs_m_s", "VpVsRatio", "AcousticImpedance_MRayl",
                    "ElectricalResistivity_Ohm_m", "MagneticSusceptibility_SI"
                };

                var mats = MaterialLibrary.Instance.Materials;
                using var sw = new System.IO.StreamWriter(path);
                sw.WriteLine(string.Join(",", cols));

                foreach (var m in mats)
                {
                    string[] vals =
                    {
                        Esc(m.Name), m.Phase.ToString(), F(m.Density_kg_m3), F(m.MohsHardness), F(m.Viscosity_Pa_s),
                        F(m.YoungModulus_GPa), F(m.PoissonRatio), F(m.FrictionAngle_deg), F(m.CompressiveStrength_MPa), F(m.TensileStrength_MPa), F(m.YieldStrength_MPa),
                        F(m.ThermalConductivity_W_mK), F(m.SpecificHeatCapacity_J_kgK), F(m.ThermalDiffusivity_m2_s),
                        F(m.TypicalWettability_contactAngle_deg), F(m.TypicalPorosity_fraction),
                        F(m.Vp_m_s), F(m.Vs_m_s), F(m.VpVsRatio), F(m.AcousticImpedance_MRayl),
                        F(m.ElectricalResistivity_Ohm_m), F(m.MagneticSusceptibility_SI)
                    };
                    sw.WriteLine(string.Join(",", vals));
                }
                Logger.Log($"[MaterialLibrary] CSV exported to {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MaterialLibraryWindow] CSV export failed: {ex.Message}");
            }

            static string Esc(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
            static string F(double? v) => v.HasValue ? v.Value.ToString("G", CultureInfo.InvariantCulture) : "";
        }

        // Internal numeric buffers
        private sealed class NumericBuffers
        {
            public string Viscosity = "";
            public string Mohs = "";
            public string Density = "";
            public string ThermalK = "";
            public string Nu = "";
            public string FrictionAngle = "";
            public string YoungGPa = "";
            public string CompressiveMPa = "";
            public string TensileMPa = "";
            public string YieldMPa = "";
            public string SpecificHeat = "";
            public string ThermalDiff = "";
            public string ContactAngle = "";
            public string Porosity = "";
            public string Vs = "";
            public string Vp = "";
            public string VpVs = "";
            public string AcousticZ = "";
            public string Resistivity = "";
            public string MagSus = "";

            public void Clear()
            {
                Viscosity = Mohs = Density = ThermalK = Nu = FrictionAngle = YoungGPa = CompressiveMPa = TensileMPa = YieldMPa = "";
                SpecificHeat = ThermalDiff = ContactAngle = Porosity = "";
                Vs = Vp = VpVs = AcousticZ = Resistivity = MagSus = "";
            }

            public void FromMaterial(PhysicalMaterial m)
            {
                Clear();
                Viscosity   = S(m.Viscosity_Pa_s);
                Mohs        = S(m.MohsHardness);
                Density     = S(m.Density_kg_m3);
                ThermalK    = S(m.ThermalConductivity_W_mK);
                Nu          = S(m.PoissonRatio);
                FrictionAngle = S(m.FrictionAngle_deg);
                YoungGPa    = S(m.YoungModulus_GPa);
                CompressiveMPa = S(m.CompressiveStrength_MPa);
                TensileMPa = S(m.TensileStrength_MPa);
                YieldMPa = S(m.YieldStrength_MPa);
                SpecificHeat = S(m.SpecificHeatCapacity_J_kgK);
                ThermalDiff = S(m.ThermalDiffusivity_m2_s);
                ContactAngle = S(m.TypicalWettability_contactAngle_deg);
                Porosity     = S(m.TypicalPorosity_fraction);
                Vs  = S(m.Vs_m_s);
                Vp  = S(m.Vp_m_s);
                VpVs= S(m.VpVsRatio);
                AcousticZ = S(m.AcousticImpedance_MRayl);
                Resistivity = S(m.ElectricalResistivity_Ohm_m);
                MagSus = S(m.MagneticSusceptibility_SI);
            }

            private static string S(double? v) => v.HasValue
                ? v.Value.ToString("G", CultureInfo.InvariantCulture)
                : "";
        }
    }
}