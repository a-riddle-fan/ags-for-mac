// ags4mac: forked from ags/Editor/AGS.Types/EditorFeatures/RoomTemplate.cs.
// System.Drawing.Icon replaced by AGS.Imaging.IImage.
using System;
using AGS.Imaging;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace AGS.Types
{
	public class RoomTemplate : BaseTemplate
	{
		public RoomTemplate(string fileName, IImage icon)
			: base(fileName, null, icon)
		{
		}

		public RoomTemplate(string fileName, IImage icon, string friendlyName)
			: base(fileName, null, icon)
		{
			_friendlyName = friendlyName;
		}
	}
}
