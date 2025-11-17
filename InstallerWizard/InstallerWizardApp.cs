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
    private bool _isStopping;

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

    private void SafeRequestStop()
    {
        if (_isStopping)
        {
            return;
        }
        _isStopping = true;
        Application.RequestStop();
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
            Height = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black),
                Focus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black),
                HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black),
                HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            }
        };

        wizard.AddStep(CreateWelcomePage());
        wizard.AddStep(CreateRuntimePage());
        wizard.AddStep(CreateReviewPage());
        wizard.AddStep(CreateProgressPage());

        wizard.Finished += _ => SafeRequestStop();
        wizard.Cancelled += _ => SafeRequestStop();
        
        // Add step changed handler to ensure proper refresh
        wizard.StepChanged += (args) =>
        {
            Application.MainLoop.Invoke(() =>
            {
                wizard.SetNeedsDisplay();
                Application.Refresh();
                if (Application.Driver != null)
                {
                    Application.Driver.Refresh();
                }
            });
        };

        return wizard;
    }

    private Wizard.WizardStep CreateWelcomePage()
    {
        var page = new Wizard.WizardStep("Benvenuto")
        {
            HelpText = "Preparazione dell'installazione"
        };

        var titleLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = $"╔════════════════════════════════════════════════════════════╗",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        var titleLabel2 = new Label
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            Text = $"║   {_settings.ProductName,-56} ║",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        var titleLabel3 = new Label
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 1,
            Text = $"╚════════════════════════════════════════════════════════════╝",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        _welcomeLabel = new Label
        {
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 2,
            Text = _manifest is null
                ? "[!] Manifest non disponibile"
                : $"[*] Versione disponibile: {_manifest.Version}",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            }
        };

        var installedVersionText = _metadata is not null ? $"[#] Versione installata: {_metadata.Version}\n" : string.Empty;
        var updateIcon = _hasUpdate ? "[+]" : "[√]";
        var updateText = _hasUpdate
            ? "È disponibile un aggiornamento. Procedere per installarlo."
            : "Nessun aggiornamento necessario.";

        _updateLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(_welcomeLabel) + 1,
            Width = Dim.Fill(),
            Height = 3,
            Text = installedVersionText + $"{updateIcon} {updateText}",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(_hasUpdate ? Color.BrightGreen : Color.White, Color.Black)
            }
        };

        page.Add(titleLabel);
        page.Add(titleLabel2);
        page.Add(titleLabel3);
        page.Add(_welcomeLabel);
        page.Add(_updateLabel);

        if (_manifest?.Prerequisites.Any() == true)
        {
            var prereqFrame = new FrameView("Prerequisiti")
            {
                X = 0,
                Y = Pos.Bottom(_updateLabel) + 1,
                Width = Dim.Fill(),
                Height = _manifest.Prerequisites.Count + 2,
                ColorScheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
                }
            };

            var y = 0;
            foreach (var prereq in _manifest.Prerequisites)
            {
                var prereqLabel = new Label
                {
                    X = 0,
                    Y = y++,
                    Width = Dim.Fill(),
                    Text = $"  - {prereq.Description}"
                };
                prereqFrame.Add(prereqLabel);
            }

            page.Add(prereqFrame);
        }

        page.Enter += _ => RefreshWizardPage(page);

        return page;
    }

    private Wizard.WizardStep CreateRuntimePage()
    {
        var page = new Wizard.WizardStep("Opzioni")
        {
            HelpText = "Seleziona runtime e cartella di installazione"
        };

        var runtimeLabel = new Label("[>] Seleziona Runtime:")
        {
            X = 0,
            Y = 0,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        var packages = _manifest?.Packages ?? new List<RuntimePackage>();
        var items = packages.Select(p => $"  {p.RuntimeIdentifier} · {(p.Description ?? "")}").ToList();

        _runtimeList = new ListView(items)
        {
            X = 0,
            Y = Pos.Bottom(runtimeLabel) + 1,
            Width = Dim.Fill(),
            Height = 6,
            AllowsMarking = false,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black),
                Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightCyan)
            }
        };

        var defaultIndex = Math.Max(0, packages.FindIndex(p => string.Equals(p.RuntimeIdentifier, _selectedRuntime, StringComparison.OrdinalIgnoreCase)));
        _runtimeList.SelectedItem = defaultIndex >= 0 ? defaultIndex : 0;
        _runtimeList.SelectedItemChanged += _ => RefreshComponentOptions();

        var pathLabel = new Label("[/] Percorso di installazione:")
        {
            X = 0,
            Y = Pos.Bottom(_runtimeList) + 1,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        _installPathField = new TextField(_installPath)
        {
            X = 0,
            Y = Pos.Bottom(pathLabel) + 1,
            Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black),
                Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightYellow)
            }
        };

        _componentsFrame = new FrameView("Componenti")
        {
            X = 0,
            Y = Pos.Bottom(_installPathField) + 1,
            Width = Dim.Fill(),
            Height = 7,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            }
        };

        _shortcutCheckbox = new CheckBox("Crea collegamento sul desktop", _createDesktopShortcut)
        {
            X = 0,
            Y = Pos.Bottom(_componentsFrame) + 1
        };
        _shortcutCheckbox.Toggled += _ =>
        {
            _createDesktopShortcut = _shortcutCheckbox.Checked;
            _shortcutCheckbox.SetNeedsDisplay();
            Application.Refresh();
        };

        page.Add(runtimeLabel);
        page.Add(_runtimeList);
        page.Add(pathLabel);
        page.Add(_installPathField);
        page.Add(_componentsFrame);
        page.Add(_shortcutCheckbox);

        page.Enter += _ =>
        {
            RefreshComponentOptions();
            RefreshWizardPage(page);
        };

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
        _currentComponents = (IReadOnlyList<RuntimeComponent>?)package?.Components ?? Array.Empty<RuntimeComponent>();

        if (_currentComponents.Count == 0)
        {
            _componentsFrame.Add(new Label("Nessun componente configurato")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill()
            });
            _componentsFrame.Height = 3;
            _componentsFrame.SetNeedsDisplay();
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
            checkBox.Toggled += _ =>
            {
                _componentSelections[component.Id] = checkBox.Checked;
                checkBox.SetNeedsDisplay();
                Application.Refresh();
            };
            _componentsFrame.Add(checkBox);
        }

        _componentsFrame.Height = Math.Max(5, _currentComponents.Count + 2);
        _componentsFrame.SetNeedsDisplay();
        Application.Refresh();
    }

    private Wizard.WizardStep CreateReviewPage()
    {
        var page = new Wizard.WizardStep("Riepilogo")
        {
            HelpText = "Conferma i parametri scelti"
        };

        var reviewFrame = new FrameView("Configurazione")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 5,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            }
        };

        _reviewText = new TextView
        {
            X = 0,
            Y = 0,
            ReadOnly = true,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black)
            }
        };

        reviewFrame.Add(_reviewText);

        _elevationLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(reviewFrame) + 1,
            Width = Dim.Fill(),
            Text = string.Empty,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
            }
        };

        page.Add(reviewFrame);
        page.Add(_elevationLabel);

        if (OperatingSystem.IsWindows())
        {
            _elevateButton = new Button("[!] Richiedi privilegi amministratore")
            {
                X = 0,
                Y = Pos.Bottom(_elevationLabel) + 1,
                ColorScheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightYellow),
                    Focus = Terminal.Gui.Attribute.Make(Color.Black, Color.BrightRed)
                }
            };
            _elevateButton.Clicked += RequestElevation;
            page.Add(_elevateButton);
        }

        page.Enter += _ =>
        {
            UpdateReview();
            UpdateElevationLabel();
            RefreshWizardPage(page);
        };

        return page;
    }

    private Wizard.WizardStep CreateProgressPage()
    {
        var page = new Wizard.WizardStep("Installazione")
        {
            HelpText = "Esecuzione attività"
        };

        var progressFrame = new FrameView("Avanzamento")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 5,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            }
        };

        _progressBar = new ProgressBar
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            }
        };

        _statusLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(_progressBar) + 1,
            Width = Dim.Fill(),
            Text = "[...] In attesa...",
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            }
        };

        progressFrame.Add(_progressBar);
        progressFrame.Add(_statusLabel);

        var logFrame = new FrameView("Registro attività")
        {
            X = 0,
            Y = Pos.Bottom(progressFrame) + 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
            }
        };

        _logView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black)
            }
        };

        logFrame.Add(_logView);

        page.Add(progressFrame);
        page.Add(logFrame);

        page.Enter += _ =>
        {
            RefreshWizardPage(page);
            StartInstallation();
        };

        return page;
    }

    private void RefreshWizardPage(Wizard.WizardStep page)
    {
        // Force layout recalculation
        page.LayoutSubviews();
        page.SetNeedsDisplay();
        
        if (_wizard is not null)
        {
            _wizard.LayoutSubviews();
            _wizard.SetNeedsDisplay();
            
            // Use MainLoop.Invoke to ensure UI updates happen on the main thread
            Application.MainLoop.Invoke(() =>
            {
                // Force the wizard to update its focus
                _wizard.SetFocus();
                
                // Find and focus the first focusable view
                var firstFocusable = FindFirstFocusableView(page);
                if (firstFocusable != null)
                {
                    firstFocusable.SetFocus();
                }
                
                // Force complete redraw
                page.SetNeedsDisplay();
                _wizard.SetNeedsDisplay();
                Application.Refresh();
                
                // Force driver refresh if available
                if (Application.Driver != null)
                {
                    try
                    {
                        Application.Driver.Refresh();
                    }
                    catch
                    {
                        // Ignore any driver refresh errors
                    }
                }
            });
        }
        else
        {
            // Fallback if wizard is null
            Application.Refresh();
        }
    }

    private View? FindFirstFocusableView(View parent)
    {
        if (parent.CanFocus)
        {
            return parent;
        }
        
        foreach (var view in parent.Subviews)
        {
            var focusable = FindFirstFocusableView(view);
            if (focusable != null)
            {
                return focusable;
            }
        }
        
        return null;
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

        var reviewText = $@"
╔═══════════════════════════════════════════════════════════════╗
║  RIEPILOGO INSTALLAZIONE                                     ║
╚═══════════════════════════════════════════════════════════════╝

  [*] Prodotto:                 {_settings.ProductName}
  [#] Versione manifest:        {_manifest.Version}
  [>] Runtime:                  {runtime}
  [/] Percorso:                 {path}
  [+] Componenti:               {componentNames}
  [~] Collegamento desktop:     {desktopShortcut}
  [!] Privilegi amministratore: {adminStatus}

";

        _reviewText.Text = reviewText;
        _reviewText.SetNeedsDisplay();
        Application.Refresh();
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
            if (_elevateButton is not null)
            {
                _elevateButton.Visible = false;
                _elevateButton.SetNeedsDisplay();
            }
            _elevationLabel.SetNeedsDisplay();
            Application.Refresh();
            return;
        }

        _elevationLabel.Text = _isElevated
            ? "Privilegi amministratore già concessi."
            : "Per installare in cartelle protette è consigliata l'elevazione con UAC.";

        if (_elevateButton is not null)
        {
            _elevateButton.Visible = !_isElevated;
            _elevateButton.SetNeedsDisplay();
        }

        _elevationLabel.SetNeedsDisplay();
        Application.Refresh();
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
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteInstallationAsync().ConfigureAwait(false);
                Application.MainLoop.Invoke(() =>
                {
                    if (_statusLabel is not null)
                    {
                        _statusLabel.Text = "[√] Installazione completata con successo!";
                        _statusLabel.ColorScheme = new ColorScheme
                        {
                            Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
                        };
                        _statusLabel.SetNeedsDisplay();
                    }
                    if (_wizard is not null)
                    {
                        _wizard.NextFinishButton.Visible = true;
                        _wizard.NextFinishButton.Text = "Fine";
                        _wizard.NextFinishButton.Enabled = true;
                        _wizard.SetNeedsDisplay();
                    }
                    Application.Refresh();
                });
            }
            catch (Exception ex)
            {
                Application.MainLoop.Invoke(() =>
                {
                    if (_statusLabel is not null)
                    {
                        _statusLabel.Text = $"[X] Errore: {ex.Message}";
                        _statusLabel.ColorScheme = new ColorScheme
                        {
                            Normal = Terminal.Gui.Attribute.Make(Color.BrightRed, Color.Black)
                        };
                        _statusLabel.SetNeedsDisplay();
                    }
                    _logView?.InsertText($"\n[X] ERRORE DETTAGLIATO:\n{ex}\n");
                    _logView?.SetNeedsDisplay();
                    if (_wizard is not null)
                    {
                        _wizard.NextFinishButton.Visible = true;
                        _wizard.NextFinishButton.Text = "Chiudi";
                        _wizard.NextFinishButton.Enabled = true;
                        _wizard.SetNeedsDisplay();
                    }
                    Application.Refresh();
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
            SafeRequestStop();
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
                _progressBar.SetNeedsDisplay();
            }
            if (_statusLabel is not null)
            {
                var icon = percentage >= 1.0f ? "[√]" : percentage > 0.8f ? "[*]" : percentage > 0.5f ? "[+]" : percentage > 0.1f ? "[→]" : "[.]";
                _statusLabel.Text = $"{icon} {status}";
                _statusLabel.SetNeedsDisplay();
            }
            Application.Refresh();
        });
    }

    private void Log(string message)
    {
        Application.MainLoop.Invoke(() =>
        {
            if (_logView is not null)
            {
                _logView.InsertText(message + Environment.NewLine);
                _logView.SetNeedsDisplay();
            }
            Application.Refresh();
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