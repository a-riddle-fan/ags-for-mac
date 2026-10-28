using System;
using System.Collections.Generic;
using System.IO;
using AGS.Types;

namespace AGS.Editor.Build
{
    /// <summary>
    /// Registers every upstream build target name. Settings.BuildTargets drops names that aren't
    /// registered and available, so without this a project saved on macOS would silently lose e.g.
    /// "Windows" from its targets. Real platform builds arrive in Phase 4; until then these targets
    /// only carry their names, and only the data file is built (via agsbuild).
    /// </summary>
    public static class KnownBuildTargets
    {
        // ags/Editor/AGS.Editor/BuildTargets/*.cs
        public static readonly string[] Names = { BuildTargetsInfo.DATAFILE_TARGET_NAME, "Windows", "Linux", "Android", "Web" };

        private static readonly object _lock = new object();
        private static bool _registered;

        public static void RegisterAll()
        {
            lock (_lock)
            {
                if (_registered) return;
                _registered = true;
                foreach (string name in Names)
                    BuildTargetsInfo.RegisterBuildTarget(new PlaceholderBuildTarget(name));
            }
        }

        private sealed class PlaceholderBuildTarget : IBuildTarget
        {
            public PlaceholderBuildTarget(string name) { Name = name; }

            public string Name { get; }
            public bool IsAvailable => true;
            public bool IsTargetedForBuild => false;
            public bool IsOnlyBuildTarget => false;
            public bool IsNormalBuildTarget => true;
            public string OutputDirectory => Name == BuildTargetsInfo.DATAFILE_TARGET_NAME ? "Data" : Name;
            public string OutputDirectoryFullPath => Path.Combine("Compiled", OutputDirectory);

            public IDictionary<string, string> GetRequiredLibraryPaths() => new Dictionary<string, string>();
            public string GetCompiledPath(params string[] parts) => Path.Combine(OutputDirectoryFullPath, Path.Combine(parts));
            public string[] GetPlatformStandardSubfolders() => new[] { OutputDirectoryFullPath };
            public string[] GetRequiredLibraryNames() => Array.Empty<string>();
            public void EnsureStandardSubfoldersExist(CompileMessages errors) => Directory.CreateDirectory(OutputDirectoryFullPath);
            public void DeleteMainGameData(string name, CompileMessages errors) { }

            public bool Build(CompileMessages errors, bool forceRebuild)
            {
                errors.Add(new CompileError($"Building for {Name} is not available in the macOS editor yet."));
                return false;
            }
        }
    }
}
