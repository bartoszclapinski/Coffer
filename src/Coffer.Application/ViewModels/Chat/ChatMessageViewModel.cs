using Coffer.Core.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Chat;

/// <summary>
/// One message in the chat transcript as the view renders it: who spoke, the text, and (for
/// assistant turns) the read-only tools that ran to produce it. The tool trace is the
/// transparency panel from doc 04 — it lets the owner see answers come from real queries, not
/// invented numbers. Collapsed by default; <see cref="ToggleToolTraceCommand"/> expands it.
/// </summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessageViewModel(ChatAuthor author, string text, IReadOnlyList<ChatToolTrace> toolTraces)
    {
        ArgumentNullException.ThrowIfNull(toolTraces);

        Author = author;
        Text = text;
        ToolTraceLines = toolTraces.Select(Format).ToArray();
    }

    [ObservableProperty]
    private bool _isToolTraceExpanded;

    public ChatAuthor Author { get; }

    public string Text { get; }

    public IReadOnlyList<string> ToolTraceLines { get; }

    public bool IsUser => Author == ChatAuthor.User;

    public bool IsAssistant => Author == ChatAuthor.Assistant;

    public bool HasToolTraces => ToolTraceLines.Count > 0;

    [RelayCommand]
    private void ToggleToolTrace() => IsToolTraceExpanded = !IsToolTraceExpanded;

    private static string Format(ChatToolTrace trace) => $"{trace.ToolName}({trace.ArgumentsJson})";
}
