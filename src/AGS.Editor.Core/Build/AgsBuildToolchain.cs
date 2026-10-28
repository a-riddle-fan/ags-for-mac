using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AGS.Types;

namespace AGS.Editor.Build
{
    /// <summary>
    /// Builds a game with upstream's command-line toolchain (agsbuild, which drives agscc, agf2dta,
    /// crmpak, spriteimport, agspak, trac, ...). The tools folder holds all of them plus agsdefns.sh,
    /// as assembled by scripts/build-upstream-tools.sh.
    /// </summary>
    public sealed class AgsBuildToolchain
    {
        public const string ToolsEnvironmentVariable = "AGS4MAC_TOOLS";

        public AgsBuildToolchain(string toolsDirectory)
        {
            ToolsDirectory = Path.GetFullPath(toolsDirectory);
            if (!File.Exists(AgsBuildPath))
                throw new FileNotFoundException($"agsbuild was not found in '{ToolsDirectory}'.", AgsBuildPath);
        }

        public string ToolsDirectory { get; }
        public string AgsBuildPath => Path.Combine(ToolsDirectory, "agsbuild");

        /// <summary>
        /// Finds the tools: an explicit directory, then $AGS4MAC_TOOLS, then the app bundle
        /// (Contents/Resources/tools), then build/agstools of a development checkout above
        /// <paramref name="searchFrom"/>. Returns null if none is found.
        /// </summary>
        public static AgsBuildToolchain Locate(string explicitDirectory = null, string searchFrom = null)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitDirectory)) candidates.Add(explicitDirectory);
            string env = Environment.GetEnvironmentVariable(ToolsEnvironmentVariable);
            if (!string.IsNullOrEmpty(env)) candidates.Add(env);
            // Inside "AGS Editor.app": Contents/MacOS (the app) and Contents/Resources/tools.
            string baseDir = searchFrom ?? AppContext.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "tools"));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "tools")));
            for (string dir = searchFrom ?? AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
                candidates.Add(Path.Combine(dir, "build", "agstools"));

            foreach (string candidate in candidates)
                if (File.Exists(Path.Combine(candidate, "agsbuild")))
                    return new AgsBuildToolchain(candidate);
            return null;
        }

        /// <summary>
        /// Builds the game's data files from the saved Game.agf into <paramref name="outputDirectory"/>
        /// (default: Compiled/Data, like the editor's data file target). Save the project first.
        /// </summary>
        public BuildResult Build(string gameFile, string outputDirectory = null, Action<string> log = null)
        {
            string projectDir = Path.GetDirectoryName(Path.GetFullPath(gameFile));
            outputDirectory = Path.GetFullPath(outputDirectory ?? Path.Combine(projectDir, "Compiled", "Data"));
            Directory.CreateDirectory(outputDirectory); // agsbuild requires an existing output folder

            var psi = new ProcessStartInfo(AgsBuildPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = projectDir,
            };
            psi.ArgumentList.Add(Path.GetFullPath(gameFile));
            psi.ArgumentList.Add(outputDirectory);
            psi.ArgumentList.Add("--sys-dir");
            psi.ArgumentList.Add(ToolsDirectory);

            var output = new StringBuilder();
            object sync = new object();
            void OnLine(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (sync) output.AppendLine(e.Data);
                log?.Invoke(e.Data);
            }

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += OnLine;
            process.ErrorDataReceived += OnLine;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string text = output.ToString();
            return new BuildResult(process.ExitCode, outputDirectory, text, ParseMessages(text, process.ExitCode));
        }

        // agscc: "Error: compile failed at Doors.asc, line 11 : Error (line 11): undefined symbol 'x'"
        //        (also "preprocessor failed at ..."); section names are script file names.
        private static readonly Regex ScriptError = new Regex(
            @"(?:compile|preprocessor) failed at (?<script>.+?), line (?<line>\d+) : (?:Error \(line \d+\): )?(?<message>.*)$",
            RegexOptions.Multiline);

        // agsbuild reports failing tasks ("Task <name>: failed") with the tool's output above them.
        // A header error fails every script that includes the header; it is reported once.
        internal static CompileMessages ParseMessages(string output, int exitCode)
        {
            var messages = new CompileMessages();
            var seen = new HashSet<string>();
            foreach (Match m in ScriptError.Matches(output))
            {
                string script = m.Groups["script"].Value.Trim();
                int line = int.Parse(m.Groups["line"].Value);
                string message = m.Groups["message"].Value.Trim();
                if (seen.Add($"{script}:{line}:{message}"))
                    messages.Add(new CompileError(message, script, line));
            }
            if (messages.Count == 0)
            {
                foreach (Match m in Regex.Matches(output, @"^(?:.*?)(?:Error|ERROR)[^:\n]*:\s*(.+)$", RegexOptions.Multiline))
                    messages.Add(new CompileError(m.Groups[1].Value.Trim()));
                foreach (Match m in Regex.Matches(output, @"^Task (\S+): failed", RegexOptions.Multiline))
                    messages.Add(new CompileError($"Build step '{m.Groups[1].Value}' failed."));
            }
            if (exitCode != 0 && messages.Count == 0)
                messages.Add(new CompileError($"agsbuild exited with code {exitCode}."));
            return messages;
        }
    }

    public sealed class BuildResult
    {
        public BuildResult(int exitCode, string outputDirectory, string log, CompileMessages messages)
        {
            ExitCode = exitCode;
            OutputDirectory = outputDirectory;
            Log = log;
            Messages = messages;
        }

        public bool Succeeded => ExitCode == 0 && !Messages.HasErrors;
        public int ExitCode { get; }
        public string OutputDirectory { get; }
        public string Log { get; }
        public CompileMessages Messages { get; }
    }
}
