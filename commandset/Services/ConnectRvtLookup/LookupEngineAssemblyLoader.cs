using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

internal static class LookupEngineAssemblyLoader
{
    private static readonly string[] AssemblyNames =
    {
        "RevitLookup",
        "LookupEngine",
        "LookupEngine.Abstractions"
    };

    public static void EnsureLoaded()
    {
        var missing = GetMissingAssemblies();
        if (missing.Count == 0)
        {
            return;
        }

        var searchDirectories = BuildSearchDirectories();
        var attemptedPaths = new List<string>();
        var loaded = new List<string>();
        var failures = new List<string>();

        foreach (var directory in searchDirectories)
        {
            foreach (var assemblyName in missing.ToList())
            {
                var path = Path.Combine(directory, $"{assemblyName}.dll");
                attemptedPaths.Add(path);

                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    Assembly.LoadFrom(path);
                    missing.Remove(assemblyName);
                    loaded.Add(assemblyName);
                }
                catch (Exception ex)
                {
                    failures.Add($"{assemblyName}: {ex.GetType().Name}");
                }
            }

            if (missing.Count == 0)
            {
                break;
            }
        }

        if (loaded.Count > 0)
        {
            ConnectRvtLookupDiagnostics.Info(
                nameof(LookupEngineAssemblyLoader),
                "已尝试自动加载 RevitLookup/LookupEngine 程序集。",
                ConnectRvtLookupDiagnostics.Context("loaded", string.Join(",", loaded)));
        }

        if (missing.Count > 0)
        {
            var attemptHint = attemptedPaths.Count == 0
                ? "no_paths"
                : string.Join(";", attemptedPaths.Take(6));

            ConnectRvtLookupDiagnostics.Warning(
                nameof(LookupEngineAssemblyLoader),
                "自动加载 RevitLookup/LookupEngine 未找到或未成功。",
                ConnectRvtLookupDiagnostics.Context("missing", string.Join(",", missing)),
                ConnectRvtLookupDiagnostics.Context("attempted", attemptHint),
                failures.Count == 0 ? null : ConnectRvtLookupDiagnostics.Context("failures", string.Join(",", failures)));
        }
    }

    private static HashSet<string> GetMissingAssemblies()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyName in AssemblyNames)
        {
            if (!loadedAssemblies.Any(assembly =>
                    string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)))
            {
                missing.Add(assemblyName);
            }
        }

        return missing;
    }

    private static IEnumerable<string> BuildSearchDirectories()
    {
        var directories = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var addinsRoot = Path.Combine(appData, "Autodesk", "Revit", "Addins");
            if (Directory.Exists(addinsRoot))
            {
                foreach (var versionDir in SafeGetDirectories(addinsRoot))
                {
                    directories.Add(Path.Combine(versionDir, "RevitLookup"));
                    directories.Add(versionDir);
                }
            }
        }

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            directories.Add(Path.Combine(baseDir, "RevitLookup"));
            directories.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "RevitLookup")));
            directories.Add(baseDir);
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SafeGetDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
