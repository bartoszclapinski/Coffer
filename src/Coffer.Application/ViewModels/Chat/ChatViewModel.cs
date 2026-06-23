using System.Collections.ObjectModel;
using Coffer.Core.Chat;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Chat;

/// <summary>
/// View-model behind the "Asystent" chat page (Phase 7). Holds the in-session transcript and
/// drives <see cref="IChatService"/>: each question is sent with the prior turns as history, and
/// the resulting answer (plus its read-only tool trace) is appended. The model never invents
/// numbers and never mutates data — answers come from read-only tools (doc 04). Conversation lives
/// in memory only; durable history is a later nicety. Two blocked states surface friendly guidance
/// instead of errors: no API key configured (<see cref="MissingApiKey"/>) and the monthly budget
/// cap reached (<see cref="BudgetExceeded"/>).
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _missingApiKey;

    [ObservableProperty]
    private bool _budgetExceeded;

    public ChatViewModel(IChatService chatService, ILogger<ChatViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentNullException.ThrowIfNull(logger);

        _chatService = chatService;
        _logger = logger;
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> SuggestedPrompts { get; } =
    [
        "Ile wydałem na paliwo w tym miesiącu?",
        "Pokaż moje wydatki według kategorii w czerwcu.",
        "Jak zmieniały się moje wydatki na spożywcze przez ostatnie pół roku?",
        "Pokaż ostatnie 10 transakcji.",
    ];

    public bool IsEmpty => Messages.Count == 0 && !IsBusy;

    [RelayCommand]
    private async Task UseSuggestionAsync(string prompt)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        InputText = prompt;
        await SendAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        var question = InputText.Trim();
        if (question.Length == 0)
        {
            return;
        }

        var history = Messages
            .Select(m => new ChatMessage(m.Author, m.Text, []))
            .ToList();

        Messages.Add(new ChatMessageViewModel(ChatAuthor.User, question, []));
        InputText = "";
        ErrorMessage = "";
        MissingApiKey = false;
        BudgetExceeded = false;
        IsBusy = true;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var turn = await _chatService.AskAsync(question, history, ct).ConfigureAwait(true);

            MissingApiKey = turn.MissingApiKey;
            BudgetExceeded = turn.BudgetExceeded;
            Messages.Add(new ChatMessageViewModel(ChatAuthor.Assistant, turn.Answer, turn.ToolTraces));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat turn failed");
            ErrorMessage = "Przepraszam, wystąpił błąd. Spróbuj ponownie za chwilę.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);
}
