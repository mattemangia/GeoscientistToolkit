// GeoscientistToolkit/Data/ImageStackOrganizerDialog.cs

using System.Numerics;
using System.Text.RegularExpressions;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class ImageStackOrganizerDialog
{
    // Options
    private readonly bool _autoGroupBySimilarNames = false;
    private readonly Dictionary<string, List<ImageFileInfo>> _groups = new();
    private readonly HashSet<ImageFileInfo> _selectedImages = new();

    private List<ImageFileInfo> _availableImages = new();
    private bool _createSingleGroup;
    private ImageFileInfo _lastSelectedImage;
    private string _newGroupName = "";
    private string _selectedGroup;
    private string _singleGroupName = "Image Stack";
    private string _sourceFolderPath = "";
    public bool IsOpen { get; set; }

    // --- ADDED: Fields for asynchronous loading ---
    private Task _loadingTask;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isLoading;
    private float _loadingProgress;
    private string _loadingStatus = "";
    // --- END ADDED ---

    public void Open(string folderPath)
    {
        IsOpen = true;
        _sourceFolderPath = folderPath;
        _availableImages.Clear();
        _groups.Clear();
        _selectedImages.Clear();
        _selectedGroup = null;

        // --- MODIFIED: Start asynchronous loading instead of synchronous ---
        _isLoading = true;
        _loadingProgress = 0f;
        _loadingStatus = "Initializing...";
        _cancellationTokenSource = new CancellationTokenSource();
        _loadingTask = Task.Run(() => LoadImagesFromFolderAsync(folderPath, _cancellationTokenSource.Token));
        // --- END MODIFIED ---
    }

    // --- REPLACED: Synchronous LoadImagesFromFolder with asynchronous version ---
    private void LoadImagesFromFolderAsync(string folderPath, CancellationToken token)
    {
        try
        {
            _loadingStatus = "Scanning folder for supported images...";
            _loadingProgress = 0f;

            var files = Directory.GetFiles(folderPath)
                .Where(ImageLoader.IsSupportedImageFile)
                .ToList();

            if (token.IsCancellationRequested) return;

            var loadedImages = new List<ImageFileInfo>();
            for (var i = 0; i < files.Count; i++)
            {
                if (token.IsCancellationRequested) return;

                var file = files[i];
                _loadingStatus = $"({i + 1}/{files.Count}) Loading: {Path.GetFileName(file)}";

                var fileInfo = new FileInfo(file);
                var imageInfo = ImageLoader.LoadImageInfo(file);

                loadedImages.Add(new ImageFileInfo
                {
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    FileSize = fileInfo.Length,
                    Width = imageInfo.Width,
                    Height = imageInfo.Height,
                    Modified = fileInfo.LastWriteTime
                });

                _loadingProgress = (float)(i + 1) / files.Count;
            }

            // This is an atomic operation, so it's safe to assign without a lock.
            _availableImages = loadedImages.OrderBy(img => img.FileName).ToList();

            if (_autoGroupBySimilarNames)
            {
                _loadingStatus = "Auto-grouping images...";
                AutoGroupImages();
            }

            _loadingStatus = "Finished loading.";
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading images asynchronously: {ex.Message}");
            _loadingStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
    // --- END REPLACED ---

    private void AutoGroupImages()
    {
        // Simple auto-grouping by common prefixes
        var groupedByPrefix = new Dictionary<string, List<ImageFileInfo>>();

        foreach (var img in _availableImages)
        {
            // Extract prefix (everything before first number or underscore+number)
            var match = Regex.Match(img.FileName, @"^([^0-9_]+)");
            var prefix = match.Success ? match.Groups[1].Value.Trim() : "Ungrouped";

            if (string.IsNullOrEmpty(prefix))
                prefix = "Ungrouped";

            if (!groupedByPrefix.ContainsKey(prefix))
                groupedByPrefix[prefix] = new List<ImageFileInfo>();

            groupedByPrefix[prefix].Add(img);
        }

        // Create groups
        foreach (var kvp in groupedByPrefix)
            if (kvp.Value.Count > 1) // Only create groups with multiple images
                _groups[kvp.Key] = new List<ImageFileInfo>(kvp.Value);
    }

    // --- MODIFIED: Submit method now handles the loading state ---
    public void Submit()
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        var pOpen = IsOpen;
        if (ImGui.Begin("Organize Image Stack", ref pOpen, ImGuiWindowFlags.NoDocking))
        {
            if (_isLoading)
            {
                DrawLoadingState();
            }
            else
            {
                DrawToolbar();
                ImGui.Separator();

                var bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + 10f;
                var availableHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight;

                // Use modern Table API instead of old Columns API
                var tableFlags = ImGuiTableFlags.Resizable |
                                 ImGuiTableFlags.BordersInnerV |
                                 ImGuiTableFlags.SizingFixedFit;

                if (ImGui.BeginTable("OrganizerTable", 3, tableFlags, new Vector2(0, availableHeight)))
                {
                    // Setup columns with initial widths
                    ImGui.TableSetupColumn("Available Images", ImGuiTableColumnFlags.WidthFixed, 300);
                    ImGui.TableSetupColumn("Groups", ImGuiTableColumnFlags.WidthFixed, 250);
                    ImGui.TableSetupColumn("Group Contents", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableNextRow();

                    // Column 1: Available Images
                    ImGui.TableSetColumnIndex(0);
                    DrawAvailableImages();

                    // Column 2: Groups
                    ImGui.TableSetColumnIndex(1);
                    DrawGroups();

                    // Column 3: Group Details
                    ImGui.TableSetColumnIndex(2);
                    DrawGroupDetails();

                    ImGui.EndTable();
                }

                ImGui.Separator();

                DrawBottomButtons();
            }
            ImGui.End();
        }

        if (!pOpen)
        {
            if (_isLoading) _cancellationTokenSource?.Cancel();
            IsOpen = false;
        }
    }
    // --- END MODIFIED ---

    // --- ADDED: Method to draw the loading UI ---
    private void DrawLoadingState()
    {
        var windowSize = ImGui.GetWindowSize();
        var contentRegionAvail = ImGui.GetContentRegionAvail();

        // Center content vertically
        var verticalOffset = (contentRegionAvail.Y - ImGui.GetTextLineHeightWithSpacing() * 3 - ImGui.GetFrameHeightWithSpacing()) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalOffset);

        ImGui.Text("Loading Images...");
        ImGui.TextWrapped(_loadingStatus);
        ImGui.ProgressBar(_loadingProgress, new Vector2(-1, 0), $"{_loadingProgress * 100:0}%");
        ImGui.Spacing();

        // Center the cancel button horizontally
        var buttonText = "Cancel";
        var buttonWidth = ImGui.CalcTextSize(buttonText).X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosX((windowSize.X - buttonWidth) * 0.5f);

        if (ImGui.Button(buttonText))
        {
            _cancellationTokenSource?.Cancel();
            IsOpen = false; // Close the dialog on cancel
        }
    }
    // --- END ADDED ---

    private void DrawToolbar()
    {
        if (ImGui.Button("Auto Group")) AutoGroupImages();
        ImGui.SameLine();

        ImGui.Checkbox("Create Single Group", ref _createSingleGroup);
        if (_createSingleGroup)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##SingleGroupName", ref _singleGroupName, 100);
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"Source: {Path.GetFileName(_sourceFolderPath)}");
    }

    private void DrawAvailableImages()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1.0f, 1.0f), "Available Images");
        ImGui.Separator();

        var ungroupedImages = GetUngroupedImages();
        var availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y;

        if (ImGui.BeginChild("AvailableImagesChild", new Vector2(0, availableHeight), ImGuiChildFlags.Border))
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
                ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
                _selectedImages.Clear();

            if (ImGui.Selectable("Select All", false))
                foreach (var img in GetUngroupedImages())
                    _selectedImages.Add(img);

            ImGui.Separator();

            for (var i = 0; i < ungroupedImages.Count; i++)
            {
                var img = ungroupedImages[i];
                var isSelected = _selectedImages.Contains(img);

                ImGui.PushID(img.GetHashCode());
                if (ImGui.Selectable(img.FileName, isSelected)) HandleImageSelection(img, ungroupedImages);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Size: {img.Width}x{img.Height}");
                    ImGui.Text($"File Size: {img.FileSize / 1024} KB");
                    ImGui.Text($"Modified: {img.Modified:yyyy-MM-dd HH:mm}");
                    ImGui.EndTooltip();
                }

                if (ImGui.BeginDragDropSource())
                {
                    var selectedList = _selectedImages.Contains(img)
                        ? _selectedImages.ToList()
                        : new List<ImageFileInfo> { img };
                    ImGui.SetDragDropPayload("IMAGES", IntPtr.Zero, 0);
                    ImGui.Text($"Move {selectedList.Count} image(s)");
                    ImGui.EndDragDropSource();
                }

                ImGui.PopID();
            }

            ImGui.EndChild();
        }

        ImGui.TextDisabled($"{ungroupedImages.Count} ungrouped images");
    }

    private void DrawGroups()
    {
        ImGui.TextColored(new Vector4(0.8f, 1.0f, 0.8f, 1.0f), "Groups");
        ImGui.Separator();

        ImGui.SetNextItemWidth(-50);
        ImGui.InputText("##NewGroup", ref _newGroupName, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(_newGroupName))
            if (!_groups.ContainsKey(_newGroupName))
            {
                _groups[_newGroupName] = new List<ImageFileInfo>();
                _selectedGroup = _newGroupName;
                _newGroupName = "";
            }

        ImGui.Separator();

        var availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y;

        if (ImGui.BeginChild("GroupsChild", new Vector2(0, availableHeight), ImGuiChildFlags.Border))
        {
            foreach (var group in _groups.ToList())
            {
                var isSelected = _selectedGroup == group.Key;

                ImGui.PushID(group.Key);

                var label = $"{group.Key} ({group.Value.Count})";
                if (ImGui.Selectable(label, isSelected)) _selectedGroup = group.Key;

                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("IMAGES");
                        if (payload.NativePtr != null)
                        {
                            foreach (var img in _selectedImages)
                            {
                                foreach (var g in _groups.Values) g.Remove(img);

                                if (!group.Value.Contains(img)) group.Value.Add(img);
                            }

                            _selectedImages.Clear();
                        }
                    }

                    ImGui.EndDragDropTarget();
                }

                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Rename"))
                    {
                        // TODO: Implement rename
                    }

                    if (ImGui.MenuItem("Delete"))
                    {
                        _groups.Remove(group.Key);
                        if (_selectedGroup == group.Key)
                            _selectedGroup = null;
                    }

                    ImGui.EndPopup();
                }

                ImGui.PopID();
            }

            ImGui.EndChild();
        }

        ImGui.TextDisabled($"{_groups.Count} groups");
    }

    private void DrawGroupDetails()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.8f, 1.0f), "Group Contents");
        ImGui.Separator();

        if (_selectedGroup != null && _groups.ContainsKey(_selectedGroup))
        {
            var group = _groups[_selectedGroup];
            var availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y;

            if (ImGui.BeginChild("GroupContentsChild", new Vector2(0, availableHeight), ImGuiChildFlags.Border))
            {
                for (var i = 0; i < group.Count; i++)
                {
                    var img = group[i];

                    ImGui.PushID(img.GetHashCode());
                    if (ImGui.Selectable(img.FileName))
                    {
                        // Could open preview
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Size: {img.Width}x{img.Height}");
                        ImGui.Text($"File Size: {img.FileSize / 1024} KB");
                        ImGui.EndTooltip();
                    }

                    if (ImGui.BeginPopupContextItem())
                    {
                        if (ImGui.MenuItem("Remove from group")) group.Remove(img);
                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }

            ImGui.TextDisabled($"{group.Count} images in group");
        }
        else
        {
            var availableHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y;
            ImGui.BeginChild("GroupContentsChildEmpty", new Vector2(0, availableHeight), ImGuiChildFlags.Border);

            // Center the text vertically and horizontally
            var textSize = ImGui.CalcTextSize("Select a group to view contents");
            var windowSize = ImGui.GetWindowSize();
            ImGui.SetCursorPos(new Vector2(
                (windowSize.X - textSize.X) * 0.5f,
                (windowSize.Y - textSize.Y) * 0.5f
            ));
            ImGui.TextDisabled("Select a group to view contents");

            ImGui.EndChild();
        }
    }

    private void DrawBottomButtons()
    {
        if (ImGui.Button("Create Group from Selected") && _selectedImages.Count > 0)
        {
            var groupName = $"Group {_groups.Count + 1}";
            _groups[groupName] = new List<ImageFileInfo>(_selectedImages);
            _selectedImages.Clear();
            _selectedGroup = groupName;
        }

        ImGui.SameLine();

        var canImport = _groups.Any(g => g.Value.Count > 0) || _createSingleGroup;
        if (!canImport) ImGui.BeginDisabled();

        if (ImGui.Button("Import", new Vector2(120, 0)))
        {
            ImportGroups();
            IsOpen = false;
        }

        if (!canImport) ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(120, 0))) IsOpen = false;
    }

    private void HandleImageSelection(ImageFileInfo img, List<ImageFileInfo> imageList)
    {
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var shiftHeld = ImGui.GetIO().KeyShift;

        if (ctrlHeld)
        {
            if (_selectedImages.Contains(img))
                _selectedImages.Remove(img);
            else
                _selectedImages.Add(img);
        }
        else if (shiftHeld && _lastSelectedImage != null)
        {
            var startIdx = imageList.IndexOf(_lastSelectedImage);
            var endIdx = imageList.IndexOf(img);

            if (startIdx != -1 && endIdx != -1)
            {
                var minIdx = Math.Min(startIdx, endIdx);
                var maxIdx = Math.Max(startIdx, endIdx);

                for (var i = minIdx; i <= maxIdx; i++) _selectedImages.Add(imageList[i]);
            }
        }
        else
        {
            _selectedImages.Clear();
            _selectedImages.Add(img);
        }

        _lastSelectedImage = img;
    }

    private List<ImageFileInfo> GetUngroupedImages()
    {
        var grouped = new HashSet<ImageFileInfo>();
        foreach (var group in _groups.Values)
        foreach (var img in group)
            grouped.Add(img);

        return _availableImages.Where(img => !grouped.Contains(img)).ToList();
    }

    private void ImportGroups()
    {
        if (_createSingleGroup)
        {
            var allImages = new List<Dataset>();
            foreach (var img in _availableImages)
            {
                var dataset = CreateImageDataset(img);
                ProjectManager.Instance.AddDataset(dataset);
                allImages.Add(dataset);
            }

            if (allImages.Count > 0)
            {
                var group = new DatasetGroup(_singleGroupName, allImages);

                foreach (var ds in allImages) ProjectManager.Instance.RemoveDataset(ds);

                ProjectManager.Instance.AddDataset(group);
            }
        }
        else
        {
            foreach (var group in _groups)
            {
                if (group.Value.Count == 0) continue;

                var datasets = new List<Dataset>();
                foreach (var img in group.Value)
                {
                    var dataset = CreateImageDataset(img);
                    ProjectManager.Instance.AddDataset(dataset);
                    datasets.Add(dataset);
                }

                if (datasets.Count > 1)
                {
                    var datasetGroup = new DatasetGroup(group.Key, datasets);

                    foreach (var ds in datasets) ProjectManager.Instance.RemoveDataset(ds);

                    ProjectManager.Instance.AddDataset(datasetGroup);
                }
            }

            foreach (var img in GetUngroupedImages())
            {
                var dataset = CreateImageDataset(img);
                ProjectManager.Instance.AddDataset(dataset);
            }
        }
    }

    private ImageDataset CreateImageDataset(ImageFileInfo img)
    {
        return new ImageDataset(Path.GetFileNameWithoutExtension(img.FileName), img.FilePath)
        {
            Width = img.Width,
            Height = img.Height,
            BitDepth = 32, // RGBA
            PixelSize = 0,
            Unit = ""
        };
    }

    private class ImageFileInfo
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime Modified { get; set; }
    }
}