using System.Collections.Generic;
using AGS.Types;

namespace AGS.Editor
{
    public static class ScriptNames
    {
        // ags4mac: port of AGSEditor.GetFirstAvailableScriptName (ags/Editor/AGS.Editor/AGSEditor.cs).
        public static string GetFirstAvailableScriptName(Game game, string namePrefix, int startIndex = 1, Room room = null, List<int> skipIDs = null)
        {
            int tryIndex = startIndex;
            string tryName = null;
            bool canUseName = false;
            while (!canUseName)
            {
                if (skipIDs != null)
                {
                    while (skipIDs.IndexOf(tryIndex) >= 0) tryIndex++;
                }

                tryName = namePrefix + tryIndex;
                canUseName = !game.IsScriptNameAlreadyUsed(tryName, null);
                if (canUseName && room != null)
                    canUseName = !room.IsScriptNameAlreadyUsed(tryName, null);
                if (!canUseName)
                {
                    tryIndex++;
                }
                else if (skipIDs != null)
                {
                    skipIDs.Add(tryIndex);
                }
            }
            return tryName;
        }
    }
}
