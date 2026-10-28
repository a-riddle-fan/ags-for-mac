namespace AGS.Native
{
    /// <summary>Extraction of game (.agt) and room (.art) templates (upstream extract_template_files*).</summary>
    public static class NativeTemplates
    {
        public static void ExtractGame(string templatePath, string destinationDirectory)
        {
            NativeLibraryInfo.EnsureCompatible();
            NativeImage.Check(NativeMethods.TemplateExtractGame(templatePath, destinationDirectory));
        }

        public static void ExtractRoom(string templatePath, string destinationDirectory, int newRoomNumber)
        {
            NativeLibraryInfo.EnsureCompatible();
            NativeImage.Check(NativeMethods.TemplateExtractRoom(templatePath, destinationDirectory, newRoomNumber));
        }
    }
}
