using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Services;
using GeoscientistToolkit.Installer.Utilities;
using Terminal.Gui;

namespace GeoscientistToolkit.Installer;

internal sealed class InstallerWizardApp
{
    private readonly InstallerSettings _settings;
    private readonly ManifestLoader _manifestLoader;
    private readonly UpdateService _updateService;
    private readonly InstallPlanBuilder _planBuilder;
    private readonly ArchiveInstallService _archiveInstallService;

    private InstallerManifest? _manifest;
    private InstallMetadata? _metadata;
    private bool _hasUpdate;
    private string _selectedRuntime = RuntimeHelper.GetCurrentRuntimeIdentifier();
    private string _installPath;

    private Wizard? _wizard;
    private ListView? _runtimeList;
    private TextField? _installPathField;
    private Label? _welcomeLabel;
    private Label? _updateLabel;
    private TextView? _reviewText;
    private FrameView? _componentsFrame;
    private CheckBox? _shortcutCheckbox;
    private Label? _elevationLabel;
    private Button? _elevateButton;
    private ProgressBar? _progressBar;
    private TextView? _logView;
    private Label? _statusLabel;
    private bool _installationCompleted;
    private readonly Dictionary<string, bool> _componentSelections = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RuntimeComponent> _currentComponents = Array.Empty<RuntimeComponent>();
    private bool _createDesktopShortcut = true;
    private bool _isElevated;

    public InstallerWizardApp(
        InstallerSettings settings,
        ManifestLoader manifestLoader,
        UpdateService updateService,
        InstallPlanBuilder planBuilder,
        ArchiveInstallService archiveInstallService)
    {
        _settings = settings;
        _manifestLoader = manifestLoader;
        _updateService = updateService;
        _planBuilder = planBuilder;
        _archiveInstallService = archiveInstallService;
        _installPath = ExpandPath(settings.DefaultInstallRoot);
        _isElevated = IsRunningAsAdministrator();
    }

    public void Run()
    {
        Application.Init();
        try
        {
            LoadState();
            _wizard = BuildWizard();
            Application.Run(_wizard);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private void LoadState()
    {
        _metadata = _updateService.TryLoadExistingMetadata(_installPath);
        var updateInfo = _updateService.CheckForUpdatesAsync(_metadata).GetAwaiter().GetResult();
        _manifest = updateInfo.manifest;
        _hasUpdate = updateInfo.hasUpdate;

        if (_metadata is not null)
        {
            _selectedRuntime = _metadata.RuntimeIdentifier;
            _installPath = _metadata.InstallPath;
            _createDesktopShortcut = _metadata.CreateDesktopShortcut;
            if (_metadata.Components.Count > 0)
            {
                _componentSelections.Clear();
                foreach (var component in _metadata.Components)
                {
                    _componentSelections[component] = true;
                }
            }
        }
    }

    private Wizard BuildWizard()
    {
        var wizard = new Wizard($"Installer {_settings.ProductName}")
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        wizard.AddPage(CreateWelcomePage());
        wizard.AddPage(CreateRuntimePage());
        wizard.AddPage(CreateReviewPage());
        wizard.AddPage(CreateProgressPage());

        wizard.Finished += _ => Application.RequestStop();
        wizard.Canceled += _ => Application.RequestStop();

        return wizard;
    }

    private Wizard.WizardPage CreateWelcomePage()
    {
        var page = new Wizard.WizardPage("Benvenuto", "Preparazione dell'installazione");

        _welcomeLabel = new Label
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 2,
            Text = _manifest is null
                ? "Manifest non disponibile"
                : $"Versione disponibile: {_manifest.Version}"
        };

        var installedVersionText = _metadata is not null ? $"Versione installata: {_metadata.Version}. " : string.Empty;

        _updateLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(_welcomeLabel) + 1,
            Width = Dim.Fill(),
            Height = 2,
            Text = installedVersionText + (_hasUpdate
                ? "È disponibile un aggiornamento. Procedere per installarlo."
                : "Nessun aggiornamento necessario.")
        };

        if (_manifest?.Prerequisites.Any() == true)
        {
            var prereqText = string.Join('\n', _manifest.Prerequisites.Select(p => $"- {p.Description}"));
            var prereqLabel = new Label
            {
                X = 0,
                Y = Pos.Bottom(_updateLabel) + 1,
                Width = Dim.Fill(),
                Text = "Prerequisiti:\n" + prereqText
            };
            page.Add(prereqLabel);
        }

        page.Add(_welcomeLabel);
        page.Add(_updateLabel);

        return page;
    }

    private Wizard.WizardPage CreateRuntimePage()
    {
        var page = new Wizard.WizardPage("Opzioni", "Seleziona runtime e cartella di installazione");

        var packages = _manifest?.Packages ?? new List<RuntimePackage>();
        var items = packages.Select(p => $"{p.RuntimeIdentifier} · {(p.Description ?? "")}").ToList();

        _runtimeList = new ListView(items)
        {
            Width = Dim.Fill(),
            Height = 6,
            AllowsMarking = false
        };

        var defaultIndex = Math.Max(0, packages.FindIndex(p => string.Equals(p.RuntimeIdentifier, _selectedRuntime, StringComparison.OrdinalIgnoreCase)));
        _runtimeList.SelectedItem = defaultIndex >= 0 ? defaultIndex : 0;
        _runtimeList.SelectedItemChanged += _ => RefreshComponentOptions();

        var pathLabel = new Label("Percorso di installazione:")
        {
            X = 0,
            Y = Pos.Bottom(_runtimeList) + 1
        };

        _installPathField = new TextField(_installPath)
        {
            X = 0,
            Y = Pos.Bottom(pathLabel) + 1,
            Width = Dim.Fill()
        };

        _componentsFrame = new FrameView("Componenti")
        {
            X = 0,
            Y = Pos.Bottom(_installPathField) + 1,
            Width = Dim.Fill(),
            Height = 7
        };

        _shortcutCheckbox = new CheckBox("Crea collegamento sul desktop", _createDesktopShortcut)
        {
            X = 0,
            Y = Pos.Bottom(_componentsFrame) + 1
        };
        _shortcutCheckbox.Toggled += _ => _createDesktopShortcut = _shortcutCheckbox.Checked;

        page.Add(_runtimeList);
        page.Add(pathLabel);
        page.Add(_installPathField);
        page.Add(_componentsFrame);
        page.Add(_shortcutCheckbox);

        RefreshComponentOptions();

        return page;
    }

    private void RefreshComponentOptions()
    {
        if (_componentsFrame is null || _manifest is null)
        {
            return;
        }

        _componentsFrame.RemoveAll();
        var runtime = GetSelectedRuntime();
        var package = _manifest.Packages.FirstOrDefault(p => string.Equals(p.RuntimeIdentifier, runtime, StringComparison.OrdinalIgnoreCase));
        _currentComponents = package?.Components ?? Array.Empty<RuntimeComponent>();

        if (_currentComponents.Count == 0)
        {
            _componentsFrame.Add(new Label("Nessun componente configurato")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill()
            });
            _componentsFrame.Height = 3;
            return;
        }

        var knownIds = new HashSet<string>(_currentComponents.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var toRemove = _componentSelections.Keys.Where(k => !knownIds.Contains(k)).ToList();
        foreach (var key in toRemove)
        {
            _componentSelections.Remove(key);
        }

        var y = 0;
        foreach (var component in _currentComponents)
        {
            var isSelected = _componentSelections.TryGetValue(component.Id, out var selected)
                ? selected
                : component.DefaultSelected;

            _componentSelections[component.Id] = isSelected;

            var checkBox = new CheckBox(component.DisplayName, isSelected)
            {
                X = 0,
                Y = y++
            };
            checkBox.Toggled += _ => _componentSelections[component.Id] = checkBox.Checked;
            _componentsFrame.Add(checkBox);
        }

        _componentsFrame.Height = Math.Max(5, _currentComponents.Count + 2);
    }

    private Wizard.WizardPage CreateReviewPage()
    {
        var page = new Wizard.WizardPage("Riepilogo", "Conferma i parametri scelti");

        _reviewText = new TextView
        {
            ReadOnly = true,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 3
        };

        _elevationLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(_reviewText) + 1,
            Width = Dim.Fill(),
            Text = string.Empty
        };

        page.Add(_reviewText);
        page.Add(_elevationLabel);

        if (OperatingSystem.IsWindows())
        {
            _elevateButton = new Button("Richiedi privilegi amministratore")
            {
                X = 0,
                Y = Pos.Bottom(_elevationLabel) + 1
            };
            _elevateButton.Clicked += RequestElevation;
            page.Add(_elevateButton);
        }

        page.Enter += _ =>
        {
            UpdateReview();
            UpdateElevationLabel();
        };

        return page;
    }

    private Wizard.WizardPage CreateProgressPage()
    {
        var page = new Wizard.WizardPage("Installazione", "Esecuzione attività");

        _progressBar = new ProgressBar
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill()
        };

        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(_progressBar) + 1,
            Width = Dim.Fill(),
            Text = "In attesa"
        };

        _logView = new TextView
        {
            X = 0,
            Y = Pos.Bottom(_statusLabel) + 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true
        };

        page.Add(_progressBar);
        page.Add(_statusLabel);
        page.Add(_logView);

        page.Enter += _ => StartInstallation();

        return page;
    }

    private void UpdateReview()
    {
        if (_reviewText is null || _manifest is null)
        {
            return;
        }

        var runtime = GetSelectedRuntime();
        var path = ExpandPath(_installPathField?.Text?.ToString() ?? _installPath);
        var componentIds = GetSelectedComponentIds();
        var selectedComponents = new HashSet<string>(componentIds, StringComparer.OrdinalIgnoreCase);
        var componentNames = _currentComponents.Count == 0
            ? "Nessun componente"
            : string.Join(", ", _currentComponents
                .Where(c => selectedComponents.Contains(c.Id))
                .Select(c => c.DisplayName));

        var desktopShortcut = (_shortcutCheckbox?.Checked ?? _createDesktopShortcut) ? "Sì" : "No";
        var adminStatus = OperatingSystem.IsWindows()
            ? (_isElevated ? "attivi" : "non attivi")
            : "non richiesti";

        _reviewText.Text = $"Prodotto: {_settings.ProductName}\nVersione manifest: {_manifest.Version}\nRuntime: {runtime}\nPercorso: {path}\nComponenti: {componentNames}\nCollegamento desktop: {desktopShortcut}\nPrivilegi amministratore: {adminStatus}";
    }

    private void UpdateElevationLabel()
    {
        if (_elevationLabel is null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _elevationLabel.Text = "Privilegi amministratore non necessari su questo sistema.";
            _elevateButton?.Hide();
            return;
        }

        _elevationLabel.Text = _isElevated
            ? "Privilegi amministratore già concessi."
            : "Per installare in cartelle protette è consigliata l'elevazione con UAC.";

        if (_elevateButton is not null)
        {
            _elevateButton.Visible = !_isElevated;
        }
    }

    private string GetSelectedRuntime()
    {
        if (_runtimeList is null || _manifest is null || _manifest.Packages.Count == 0)
        {
            return _selectedRuntime;
        }

        var index = Math.Clamp(_runtimeList.SelectedItem, 0, _manifest.Packages.Count - 1);
        return _manifest.Packages[index].RuntimeIdentifier;
    }

    private IReadOnlyCollection<string> GetSelectedComponentIds()
    {
        if (_currentComponents.Count == 0)
        {
            return Array.Empty<string>();
        }

        var selected = _currentComponents
            .Where(component => _componentSelections.TryGetValue(component.Id, out var value) ? value : component.DefaultSelected)
            .Select(component => component.Id)
            .ToList();

        return selected;
    }

    private void StartInstallation()
    {
        if (_installationCompleted)
        {
            return;
        }

        _installationCompleted = true;
        Task.Run(async () =>
        {
            try
            {
                await ExecuteInstallationAsync().ConfigureAwait(false);
                Application.MainLoop.Invoke(() =>
                {
                    if (_statusLabel is not null)
                    {
                        _statusLabel.Text = "Installazione completata";
                    }
                    if (_wizard is not null)
                    {
                        _wizard.NextButton.Visible = true;
                        _wizard.NextButton.Text = "Fine";
                        _wizard.NextButton.Enabled = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Application.MainLoop.Invoke(() =>
                {
                    _statusLabel!.Text = $"Errore: {ex.Message}";
                    _logView?.InsertText($"\n{ex}\n");
                    if (_wizard is not null)
                    {
                        _wizard.NextButton.Visible = true;
                        _wizard.NextButton.Text = "Chiudi";
                        _wizard.NextButton.Enabled = true;
                    }
                });
            }
        });
    }

    private void RequestElevation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentProcessPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = currentProcessPath,
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                Verb = "runas"
            });
            Application.RequestStop();
        }
        catch (Win32Exception ex)
        {
            Log($"Elevazione annullata o non riuscita: {ex.Message}");
        }
    }

    private async Task ExecuteInstallationAsync()
    {
        if (_manifest is null)
        {
            throw new InvalidOperationException("Manifest non caricato");
        }

        var runtime = GetSelectedRuntime();
        var installPath = ExpandPath(_installPathField?.Text?.ToString() ?? _installPath);
        var componentIds = GetSelectedComponentIds();
        var plan = _planBuilder.CreatePlan(
            _manifest,
            runtime,
            installPath,
            componentIds,
            _shortcutCheckbox?.Checked ?? _createDesktopShortcut);
        UpdateProgress(0.05f, "Preparazione cartelle");
        if (Directory.Exists(plan.InstallPath))
        {
            Directory.Delete(plan.InstallPath, true);
        }
        Directory.CreateDirectory(plan.InstallPath);

        string payloadPath;
        if (plan.Package.Transport == PackageTransport.Archive)
        {
            UpdateProgress(0.15f, "Download pacchetto");
            payloadPath = await _archiveInstallService.DownloadAndExtractAsync(plan.Package, Log).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException("Il pacchetto selezionato non fornisce un archivio compresso. Eseguire il packager prima della distribuzione.");
        }

        UpdateProgress(0.6f, "Copia componenti");
        foreach (var component in plan.Components)
        {
            var source = string.IsNullOrWhiteSpace(component.RelativePath) || component.RelativePath == "."
                ? payloadPath
                : Path.Combine(payloadPath, component.RelativePath);
            var target = Path.Combine(plan.InstallPath, component.TargetSubdirectory ?? string.Empty);
            CopyDirectory(source, target);
        }

        UpdateProgress(0.8f, "Creazione launcher");
        CreateLaunchers(plan);

        if (plan.CreateDesktopShortcut)
        {
            UpdateProgress(0.85f, "Collegamento desktop");
            CreateDesktopShortcut(plan);
        }
        UpdateProgress(0.9f, "Scrittura metadati");
        await _updateService.SaveMetadataAsync(plan).ConfigureAwait(false);
        UpdateProgress(1f, "Installazione completata");
    }

    private void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Percorso sorgente non trovato: {source}");
        }

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            var targetDir = Path.Combine(destination, relative);
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, true);
        }
    }

    private void CreateLaunchers(InstallPlan plan)
    {
        var launchComponent = plan.Components.FirstOrDefault(c => !string.IsNullOrEmpty(c.EntryExecutable));
        if (launchComponent is null || string.IsNullOrEmpty(launchComponent.EntryExecutable))
        {
            return;
        }

        var executableRelative = Path.Combine(launchComponent.TargetSubdirectory ?? string.Empty, launchComponent.EntryExecutable);
        var executableFull = Path.Combine(plan.InstallPath, executableRelative);
        if (!File.Exists(executableFull))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(plan.InstallPath, "GeoscientistToolkit.cmd");
            File.WriteAllText(script, $"@echo off\r\n\"%~dp0{executableRelative}\" %*\r\n");
        }
        else
        {
            var script = Path.Combine(plan.InstallPath, "geoscientist-toolkit.sh");
            var relativePath = executableRelative.Replace('\\', '/');
            File.WriteAllText(script, $"#!/bin/sh\nDIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"\n\"$DIR/{relativePath}\" \"$@\"\n");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void CreateDesktopShortcut(InstallPlan plan)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            return;
        }

        var shortcutComponent = plan.Components.FirstOrDefault(c => c.SupportsDesktopShortcut && !string.IsNullOrEmpty(c.EntryExecutable));
        if (shortcutComponent is null)
        {
            return;
        }

        var executableRelative = Path.Combine(shortcutComponent.TargetSubdirectory ?? string.Empty, shortcutComponent.EntryExecutable);
        var executableFull = Path.Combine(plan.InstallPath, executableRelative);
        if (!File.Exists(executableFull))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            CreateWindowsShortcut(desktop, executableFull);
        }
        else if (OperatingSystem.IsLinux())
        {
            CreateDesktopFile(desktop, executableFull);
        }
        else if (OperatingSystem.IsMacOS())
        {
            CreateMacShortcut(desktop, executableFull);
        }
    }

    private void CreateWindowsShortcut(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, $"{_settings.ProductName}.lnk");
        var workingDirectory = Path.GetDirectoryName(target) ?? desktop;
        var psScript = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut(\"{shortcut}\"); $s.TargetPath = \"{target}\"; $s.WorkingDirectory = \"{workingDirectory}\"; $s.Save();";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoLogo -NoProfile -WindowStyle Hidden -Command \"{psScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch (Exception ex)
        {
            Log($"Impossibile creare il collegamento Windows: {ex.Message}");
        }
    }

    private void CreateDesktopFile(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, "geoscientist-toolkit.desktop");
        var content = $"[Desktop Entry]\nType=Application\nName={_settings.ProductName}\nExec=\"{target}\"\nTerminal=false\n";
        File.WriteAllText(shortcut, content);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{shortcut}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch
        {
            // ignore
        }
    }

    private void CreateMacShortcut(string desktop, string target)
    {
        var shortcut = Path.Combine(desktop, "GeoscientistToolkit.command");
        File.WriteAllText(shortcut, $"#!/bin/bash\n\"{target}\" \"$@\"\n");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{shortcut}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch
        {
            // ignore
        }
    }

    private void UpdateProgress(float percentage, string status)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_progressBar is not null)
            {
                _progressBar.Fraction = percentage;
            }
            if (_statusLabel is not null)
            {
                _statusLabel.Text = status;
            }
        });
    }

    private void Log(string message)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_logView is not null)
            {
                _logView.InsertText(message + Environment.NewLine);
            }
        });
    }

    private static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, expanded[2..]);
        }

        return Path.GetFullPath(expanded);
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
