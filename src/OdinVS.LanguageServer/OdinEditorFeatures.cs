using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.BraceCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using OdinVS.Core;

namespace OdinVS.LanguageServer;

// Public because VS discovers it through MEF. The TextMate language-configuration auto-close does not run here (see
// docs/dev/pitfalls.md), so closing pairs come from VS's own brace-completion provider for the Odin content type.
[Export(typeof(IBraceCompletionDefaultProvider))]
[ContentType(OdinContentType.Name)]
[BracePair('(', ')')]
[BracePair('{', '}')]
[BracePair('[', ']')]
[BracePair('"', '"')]
[BracePair('\'', '\'')]
[BracePair('`', '`')]
public sealed class OdinBraceCompletionProvider : IBraceCompletionDefaultProvider
{
}

// Public because VS discovers it through MEF. VS sets "BraceCompletion/Enabled" on every editor view from the language's
// preferences (fBraceCompletion), and for a language without a registered language service that comes out false, so VS's
// brace completion never starts (seen in the log on VS 18.11: manager present, Enabled=False). Turn it on for Odin views and
// again whenever VS switches it off.
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(OdinContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
public sealed class OdinBraceCompletionEnabler : IWpfTextViewCreationListener
{
    private readonly Log m_Log = Log.Default;

    public void TextViewCreated(IWpfTextView textView)
    {
        Enable(textView);
        // Not unsubscribed on purpose: the options bag belongs to the view, so the handler goes away with it.
        textView.Options.OptionChanged += (_, e) =>
        {
            if (e.OptionId == DefaultTextViewOptions.BraceCompletionEnabledOptionId.Name)
            {
                Enable(textView);
            }
        };
    }

    private void Enable(IWpfTextView textView)
    {
        if (textView.Options.GetOptionValue(DefaultTextViewOptions.BraceCompletionEnabledOptionId))
        {
            return;
        }

        textView.Options.SetOptionValue(DefaultTextViewOptions.BraceCompletionEnabledOptionId, true);
        m_Log.Write("brace completion was off for an Odin editor view; turned it on");
    }
}

// Public because VS discovers it through MEF. Runs before VS's completion handler: a letter or digit typed inside a number
// (`1.5f`) is inserted directly, so no identifier completion list opens inside the literal.
[Export(typeof(ICommandHandler))]
[Name("OdinNumericLiteralGuard")]
[ContentType(OdinContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[Order(Before = "CompletionCommandHandler")]
[method: ImportingConstructor]
public sealed class OdinNumericLiteralGuard(IEditorOperationsFactoryService editorOperations) : IChainedCommandHandler<TypeCharCommandArgs>
{
    public string DisplayName => "Odin numeric literal completion guard";

    public CommandState GetCommandState(TypeCharCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler();

    public void ExecuteCommand(TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
    {
        if (!args.TextView.Selection.IsEmpty || args.TextView.GetMultiSelectionBroker().AllSelections.Count > 1 || !StartsInsideNumber(args))
        {
            nextCommandHandler();
            return;
        }

        editorOperations.GetEditorOperations(args.TextView).InsertText(args.TypedChar.ToString());
    }

    private static bool StartsInsideNumber(TypeCharCommandArgs args)
    {
        SnapshotPoint caret = args.TextView.Caret.Position.BufferPosition;
        ITextSnapshotLine line = caret.GetContainingLine();
        string before = caret.Snapshot.GetText(line.Start.Position, caret.Position - line.Start.Position);
        return OdinNumericLiteral.ContinuesNumber(before, args.TypedChar);
    }
}