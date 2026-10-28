// ags4mac: forked from ags/Editor/AGS.Types/EditorFeatures/GameTemplate.cs.
// System.Drawing.Icon replaced by AGS.Imaging.IImage.
using System;
using AGS.Imaging;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace AGS.Types
{
    public class GameTemplate : BaseTemplate
    {
        public GameTemplate(string fileName, string description, IImage icon)
            : base(fileName, description, icon)
        {
        }
    }
}
