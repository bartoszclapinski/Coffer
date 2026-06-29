using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Chat;
using Coffer.Core.Chat;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Chat;

public class ChatViewModelTests
{
    [Fact]
    public async Task Send_AppendsUserAndAssistantMessages_AndClearsInput()
    {
        var service = new FakeChatService(new ChatTurn("Wydałeś 320,50 PLN.", [new ChatToolTrace("GetTotalSpent", "{\"from\":\"2026-06-01\"}")]));
        var vm = Vm(service);
        vm.InputText = "Ile wydałem?";

        await vm.SendCommand.ExecuteAsync(null);

        vm.Messages.Should().HaveCount(2);
        vm.Messages[0].IsUser.Should().BeTrue();
        vm.Messages[0].Text.Should().Be("Ile wydałem?");
        vm.Messages[1].IsAssistant.Should().BeTrue();
        vm.Messages[1].Text.Should().Be("Wydałeś 320,50 PLN.");
        vm.Messages[1].ToolTraceLines.Should().ContainSingle().Which.Should().StartWith("GetTotalSpent(");
        vm.InputText.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Send_PassesQuestionAndPriorHistory_ToService()
    {
        var service = new FakeChatService(
            new ChatTurn("pierwsza", []),
            new ChatTurn("druga", []));
        var vm = Vm(service);

        vm.InputText = "pytanie 1";
        await vm.SendCommand.ExecuteAsync(null);
        vm.InputText = "pytanie 2";
        await vm.SendCommand.ExecuteAsync(null);

        service.Calls.Should().HaveCount(2);
        service.Calls[1].Question.Should().Be("pytanie 2");
        service.Calls[1].History.Should().HaveCount(2, "the first user question and its answer are sent as history");
        service.Calls[1].History[0].Author.Should().Be(ChatAuthor.User);
        service.Calls[1].History[0].Text.Should().Be("pytanie 1");
        service.Calls[1].History[1].Author.Should().Be(ChatAuthor.Assistant);
        service.Calls[1].History[1].Text.Should().Be("pierwsza");
    }

    [Fact]
    public void Send_CannotExecute_WhenInputBlank()
    {
        var vm = Vm(new FakeChatService(new ChatTurn("x", [])));

        vm.InputText = "   ";
        vm.SendCommand.CanExecute(null).Should().BeFalse();

        vm.InputText = "coś";
        vm.SendCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Send_MissingApiKey_SetsFlagAndStillShowsAnswer()
    {
        var service = new FakeChatService(new ChatTurn("Dodaj klucz API w ustawieniach.", [], MissingApiKey: true));
        var vm = Vm(service);
        vm.InputText = "pytanie";

        await vm.SendCommand.ExecuteAsync(null);

        vm.MissingApiKey.Should().BeTrue();
        vm.Messages[1].Text.Should().Be("Dodaj klucz API w ustawieniach.");
    }

    [Fact]
    public async Task Send_BudgetExceeded_SetsFlag()
    {
        var service = new FakeChatService(new ChatTurn("Budżet wyczerpany.", [], BudgetExceeded: true));
        var vm = Vm(service);
        vm.InputText = "pytanie";

        await vm.SendCommand.ExecuteAsync(null);

        vm.BudgetExceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Send_WhenServiceThrows_SetsErrorAndLeavesUserMessage()
    {
        var service = new FakeChatService { Throw = new InvalidOperationException("boom") };
        var vm = Vm(service);
        vm.InputText = "pytanie";

        await vm.SendCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.IsBusy.Should().BeFalse();
        vm.Messages.Should().ContainSingle("the user message stays even though the answer failed");
    }

    [Fact]
    public async Task UseSuggestion_SendsThePrompt()
    {
        var service = new FakeChatService(new ChatTurn("odpowiedź", []));
        var vm = Vm(service);

        await vm.UseSuggestionCommand.ExecuteAsync(vm.SuggestedPrompts[0]);

        service.Calls.Should().ContainSingle();
        service.Calls[0].Question.Should().Be(vm.SuggestedPrompts[0]);
        vm.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void Empty_WhenNoMessages_IsEmptyTrue()
    {
        var vm = Vm(new FakeChatService(new ChatTurn("x", [])));

        vm.IsEmpty.Should().BeTrue();
        vm.Messages.Should().BeEmpty();
    }

    private static ChatViewModel Vm(FakeChatService service) =>
        new(service, new FakeLocalizer(), NullLogger<ChatViewModel>.Instance);

    private sealed class FakeChatService : IChatService
    {
        private readonly Queue<ChatTurn> _turns = new();

        public FakeChatService(params ChatTurn[] turns)
        {
            foreach (var turn in turns)
            {
                _turns.Enqueue(turn);
            }
        }

        public Exception? Throw { get; init; }

        public List<(string Question, IReadOnlyList<ChatMessage> History)> Calls { get; } = [];

        public Task<ChatTurn> AskAsync(string question, IReadOnlyList<ChatMessage> history, CancellationToken ct)
        {
            Calls.Add((question, history));
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(_turns.Count > 0 ? _turns.Dequeue() : new ChatTurn("", []));
        }
    }
}
