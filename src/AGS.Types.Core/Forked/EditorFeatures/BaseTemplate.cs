// ags4mac: forked from ags/Editor/AGS.Types/EditorFeatures/BaseTemplate.cs.
// System.Drawing.Icon replaced by AGS.Imaging.IImage.
using System;
using AGS.Imaging;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace AGS.Types
{
	public class BaseTemplate
	{
		protected string _fileName;
		protected string _friendlyName;
        protected string _description;
        protected IImage _icon;

        public BaseTemplate(string fileName, string description, IImage icon)
		{
			_fileName = fileName;
            _description = description;
            _icon = icon;
			if (_fileName != null)
			{
				_friendlyName = Path.GetFileNameWithoutExtension(_fileName);
			}
		}

		public string FileName
		{
			get { return _fileName; }
		}

		public string FriendlyName
		{
			get { return _friendlyName; }
		}

        public string Description
        {
            get { return _description; }
        }

		public IImage Icon
		{
			get { return _icon; }
		}
	}
}
