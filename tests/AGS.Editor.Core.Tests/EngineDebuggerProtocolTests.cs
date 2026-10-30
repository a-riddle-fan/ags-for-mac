using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AGS.Editor.Build;

namespace AGS.Editor.Core.Tests
{
    /// <summary>The editor side of the file-based debugger protocol, against a fake engine script.</summary>
    public class EngineDebuggerProtocolTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-dbgproto-" + Guid.NewGuid().ToString("N"));

        public EngineDebuggerProtocolTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void MessagesWaitForASlowlyStartingEngineAndArriveInOrder()
        {
            // Like a first launch: the engine only reads its messages after 4 seconds, then one at a time
            // (dbgsend_<token>.tmp is read and deleted), writes them to a log and quits.
            string log = Path.Combine(_dir, "received.log");
            string engine = Path.Combine(_dir, "fake-engine.sh");
            File.WriteAllText(engine,
                "#!/bin/sh\n" +
                "token=\"$2\"; dir=\"${3#file=dir:}\"\n" +
                "sleep 4\n" +
                "while true; do\n" +
                "  if [ -f \"$dir/dbgsend_$token.tmp\" ]; then\n" +
                "    msg=$(cat \"$dir/dbgsend_$token.tmp\"); rm \"$dir/dbgsend_$token.tmp\"\n" +
                "    echo \"$msg\" >> \"" + log + "\"\n" +
                "    case \"$msg\" in *READY*) exit 0;; esac\n" +
                "  fi\n" +
                "  sleep 0.05\n" +
                "done\n");
            File.SetUnixFileMode(engine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            using var debugger = new EngineDebugger(engine, Path.Combine(_dir, "game.ags"));
            var exited = new ManualResetEventSlim();
            debugger.Exited += exited.Set;
            var watch = Stopwatch.StartNew();
            debugger.Start(new[] { ("GlobalScript.asc", 12), ("room1.asc", 3) });
            Assert.True(watch.ElapsedMilliseconds < 1000, $"Start blocked the caller for {watch.ElapsedMilliseconds} ms");

            Assert.True(exited.Wait(TimeSpan.FromSeconds(30)), "the fake engine did not get READY");
            string[] lines = File.ReadAllLines(log);
            Assert.Equal(4, lines.Length);
            Assert.Contains("START", lines[0]);
            Assert.Contains("SETBREAK $GlobalScript.asc$12$", lines[1]);
            Assert.Contains("SETBREAK $room1.asc$3$", lines[2]);
            Assert.Contains("READY", lines[3]);
        }
    }
}
