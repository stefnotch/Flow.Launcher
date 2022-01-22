using Flow.Launcher.Core.ExternalPlugins;
using Flow.Launcher.Core.Plugin;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Infrastructure.Http;
using Flow.Launcher.Infrastructure.Logger;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin.SharedCommands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Flow.Launcher.Plugin.PluginsManager
{
    internal class PluginsManager
    {
        const string zip = "zip";

        private PluginInitContext Context { get; set; }

        private Settings Settings { get; set; }

        private bool shouldHideWindow = true;

        private bool ShouldHideWindow
        {
            set => shouldHideWindow = value;
            get
            {
                var setValue = shouldHideWindow;
                // Default value for hide main window is true. Revert after get call.
                // This ensures when set by another method to false, it is only used once.
                shouldHideWindow = true;

                return setValue;
            }
        }

        private const string IcoPath = "Images\\pluginsmanager.png";

        internal PluginsManager(PluginInitContext context, Settings settings)
        {
            Context = context;
            Settings = settings;
        }
        
        internal List<Result> GetDefaultHotKeys()
        {
            return new List<Result>()
            {
                new()
                {
                    Title = Settings.InstallCommand,
                    IcoPath = IcoPath,
                    AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.InstallCommand} ",
                    Action = _ =>
                    {
                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.InstallCommand} ");
                        return false;
                    }
                },
                new()
                {
                    Title = Settings.UninstallCommand,
                    IcoPath = IcoPath,
                    AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.UninstallCommand} ",
                    Action = _ =>
                    {
                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.UninstallCommand} ");
                        return false;
                    }
                },
                new()
                {
                    Title = Settings.UpdateCommand,
                    IcoPath = IcoPath,
                    AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.UpdateCommand} ",
                    Action = _ =>
                    {
                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {Settings.UpdateCommand} ");
                        return false;
                    }
                }
            };
        }

        internal async Task InstallOrUpdateAsync(UserPlugin plugin)
        {
            if (PluginExists(plugin.ID))
            {
                if (Context.API.GetAllPlugins()
                    .Any(x => x.Metadata.ID == plugin.ID && string.Compare(x.Metadata.Version, plugin.Version, StringComparison.Ordinal) < 0))
                {
                    if (MessageBox.Show(Context.API.GetTranslation("plugin_pluginsmanager_update_exists"),
                            Context.API.GetTranslation("plugin_pluginsmanager_update_title"),
                            MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        Context
                            .API
                            .ChangeQuery(
                                $"{Context.CurrentPluginMetadata.ActionKeywords.FirstOrDefault()} {Settings.UpdateCommand} {plugin.Name}");

                    Context.API.ShowMainWindow();
                    shouldHideWindow = false;

                    return;
                }

                Context.API.ShowMsg(Context.API.GetTranslation("plugin_pluginsmanager_update_alreadyexists"));
                return;
            }

            var message = string.Format(Context.API.GetTranslation("plugin_pluginsmanager_install_prompt"),
                plugin.Name, plugin.Author,
                Environment.NewLine, Environment.NewLine);

            if (MessageBox.Show(message, Context.API.GetTranslation("plugin_pluginsmanager_install_title"),
                    MessageBoxButton.YesNo) == MessageBoxResult.No)
                return;

            // at minimum should provide a name, but handle plugin that is not downloaded from plugins manifest and is a url download
            var downloadFilename = string.IsNullOrEmpty(plugin.Version)
                ? $"{plugin.Name}-{Guid.NewGuid()}.zip"
                : $"{plugin.Name}-{plugin.Version}.zip";

            var filePath = Path.Combine(DataLocation.PluginsDirectory, downloadFilename);

            try
            {
                await Http.DownloadAsync(plugin.UrlDownload, filePath).ConfigureAwait(false);

                Context.API.ShowMsg(Context.API.GetTranslation("plugin_pluginsmanager_downloading_plugin"),
                    Context.API.GetTranslation("plugin_pluginsmanager_download_success"));

                Install(plugin, filePath);
            }
            catch (Exception e)
            {
                if (e is HttpRequestException)
                    MessageBox.Show(Context.API.GetTranslation("plugin_pluginsmanager_download_error"),
                        Context.API.GetTranslation("plugin_pluginsmanager_downloading_plugin"));

                Context.API.ShowMsgError(Context.API.GetTranslation("plugin_pluginsmanager_install_error_title"),
                    string.Format(Context.API.GetTranslation("plugin_pluginsmanager_install_error_subtitle"),
                        plugin.Name));

                Log.Exception("PluginsManager", "An error occured while downloading plugin", e, "InstallOrUpdate");

                return;
            }

            Context.API.ShowMsg(Context.API.GetTranslation("plugin_pluginsmanager_install_title"),
                Context.API.GetTranslation("plugin_pluginsmanager_install_success_restart"));

            Context.API.RestartApp();
        }

        internal async ValueTask<List<Result>> RequestListAsync(string searchName, CancellationToken token)
        {
            await PluginsManifest.UpdateManifestAsync();
            var uninstalledPlugin = GetInstallablePlugins();
            var updatablePlugin = GetUpdatablePlugins();

            var results = uninstalledPlugin.Concat(updatablePlugin).ToList();
            return Search(results, searchName);
        }
        
        private IEnumerable<Result> GetInstallablePlugins()
        {
            var installedPluginMeta = Context.API.GetAllPlugins().Select(x => (x.Metadata.Version, x.Metadata)).ToList();

            return PluginsManifest.UserPlugins
                .Where(plugin => installedPluginMeta.All(pluginMeta => plugin.ID != pluginMeta.Metadata.ID))
                .Select(ConstructNewPluginResult);

        }
        
        private IEnumerable<Result> GetUpdatablePlugins()
        {
            return from plugin in Context.API.GetAllPlugins().Select(x => (x.Metadata.Version, x.Metadata))
                   join meta in PluginsManifest.UserPlugins on plugin.Metadata.ID equals meta.ID
                   where string.Compare(meta.Version, plugin.Version, StringComparison.Ordinal) > 0
                   select ConstructUpdatablePluginResult(meta, plugin.Metadata);
        }

        private Result ConstructUpdatablePluginResult(UserPlugin newMeta, PluginMetadata currentMeta) => new()
        {
            Title = $"{currentMeta.Name} by {currentMeta.Author}",
            SubTitle = $"Update from version {currentMeta.Version} to {newMeta.Version}",
            IcoPath = currentMeta.IcoPath,
            Action = e =>
            {
                var message = string.Format(
                    Context.API.GetTranslation("plugin_pluginsmanager_update_prompt"),
                    currentMeta.Name, currentMeta.Author,
                    Environment.NewLine, Environment.NewLine);

                if (MessageBox.Show(message,
                    Context.API.GetTranslation("plugin_pluginsmanager_update_title"),
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Uninstall(currentMeta, false);

                    var downloadToFilePath = Path.Combine(DataLocation.PluginsDirectory,
                        $"{newMeta.Name}-{newMeta.Version}.zip");

                    Task.Run(async delegate
                    {
                        Context.API.ShowMsg(
                            Context.API.GetTranslation("plugin_pluginsmanager_downloading_plugin"),
                            Context.API.GetTranslation("plugin_pluginsmanager_please_wait"));

                        await Http.DownloadAsync(newMeta.UrlDownload, downloadToFilePath)
                            .ConfigureAwait(false);

                        Context.API.ShowMsg(
                            Context.API.GetTranslation("plugin_pluginsmanager_downloading_plugin"),
                            Context.API.GetTranslation("plugin_pluginsmanager_download_success"));

                        Install(newMeta, downloadToFilePath);

                        Context.API.RestartApp();
                    }).ContinueWith(t =>
                    {
                        Log.Exception("PluginsManager", $"Update failed for {currentMeta.Name}",
                            t.Exception!.InnerException, "RequestUpdate");
                        Context.API.ShowMsg(
                            Context.API.GetTranslation("plugin_pluginsmanager_install_error_title"),
                            string.Format(
                                Context.API.GetTranslation("plugin_pluginsmanager_install_error_subtitle"),
                                currentMeta.Name));
                    }, TaskContinuationOptions.OnlyOnFaulted);

                    return true;
                }

                return false;
            },
            Score = 100,
            ContextData = newMeta
        };
        
        private Result ConstructNewPluginResult(UserPlugin plugin) => new()
        {
            Title = $"{plugin.Name} by {plugin.Author}",
            SubTitle = plugin.Description,
            IcoPath = IcoPath,
            Score = 50,
            Action = e =>
            {
                if (e.SpecialKeyState.CtrlPressed)
                {
                    Context.API.OpenUrl(plugin.Website);
                    return ShouldHideWindow;
                }

                Application.Current.MainWindow?.Hide();
                _ = InstallOrUpdateAsync(plugin); // No need to wait
                return ShouldHideWindow;
            },
            ContextData = plugin
        };
        
        internal async ValueTask<List<Result>> RequestUpdateAsync(string search, CancellationToken token)
        {
            await UpdateManifestAsync(token);

            var results = GetUpdatablePlugins().ToList();

            if (!results.Any())
                return new List<Result>
                {
                    new Result
                    {
                        Title = Context.API.GetTranslation("plugin_pluginsmanager_update_noresult_title"),
                        SubTitle = Context.API.GetTranslation("plugin_pluginsmanager_update_noresult_subtitle"),
                        IcoPath = IcoPath
                    }
                };
            
            return Search(results, search);
        }

        private bool PluginExists(string id)
        {
            return Context.API.GetAllPlugins().Any(x => x.Metadata.ID == id);
        }
        
        internal async ValueTask<List<Result>> RequestInstallAsync(string search)
        {
            await PluginsManifest.UpdateManifestAsync();
            if (Uri.IsWellFormedUriString(search, UriKind.Absolute)
                && search.EndsWith($".{zip}"))
                return InstallFromWeb(search);
            
            return Search(GetInstallablePlugins(), search);
        }

        private static List<Result> Search(IEnumerable<Result> results, string searchName)
        {
            if (string.IsNullOrEmpty(searchName))
                return results.ToList();

            return results
                .Where(x =>
                {
                    var matchResult = StringMatcher.FuzzySearch(searchName, x.Title);
                    if (matchResult.IsSearchPrecisionScoreMet())
                        x.Score = matchResult.Score;

                    return matchResult.IsSearchPrecisionScoreMet();
                })
                .ToList();
        }

        private List<Result> InstallFromWeb(string url)
        {
            var filename = url.Split("/").Last();
            var name = filename.Split($".{zip}").First();

            var plugin = new UserPlugin
            {
                ID = "",
                Name = name,
                Version = string.Empty,
                Author = Context.API.GetTranslation("plugin_pluginsmanager_unknown_author"),
                UrlDownload = url
            };

            var result = new Result
            {
                Title = string.Format(Context.API.GetTranslation("plugin_pluginsmanager_install_from_web"), filename),
                SubTitle = plugin.UrlDownload,
                IcoPath = IcoPath,
                Action = e =>
                {
                    if (e.SpecialKeyState.CtrlPressed)
                    {
                        Context.API.OpenUrl(plugin.UrlDownload);
                        return ShouldHideWindow;
                    }

                    if (Settings.WarnFromUnknownSource)
                    {
                        if (!InstallSourceKnown(plugin.UrlDownload)
                            && MessageBox.Show(string.Format(Context.API.GetTranslation("plugin_pluginsmanager_install_unknown_source_warning"),
                                    Environment.NewLine),
                                Context.API.GetTranslation("plugin_pluginsmanager_install_unknown_source_warning_title"),
                                MessageBoxButton.YesNo) == MessageBoxResult.No)
                            return false;
                    }

                    _ = InstallOrUpdateAsync(plugin);

                    return ShouldHideWindow;
                }
            };

            return new List<Result>
            {
                result
            };
        }

        private bool InstallSourceKnown(string url)
        {
            var author = url.Split('/')[3];
            const string acceptedSource = "https://github.com";
            var constructedUrlPart = $"{acceptedSource}/{author}/";

            return url.StartsWith(acceptedSource) && 
                   Context.API.GetAllPlugins().Any(x => x.Metadata.Website.StartsWith(constructedUrlPart));
        }

        private void Install(UserPlugin plugin, string downloadedFilePath)
        {
            if (!File.Exists(downloadedFilePath))
                return;

            var tempFolderPath = Path.Combine(Path.GetTempPath(), "flowlauncher");
            var tempFolderPluginPath = Path.Combine(tempFolderPath, "plugin");

            if (Directory.Exists(tempFolderPath))
                Directory.Delete(tempFolderPath, true);

            Directory.CreateDirectory(tempFolderPath);

            var zipFilePath = Path.Combine(tempFolderPath, Path.GetFileName(downloadedFilePath));

            File.Copy(downloadedFilePath, zipFilePath);

            File.Delete(downloadedFilePath);

            Utilities.UnZip(zipFilePath, tempFolderPluginPath, true);

            var pluginFolderPath = Utilities.GetContainingFolderPathAfterUnzip(tempFolderPluginPath);

            var metadataJsonFilePath = string.Empty;
            if (File.Exists(Path.Combine(pluginFolderPath, Constant.PluginMetadataFileName)))
                metadataJsonFilePath = Path.Combine(pluginFolderPath, Constant.PluginMetadataFileName);

            if (string.IsNullOrEmpty(metadataJsonFilePath) || string.IsNullOrEmpty(pluginFolderPath))
            {
                MessageBox.Show(Context.API.GetTranslation("plugin_pluginsmanager_install_errormetadatafile"),
                    Context.API.GetTranslation("plugin_pluginsmanager_install_error_title"));

                throw new FileNotFoundException(
                    string.Format("Unable to find plugin.json from the extracted zip file, or this path {0} does not exist", pluginFolderPath));
            }

            if (SameOrLesserPluginVersionExists(metadataJsonFilePath))
            {
                MessageBox.Show(string.Format(Context.API.GetTranslation("plugin_pluginsmanager_install_error_duplicate"), plugin.Name),
                    Context.API.GetTranslation("plugin_pluginsmanager_install_error_title"));

                throw new InvalidOperationException(
                    string.Format("A plugin with the same ID and version already exists, " +
                                  "or the version is greater than this downloaded plugin {0}",
                        plugin.Name));
            }

            var directory = string.IsNullOrEmpty(plugin.Version) ? $"{plugin.Name}-{Guid.NewGuid()}" : $"{plugin.Name}-{plugin.Version}";
            var newPluginPath = Path.Combine(DataLocation.PluginsDirectory, directory);

            FilesFolders.CopyAll(pluginFolderPath, newPluginPath);

            Directory.Delete(pluginFolderPath, true);
        }

        internal List<Result> RequestUninstall(string search)
        {
            var results = Context.API
                .GetAllPlugins()
                .Select(x =>
                    new Result
                    {
                        Title = $"{x.Metadata.Name} by {x.Metadata.Author}",
                        SubTitle = x.Metadata.Description,
                        IcoPath = x.Metadata.IcoPath,
                        Action = e =>
                        {
                            var message = string.Format(
                                Context.API.GetTranslation("plugin_pluginsmanager_uninstall_prompt"),
                                x.Metadata.Name, x.Metadata.Author,
                                Environment.NewLine, Environment.NewLine);

                            if (MessageBox.Show(message,
                                    Context.API.GetTranslation("plugin_pluginsmanager_uninstall_title"),
                                    MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                            {
                                _ = Task.Run(() =>
                                {
                                    Uninstall(x.Metadata);
                                    Context.API.RestartApp();
                                });
                                return true;
                            }

                            return false;
                        }
                    });

            return Search(results, search);
        }

        private static void Uninstall(PluginMetadata plugin, bool removedSetting = true)
        {
            if (removedSetting)
            {
                PluginManager.Settings.Plugins.Remove(plugin.ID);
                PluginManager.AllPlugins.RemoveAll(p => p.Metadata.ID == plugin.ID);
            }

            // Marked for deletion. Will be deleted on next start up
            using var _ = File.CreateText(Path.Combine(plugin.PluginDirectory, "NeedDelete.txt"));
        }

        private bool SameOrLesserPluginVersionExists(string metadataPath)
        {
            var newMetadata = JsonSerializer.Deserialize<PluginMetadata>(File.ReadAllText(metadataPath));
            return Context.API.GetAllPlugins()
                .Any(x => x.Metadata.ID == newMetadata.ID
                          && string.Compare(newMetadata.Version, x.Metadata.Version, StringComparison.Ordinal) <= 0);
        }
    }
}