using System.IO;
using System.Linq;
using AGS.Editor.Build;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    public class BuildMessagesTests
    {
        [Fact]
        public void ParsesScriptErrorsWithFileAndLineOnce()
        {
            string output = string.Join("\n",
                "Task: agscc:Doors.asc",
                "Error: compile failed at Doors.ash, line 63 : Error (line 63): Expected integer value after '='",
                "Task agscc:Doors.asc: failed",
                "Task: agscc:room2.asc",
                "Error: compile failed at Doors.ash, line 63 : Error (line 63): Expected integer value after '='",
                "Task agscc:room2.asc: failed",
                "Error: compile failed at room5.asc, line 7 : Error (line 7): undefined symbol 'x'");

            CompileMessages messages = AgsBuildToolchain.ParseMessages(output, 255);

            Assert.Equal(2, messages.Count);
            var first = Assert.IsType<CompileError>(messages[0]);
            Assert.Equal(("Doors.ash", 63, "Expected integer value after '='"), (first.ScriptName, first.LineNumber, first.Message));
            Assert.Equal(("room5.asc", 7), (messages[1].ScriptName, messages[1].LineNumber));
        }

        [Fact]
        public void GenericFailuresStillReported()
        {
            CompileMessages messages = AgsBuildToolchain.ParseMessages("Task agspak:game.ags: failed", 1);
            Assert.Contains(messages, m => m.Message.Contains("agspak:game.ags"));
        }
    }
}
