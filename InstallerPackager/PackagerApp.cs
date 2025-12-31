using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.InstallerPackager.Models;
using GeoscientistToolkit.InstallerPackager.Services;
using GeoscientistToolkit.InstallerPackager.UI;
using GeoscientistToolkit.InstallerPackager.Utilities;

namespace GeoscientistToolkit.InstallerPackager;

internal sealed class PackagerApp
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = CommandLineParser.Parse(args);

            if (options.ShowHelp)
            {
                CommandLineParser.PrintHelp();
                return 0;
            }

            var settings = PackagerSettingsLoader.Load();

            if (options.Interactive)
            {
                var tui = new PackagerTui(settings);
                return await tui.RunAsync().ConfigureAwait(false);
            }

            settings = ApplyCommandLineOverrides(settings, options);

            var manifest = await ManifestPersistence.LoadOrCreateAsync(settings.ManifestPath).ConfigureAwait(false);

            if (options.Version != null)
            {
                manifest = manifest with { Version = options.Version };
            }

            var packagesToBuild = options.Platforms.Count > 0
                ? manifest.Packages.Where(p => options.Platforms.Contains(p.RuntimeIdentifier)).ToList()
                : manifest.Packages;

            if (packagesToBuild.Count == 0)
            {
                Console.Error.WriteLine("No packages to build. Check the specified platforms.");
                return 1;
            }

            var publisher = new PublishService();
            var buildService = new BuildService();
            var installerService = new InstallerPublishService();
            var publishedInstallers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packagesToBuild)
            {
                await buildService.BuildPackageAsync(package, settings, publisher).ConfigureAwait(false);
                if (publishedInstallers.Add(package.RuntimeIdentifier))
                {
                    await installerService.PublishInstallerAsync(package.RuntimeIdentifier, settings, publisher)
                        .ConfigureAwait(false);
                }
            }

            await ManifestPersistence.SaveAsync(settings.ManifestPath, manifest).ConfigureAwait(false);
            Console.WriteLine("Packages generated successfully.");
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.WriteLine();
            CommandLineParser.PrintHelp();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Packaging failed: {ex.Message}\n{ex}");
            return 1;
        }
    }

    private static PackagerSettings ApplyCommandLineOverrides(PackagerSettings settings, CommandLineOptions options)
    {
        if (!options.HasOverrides)
        {
            return settings;
        }

        return settings with
        {
            PublishConfiguration = options.Configuration ?? settings.PublishConfiguration,
            PackagesOutputDirectory = options.OutputDirectory ?? settings.PackagesOutputDirectory,
            PackageBaseUrl = options.PackageBaseUrl ?? settings.PackageBaseUrl
        };
    }
}
