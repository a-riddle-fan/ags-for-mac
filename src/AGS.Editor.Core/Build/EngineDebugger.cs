using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;
using AGS.Types;

namespace AGS.Editor.Build
{
    /// <summary>The state of a debugging session (upstream DebugState).</summary>
    public enum DebugSessionState { NotRunning, Running, Paused }

    /// <summary>
    /// Runs the game in the engine under the editor's control (upstream DebugController with
    /// FileBasedEngineCommunication): the engine is started with "--enabledebugger &lt;token&gt; file=dir:&lt;folder&gt;"
    /// and both sides exchange XML messages through dbgsend_&lt;token&gt;.tmp (editor → engine) and
    /// dbgrecv_&lt;token&gt;.tmp (engine → editor) in that folder (ags4mac engine patch 0007). Breakpoints, Resume, Step, Pause and Stop work as in the upstream editor.
    /// </summary>
    public sealed class EngineDebugger : IDisposable
    {
        private const string SendFile = "dbgsend_{0}.tmp";
        private const string ReceiveFile = "dbgrecv_{0}.tmp";

        private readonly string _engine;
        private readonly string _gameFile;
        private string _token;
        private string _folder;
        private Process _process;
        private Thread _receiver;
        private Thread _sender;
        private BlockingCollection<string> _outgoing;
        private volatile bool _stopping;

        public EngineDebugger(string engineExecutable, string gameFile)
        {
            _engine = engineExecutable;
            _gameFile = gameFile;
        }

        public DebugSessionState State { get; private set; } = DebugSessionState.NotRunning;

        /// <summary>The engine stopped at a breakpoint, a step or a script error (call stack, error message or null).</summary>
        public event Action<DebugCallStack> BreakAtLocation;
        /// <summary>The engine's log output (LOG messages).</summary>
        public event Action<string> Log;
        public event Action<DebugSessionState> StateChanged;
        public event Action Exited;

        /// <summary>Starts the engine and sends START, the breakpoints ("Script.asc", line) and READY.</summary>
        public void Start(IEnumerable<(string Script, int Line)> breakpoints)
        {
            if (State != DebugSessionState.NotRunning)
                throw new InvalidOperationException("The game is already running.");
            _token = Guid.NewGuid().ToString("N").Substring(0, 12);
            _folder = Path.Combine("/tmp", "agsdbg-" + _token);
            Directory.CreateDirectory(_folder);
            _stopping = false;

            var psi = new ProcessStartInfo(_engine)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_gameFile),
            };
            psi.ArgumentList.Add("--enabledebugger");
            psi.ArgumentList.Add(_token);
            psi.ArgumentList.Add("file=dir:" + _folder); // files are the default outside Windows, but said explicitly
            psi.ArgumentList.Add(_gameFile);
            _process = Process.Start(psi);
            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.Exited += (s, e) => OnExited();

            SetState(DebugSessionState.Running);
            _receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "AGS debugger receiver" };
            _receiver.Start();
            _outgoing = new BlockingCollection<string>();
            _sender = new Thread(SendLoop) { IsBackground = true, Name = "AGS debugger sender" };
            _sender.Start();

            // InitializeEngine: START, breakpoints, READY (the engine waits for READY before running the game)
            Send("<Engine Command=\"START\" EditorWindow=\"0\" />");
            foreach (var (script, line) in breakpoints)
                Send(BreakpointMessage("SETBREAK", script, line));
            Send("<Engine Command=\"READY\" EditorWindow=\"0\" />");
        }

        private static string BreakpointMessage(string command, string script, int line) =>
            "<Engine Command=\"" + command + " $" + script + "$" + line + "$\"></Engine>";

        public void AddBreakpoint(string script, int line)
        {
            if (State != DebugSessionState.NotRunning) Send(BreakpointMessage("SETBREAK", script, line));
        }

        public void RemoveBreakpoint(string script, int line)
        {
            if (State != DebugSessionState.NotRunning) Send(BreakpointMessage("DELBREAK", script, line));
        }

        public void Resume()
        {
            if (State != DebugSessionState.Paused) return;
            SetState(DebugSessionState.Running);
            Send("<Engine Command=\"RESUME\" />");
        }

        public void StepInto()
        {
            if (State != DebugSessionState.Paused) return;
            SetState(DebugSessionState.Running);
            Send("<Engine Command=\"STEP\" />");
        }

        /// <summary>PauseExecution: the STEP command breaks at the next script line.</summary>
        public void Pause()
        {
            if (State == DebugSessionState.Running) Send("<Engine Command=\"STEP\" />");
        }

        public void Stop()
        {
            if (State == DebugSessionState.NotRunning) return;
            Send("<Engine Command=\"EXIT\" />");
            // the engine exits at its next update; make sure it does
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_process != null && !_process.WaitForExit(3000))
                        _process.Kill();
                }
                catch (InvalidOperationException) { }
            });
        }

        /// <summary>Queues a message for the engine; the sender thread writes it once the engine took the previous one.</summary>
        private void Send(string message)
        {
            try { _outgoing?.Add(message); }
            catch (InvalidOperationException) { } // session over
        }

        // Messages are written in order and never overwrite one the engine has not read yet: while starting
        // (the first launch of a freshly built engine can take seconds) the engine only reads them once it
        // reaches its debugger initialisation.
        private void SendLoop()
        {
            BlockingCollection<string> queue = _outgoing;
            string target = Path.Combine(_folder, string.Format(SendFile, _token));
            string temp = Path.Combine(_folder, "dbgsend_" + _token + ".part");
            try
            {
                foreach (string message in queue.GetConsumingEnumerable())
                {
                    while (File.Exists(target) && !_stopping)
                        Thread.Sleep(5);
                    if (_stopping) return;
                    File.WriteAllText(temp, message, AnsiEncoding.Default);
                    File.Move(temp, target, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ObjectDisposedException)
            {
                // the engine exited and the folder is gone
            }
        }

        private void ReceiveLoop()
        {
            string file = Path.Combine(_folder, string.Format(ReceiveFile, _token));
            while (!_stopping)
            {
                if (File.Exists(file))
                {
                    string data = null;
                    try
                    {
                        data = File.ReadAllText(file, AnsiEncoding.Default);
                        File.Delete(file);
                    }
                    catch (IOException) { Thread.Sleep(5); continue; }
                    try
                    {
                        var doc = new XmlDocument();
                        doc.LoadXml(data);
                        Dispatch(doc);
                    }
                    catch (XmlException)
                    {
                        Log?.Invoke("[Debugger] faulty message: " + data);
                    }
                    continue;
                }
                Thread.Sleep(10);
            }
        }

        // DebugController._communicator_MessageReceived
        private void Dispatch(XmlDocument doc)
        {
            string command = doc.DocumentElement.GetAttribute("Command");
            if (command == "BREAK" || command == "ERROR")
            {
                SetState(DebugSessionState.Paused);
                string errorMessage = doc.DocumentElement.SelectSingleNode("ErrorMessage")?.InnerText;
                string callStack = doc.DocumentElement.SelectSingleNode("ScriptState")?.InnerText ?? string.Empty;
                BreakAtLocation?.Invoke(ParseCallStack(callStack, errorMessage));
            }
            else if (command == "EXIT")
            {
                OnExited();
            }
            else if (command == "LOG")
            {
                string text = doc.DocumentElement.SelectSingleNode("Text")?.InnerText;
                if (text != null) Log?.Invoke(text);
            }
        }

        /// <summary>DebugController.ParseCallStackIntoObjectForm: lines like 'in "room1.asc", line 12'.</summary>
        public static DebugCallStack ParseCallStack(string callStackFromEngine, string errorMessage)
        {
            var result = new DebugCallStack(errorMessage);
            foreach (string callStackEntry in callStackFromEngine.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int firstSpeechMark = callStackEntry.IndexOf('"');
                if (firstSpeechMark < 0) continue; // "(and more...)"
                string callStack = callStackEntry.Substring(firstSpeechMark + 1);
                int endQuote = callStack.IndexOf('"');
                if (endQuote < 0) continue;
                string scriptName = callStack.Substring(0, endQuote);
                string lineNumberText = callStack.Substring(endQuote);
                int lineAt = lineNumberText.IndexOf("line ", StringComparison.Ordinal);
                if (lineAt < 0) continue;
                lineNumberText = lineNumberText.Substring(lineAt + 5);
                int i = 0;
                while (i < lineNumberText.Length && char.IsDigit(lineNumberText, i)) i++;
                if (i == 0) continue;
                result.AddLine(scriptName, Convert.ToInt32(lineNumberText.Substring(0, i)));
            }
            return result;
        }

        private void SetState(DebugSessionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        private void OnExited()
        {
            if (State == DebugSessionState.NotRunning) return;
            _stopping = true;
            _outgoing?.CompleteAdding();
            SetState(DebugSessionState.NotRunning);
            Exited?.Invoke();
            try { if (_folder != null && Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch (IOException) { }
        }

        public void Dispose()
        {
            _stopping = true;
            _outgoing?.CompleteAdding();
            try
            {
                if (_process != null && !_process.HasExited)
                    _process.Kill();
            }
            catch (InvalidOperationException) { }
            _process?.Dispose();
        }
    }
}
