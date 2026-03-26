using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace revit_mcp_plugin.Configuration
{
    public class ConfigurationManager
    {
        private static readonly string[] DefaultEnabledCommands =
        {
            "execute",
            "exec",
            "lookup_engine_query",
            "selection_roots",
            "object_member_groups",
            "expand_members",
            "navigate_object"
        };

        private readonly ILogger _logger;
        private readonly string _configPath;

        public FrameworkConfig Config { get; private set; }

        public ConfigurationManager(ILogger logger)
        {
            _logger = logger;

            // 配置文件路径
            // Configuration file path.
            _configPath = PathManager.GetCommandRegistryFilePath();
        }

        /// <summary>
        /// <para>加载配置</para>
        /// <para>Load configuration from a JSON file.</para>
        /// </summary>
        public void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    Config = JsonConvert.DeserializeObject<FrameworkConfig>(json) ?? new FrameworkConfig();
                    EnsureDefaultBridgeCommands();
                    _logger.Info("已加载配置文件: {0}\nConfiguration file loaded: {0}", _configPath);
                }
                else
                {
                    Config = new FrameworkConfig();
                    EnsureDefaultBridgeCommands();
                    _logger.Error("未找到配置文件\nNo configuration file found.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("加载配置文件失败: {0}\nFailed to load configuration file: {0}", ex.Message);
                Config ??= new FrameworkConfig();
                EnsureDefaultBridgeCommands();
            }

            // 记录加载时间
            // Register load time.
            _lastConfigLoadTime = DateTime.Now;
        }

        ///// <summary>
        ///// <para>重新加载配置</para>
        ///  <para>Reload configuration.</para>
        ///// </summary>
        //public void RefreshConfiguration()
        //{
        //    LoadConfiguration();
        //    _logger.Info("配置已重新加载\nConfiguration has been reloaded.");
        //}

        //public bool HasConfigChanged()
        //{
        //    if (!File.Exists(_configPath))
        //        return false;

        //    DateTime lastWrite = File.GetLastWriteTime(_configPath);
        //    return lastWrite > _lastConfigLoadTime;
        //}

        private DateTime _lastConfigLoadTime;

        private void EnsureDefaultBridgeCommands()
        {
            var discoveredCommands = DiscoverDefaultBridgeCommands();
            if (discoveredCommands.Count == 0)
            {
                return;
            }

            bool changed = false;
            var existingCommands = Config.Commands.ToDictionary(
                command => command.CommandName,
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var discoveredCommand in discoveredCommands)
            {
                if (existingCommands.TryGetValue(discoveredCommand.CommandName, out var existingCommand))
                {
                    if (!existingCommand.Enabled)
                    {
                        existingCommand.Enabled = true;
                        changed = true;
                    }

                    if (string.IsNullOrWhiteSpace(existingCommand.AssemblyPath))
                    {
                        existingCommand.AssemblyPath = discoveredCommand.AssemblyPath;
                        changed = true;
                    }

                    if ((existingCommand.SupportedRevitVersions == null || existingCommand.SupportedRevitVersions.Length == 0) &&
                        discoveredCommand.SupportedRevitVersions?.Length > 0)
                    {
                        existingCommand.SupportedRevitVersions = discoveredCommand.SupportedRevitVersions;
                        changed = true;
                    }
                }
                else
                {
                    Config.Commands.Add(discoveredCommand);
                    changed = true;
                }
            }

            if (changed)
            {
                SaveConfiguration();
            }
        }

        private List<CommandConfig> DiscoverDefaultBridgeCommands()
        {
            var results = new List<CommandConfig>();
            string commandsDirectory = PathManager.GetCommandsDirectoryPath();

            if (!Directory.Exists(commandsDirectory))
            {
                return results;
            }

            foreach (var directory in Directory.GetDirectories(commandsDirectory))
            {
                string commandJsonPath = Path.Combine(directory, "command.json");
                if (!File.Exists(commandJsonPath))
                {
                    continue;
                }

                try
                {
                    var root = JObject.Parse(File.ReadAllText(commandJsonPath));
                    string commandSetName = root["name"]?.Value<string>() ?? Path.GetFileName(directory);
                    var versionDirectories = Directory.GetDirectories(directory)
                        .Select(Path.GetFileName)
                        .Where(name => int.TryParse(name, out _))
                        .ToArray();

                    foreach (var command in root["commands"] as JArray ?? new JArray())
                    {
                        string commandName = command["commandName"]?.Value<string>() ?? string.Empty;
                        if (!DefaultEnabledCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string assemblyPath = command["assemblyPath"]?.Value<string>() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(assemblyPath))
                        {
                            continue;
                        }

                        results.Add(new CommandConfig
                        {
                            CommandName = commandName,
                            Description = command["description"]?.Value<string>() ?? string.Empty,
                            AssemblyPath = Path.Combine(commandSetName, "{VERSION}", assemblyPath),
                            Enabled = true,
                            SupportedRevitVersions = versionDirectories
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("扫描默认桥接命令失败: {0}\nFailed to scan default bridge commands: {0}", ex.Message);
                }
            }

            return results
                .GroupBy(command => command.CommandName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private void SaveConfiguration()
        {
            string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }
    }
}
