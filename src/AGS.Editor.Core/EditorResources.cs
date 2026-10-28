using System;
using System.IO;

namespace AGS.Editor
{
    /// <summary>Files embedded from upstream ags/Editor/AGS.Editor/Resources (same resource names).</summary>
    public static class EditorResources
    {
        private const string ResourcePrefix = "AGS.Editor.Resources.";

        public static Stream Open(string resourceFileName)
        {
            Stream stream = typeof(EditorResources).Assembly.GetManifestResourceStream(ResourcePrefix + resourceFileName);
            if (stream == null)
                throw new FileNotFoundException($"Editor resource '{resourceFileName}' is not embedded.", resourceFileName);
            return stream;
        }

        /// <summary>ResourceManager.GetResourceAsString: a text resource (UTF-8/ASCII).</summary>
        public static string ReadText(string resourceFileName)
        {
            using Stream stream = Open(resourceFileName);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static void CopyToFile(string resourceFileName, string fileName)
        {
            using Stream input = Open(resourceFileName);
            using FileStream output = File.Create(fileName);
            input.CopyTo(output);
        }
    }
}
