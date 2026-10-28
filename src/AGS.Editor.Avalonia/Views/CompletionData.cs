using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AGS.Editor;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>An entry of the script editor's completion popup.</summary>
    internal sealed class CompletionData : ICompletionData
    {
        private readonly CompletionItem _item;

        public CompletionData(CompletionItem item)
        {
            _item = item;
        }

        public IImage Image => null;
        public string Text => _item.Text;
        public object Content => _item.Kind switch
        {
            CompletionKind.Function => _item.Text + "()",
            _ => _item.Text,
        };
        public object Description => _item.Description;
        public double Priority => _item.Kind == CompletionKind.Keyword ? -1 : 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _item.Text);
        }
    }
}
