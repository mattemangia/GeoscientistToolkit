// GeoscientistToolkit/UI/TableProperties.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class TableProperties : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not TableDataset tableDataset)
                return;
            
            if (ImGui.CollapsingHeader("Table Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                PropertiesPanel.DrawProperty("Source Format", tableDataset.SourceFormat);
                PropertiesPanel.DrawProperty("Rows", PropertiesPanel.FormatNumber(tableDataset.RowCount));
                PropertiesPanel.DrawProperty("Columns", PropertiesPanel.FormatNumber(tableDataset.ColumnCount));
                
                if (tableDataset.SourceFormat == "CSV" || tableDataset.SourceFormat == "TSV")
                {
                    PropertiesPanel.DrawProperty("Delimiter", 
                        tableDataset.Delimiter == "\t" ? "Tab" : 
                        tableDataset.Delimiter == ";" ? "Semicolon" : 
                        tableDataset.Delimiter);
                    PropertiesPanel.DrawProperty("Has Headers", tableDataset.HasHeaders ? "Yes" : "No");
                    PropertiesPanel.DrawProperty("Encoding", tableDataset.Encoding);
                }
                
                ImGui.Unindent();
            }
            
            if (ImGui.CollapsingHeader("Column Details"))
            {
                ImGui.Indent();
                
                if (ImGui.BeginTable("ColumnDetailsTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Name");
                    ImGui.TableSetupColumn("Type");
                    ImGui.TableHeadersRow();
                    
                    for (int i = 0; i < tableDataset.ColumnNames.Count && i < tableDataset.ColumnTypes.Count; i++)
                    {
                        ImGui.TableNextRow();
                        
                        ImGui.TableNextColumn();
                        ImGui.Text(i.ToString());
                        
                        ImGui.TableNextColumn();
                        ImGui.Text(tableDataset.ColumnNames[i]);
                        
                        ImGui.TableNextColumn();
                        string typeName = tableDataset.ColumnTypes[i].Name;
                        
                        // Color code by type
                        Vector4 color = typeName switch
                        {
                            "String" => new Vector4(0.8f, 0.8f, 0.2f, 1.0f),
                            "Int32" or "Int64" => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                            "Double" or "Single" => new Vector4(0.2f, 0.6f, 0.8f, 1.0f),
                            "Boolean" => new Vector4(0.8f, 0.2f, 0.8f, 1.0f),
                            "DateTime" => new Vector4(0.8f, 0.5f, 0.2f, 1.0f),
                            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                        };
                        
                        ImGui.TextColored(color, typeName);
                    }
                    
                    ImGui.EndTable();
                }
                
                ImGui.Unindent();
            }
            
            if (ImGui.CollapsingHeader("Quick Statistics"))
            {
                ImGui.Indent();
                
                DrawQuickStatistics(tableDataset);
                
                ImGui.Unindent();
            }
            
            if (ImGui.CollapsingHeader("Memory Usage"))
            {
                ImGui.Indent();
                
                long estimatedMemory = tableDataset.GetSizeInBytes();
                PropertiesPanel.DrawProperty("Estimated Size", PropertiesPanel.FormatFileSize(estimatedMemory));
                
                if (tableDataset.SourceFormat == "Generated")
                {
                    ImGui.TextWrapped("This is a generated table without a file backing. Size shown is estimated memory usage.");
                }
                
                ImGui.Unindent();
            }
        }
        
        private void DrawQuickStatistics(TableDataset tableDataset)
        {
            if (tableDataset.RowCount == 0)
            {
                ImGui.TextDisabled("No data available for statistics");
                return;
            }
            
            // Count numeric vs non-numeric columns
            int numericColumns = 0;
            int textColumns = 0;
            int dateColumns = 0;
            int boolColumns = 0;
            
            foreach (var type in tableDataset.ColumnTypes)
            {
                if (type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long))
                    numericColumns++;
                else if (type == typeof(string))
                    textColumns++;
                else if (type == typeof(System.DateTime))
                    dateColumns++;
                else if (type == typeof(bool))
                    boolColumns++;
            }
            
            ImGui.Text("Column Type Distribution:");
            ImGui.Indent();
            
            if (numericColumns > 0)
            {
                ImGui.BulletText($"Numeric: {numericColumns}");
            }
            if (textColumns > 0)
            {
                ImGui.BulletText($"Text: {textColumns}");
            }
            if (dateColumns > 0)
            {
                ImGui.BulletText($"Date/Time: {dateColumns}");
            }
            if (boolColumns > 0)
            {
                ImGui.BulletText($"Boolean: {boolColumns}");
            }
            
            ImGui.Unindent();
            
            // Show total cells
            long totalCells = (long)tableDataset.RowCount * tableDataset.ColumnCount;
            ImGui.Text($"Total Cells: {PropertiesPanel.FormatNumber(totalCells)}");
            
            // Density estimate (assuming some nulls exist)
            ImGui.Text($"Data Points: ~{PropertiesPanel.FormatNumber((long)(totalCells * 0.95))}");
        }
    }
}