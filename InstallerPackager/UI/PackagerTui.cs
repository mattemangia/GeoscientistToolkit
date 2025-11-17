using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.InstallerPackager.Models;
using GeoscientistToolkit.InstallerPackager.Services;
using GeoscientistToolkit.InstallerPackager.Utilities;
using Terminal.Gui;

namespace GeoscientistToolkit.InstallerPackager.UI;

internal sealed class PackagerTui
{
    private readonly PackagerSettings _settings;
    private InstallerManifest _manifest = new();
    private Window? _window;
    private TextView? _logView;
    private Button? _buildButton;
    private readonly Dictionary<string, CheckBox> _platformCheckboxes = new();
    private TextField? _versionField;
    private TextField? _outputField;
    private TextField? _urlField;
    private RadioGroup? _configRadio;

    public PackagerTui(PackagerSettings settings)
    {
        _settings = settings;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            _manifest = await ManifestPersistence.LoadOrCreateAsync(_settings.ManifestPath).ConfigureAwait(false);

            Application.Init();
            try
            {
                CreateUI();
                Application.Run();
                return 0;
            }
            finally
            {
                Application.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore TUI: {ex.Message}");
            return 1;
        }
    }

    private void CreateUI()
    {
        _window = new Window("GeoscientistToolkit - Installer Packager")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Platform selection
        var platformLabel = new Label("Platforms to build:")
        {
            X = 1,
            Y = 1
        };
        _window.Add(platformLabel);

        int yOffset = 2;
        foreach (var package in _manifest.Packages)
        {
            var checkbox = new CheckBox($"{package.RuntimeIdentifier} - {package.Description}")
            {
                X = 3,
                Y = yOffset++,
                Checked = true
            };
            _platformCheckboxes[package.RuntimeIdentifier] = checkbox;
            _window.Add(checkbox);
        }

        // Configuration
        yOffset++;
        var configLabel = new Label("Configuration:")
        {
            X = 1,
            Y = yOffset++
        };
        _window.Add(configLabel);

        _configRadio = new RadioGroup(new[] { "Release", "Debug" })
        {
            X = 3,
            Y = yOffset,
            SelectedItem = _settings.PublishConfiguration == "Debug" ? 1 : 0
        };
        yOffset += 2;
        _window.Add(_configRadio);

        // Version
        yOffset++;
        var versionLabel = new Label("Version:")
        {
            X = 1,
            Y = yOffset++
        };
        _window.Add(versionLabel);

        _versionField = new TextField(_manifest.Version)
        {
            X = 3,
            Y = yOffset++,
            Width = 30
        };
        _window.Add(_versionField);

        // Output directory
        yOffset++;
        var outputLabel = new Label("Output Directory:")
        {
            X = 1,
            Y = yOffset++
        };
        _window.Add(outputLabel);

        _outputField = new TextField(_settings.PackagesOutputDirectory)
        {
            X = 3,
            Y = yOffset++,
            Width = 50
        };
        _window.Add(_outputField);

        // Base URL
        yOffset++;
        var urlLabel = new Label("Package Base URL:")
        {
            X = 1,
            Y = yOffset++
        };
        _window.Add(urlLabel);

        _urlField = new TextField(_settings.PackageBaseUrl)
        {
            X = 3,
            Y = yOffset++,
            Width = 50
        };
        _window.Add(_urlField);

        // Build button
        yOffset++;
        _buildButton = new Button("Start Build")
        {
            X = 3,
            Y = yOffset++
        };
        _buildButton.Clicked += OnBuildClicked;
        _window.Add(_buildButton);

        // Log view
        yOffset++;
        var logLabel = new Label("Build Log:")
        {
            X = 1,
            Y = yOffset++
        };
        _window.Add(logLabel);

        _logView = new TextView
        {
            X = 1,
            Y = yOffset,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            ReadOnly = true
        };
        _window.Add(_logView);

        Application.Top.Add(_window);
    }

    private async void OnBuildClicked()
    {
        if (_buildButton == null || _logView == null || _versionField == null ||
            _outputField == null || _urlField == null || _configRadio == null)
        {
            return;
        }

        _buildButton.Enabled = false;
        _logView.Text = "";
        LogMessage("Avvio build...\n");

        try
        {
            var selectedPlatforms = _platformCheckboxes
                .Where(kvp => kvp.Value.Checked)
                .Select(kvp => kvp.Key)
                .ToList();

            if (selectedPlatforms.Count == 0)
            {
                LogMessage("ERRORE: Nessuna piattaforma selezionata!\n");
                return;
            }

            var settings = _settings with
            {
                PublishConfiguration = _configRadio.SelectedItem == 0 ? "Release" : "Debug",
                PackagesOutputDirectory = _outputField.Text.ToString() ?? _settings.PackagesOutputDirectory,
                PackageBaseUrl = _urlField.Text.ToString() ?? _settings.PackageBaseUrl
            };

            var manifest = _manifest with
            {
                Version = _versionField.Text.ToString() ?? _manifest.Version
            };

            var packagesToBuild = manifest.Packages
                .Where(p => selectedPlatforms.Contains(p.RuntimeIdentifier))
                .ToList();

            LogMessage($"Building {packagesToBuild.Count} package(s)...\n");

            var publisher = new PublishService();
            var buildService = new BuildService();

            foreach (var package in packagesToBuild)
            {
                LogMessage($"\n=== Building {package.RuntimeIdentifier} ===\n");
                await buildService.BuildPackageAsync(package, settings, publisher, LogMessage).ConfigureAwait(false);
                LogMessage($"✓ {package.RuntimeIdentifier} completed\n");
            }

            await ManifestPersistence.SaveAsync(settings.ManifestPath, manifest).ConfigureAwait(false);
            LogMessage("\n=== BUILD COMPLETED SUCCESSFULLY ===\n");
            LogMessage($"Packages saved to: {settings.PackagesOutputDirectory}\n");
            LogMessage($"Manifest updated: {settings.ManifestPath}\n");
        }
        catch (Exception ex)
        {
            LogMessage($"\nERRORE: {ex.Message}\n");
            LogMessage($"{ex}\n");
        }
        finally
        {
            _buildButton.Enabled = true;
        }
    }

    private void LogMessage(string message)
    {
        if (_logView == null) return;

        Application.MainLoop.Invoke(() =>
        {
            _logView.Text += message;
            _logView.MoveEnd();
        });
    }
}
