using System.Collections.Generic;
using System.Linq;
using Avalonia.Input.Platform;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InstallDragDrop();
            Closing += (s, e) => SaveDockLayout();
            // scripts changed outside the editor are offered for reloading when the window becomes active
            Activated += (s, e) => (DataContext as MainWindowViewModel)?.OnMainWindowActivated();
            Deactivated += (s, e) => { if (DataContext is MainWindowViewModel vm) vm.IsWindowActive = false; };
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is MainWindowViewModel vm)
            {
                Tag = vm; // templates reach the view model through $parent[Window].Tag (also in floating windows)
                SetUpDock(vm);
                vm.ResetLayout = ResetDockLayout;
                vm.PickProjectFile = PickProjectFileAsync;
                vm.PickSaveFile = PickSaveFileAsync;
                vm.PickOpenFiles = PickOpenFilesAsync;
                vm.PickFolder = PickFolderAsync;
                vm.CopyImageToClipboard = CopyImageToClipboardAsync;
                vm.GetClipboardImage = GetClipboardImageAsync;
                vm.BringToFront = () => { Activate(); Topmost = true; Topmost = false; };
            }
        }

        private async Task CopyImageToClipboardAsync(AGS.Imaging.PixelImage image)
        {
            if (Clipboard == null) return;
            using var bitmap = Services.ImageConversion.ToBitmap(image, transparentMagenta: true);
            await Clipboard.SetBitmapAsync(bitmap);
        }

        /// <summary>The clipboard image as 32-bit BGRA, or null.</summary>
        private async Task<AGS.Imaging.PixelImage> GetClipboardImageAsync()
        {
            if (Clipboard == null) return null;
            using var bitmap = await Clipboard.TryGetBitmapAsync();
            if (bitmap == null) return null;
            return Services.ImageConversion.FromBitmap(bitmap);
        }

        private async Task<string> PickProjectFileAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open AGS game project",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("AGS game project") { Patterns = new[] { "*.agf" } } },
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        private async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string[] extensions)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                // an extension "*" allows any file name (e.g. SCI fonts "font.*"; the macOS panel only filters by extension)
                FileTypeFilter = new[] { new FilePickerFileType(string.Join(", ", extensions.Select(e => e == "*" ? "all files" : e.ToUpperInvariant())))
                    { Patterns = extensions.Select(e => e == "*" ? "*" : "*." + e).ToArray() } },
            });
            return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToList();
        }

        private async Task<string> PickFolderAsync(string title)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }

        private async Task<string> PickSaveFileAsync(string suggestedName, string[] extensions)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                FileTypeChoices = extensions.Select(e => new FilePickerFileType(e.ToUpperInvariant()) { Patterns = new[] { "*." + e } }).ToArray(),
            });
            return file?.TryGetLocalPath();
        }


    }
}
