using System.Text.Json;
using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class PromptJsonTests
{
    [Fact]
    public void Redact_EscapedNonAsciiAndPlus_AreDecodedBeforeRedaction()
    {
        // What the default (Web) serializer emits for "Łukasz Źdźbło" and "+48 500 600 700".
        const string json = """{"note":"Łukasz Źdźbło","phone":"+48 500 600 700"}""";

        var result = PromptJson.Redact(json, new PromptRedactor(["Łukasz Źdźbło"], []));

        result.Should().Be("""{"note":"[OWNER]","phone":"[PHONE]"}""");
    }

    [Fact]
    public void Redact_NameLearntFromADescription_IsRemovedFromTheSiblingMerchant()
    {
        // The merchant comes first in the document: the learning pass still catches it.
        const string json =
            """{"transactions":[{"merchant":"JAN TESTOWY","description":"Nazwa odbiorcy: JAN TESTOWY Tytuł: Zwrot"}]}""";

        var result = PromptJson.Redact(json, new PromptRedactor([], []));

        result.Should().Be(
            """{"transactions":[{"merchant":"[PARTY_1]","description":"Nazwa odbiorcy: [PARTY_1] Tytuł: Zwrot"}]}""");
    }

    [Fact]
    public void Redact_KeepsPropertyNamesNumbersAndPolishLetters()
    {
        const string json = """{"Nazwa odbiorcy: X":1.50,"category":"Spożywcze","items":[true,null,2]}""";

        var result = PromptJson.Redact(json, new PromptRedactor([], []));

        result.Should().Be(json);
    }

    [Fact]
    public void Redact_NotJson_RedactsAsPlainText()
    {
        PromptJson.Redact("tel. +48 500 600 700", new PromptRedactor([], [])).Should().Be("tel. [PHONE]");
    }

    [Fact]
    public void Restore_NoPseudonyms_ReturnsTheExactInput()
    {
        const string json = """{ "from" : "2026-06-01" }""";

        PromptJson.Restore(json, new PromptRedactor([], [])).Should().BeSameAs(json);
    }

    [Fact]
    public void Restore_NameWithQuote_StaysValidJson()
    {
        var redactor = new PromptRedactor([], ["SKLEP \"POD LIPĄ\""]);
        redactor.Redact("SKLEP \"POD LIPĄ\"");

        var restored = PromptJson.Restore("""{"merchantPattern":"[PARTY_1]"}""", redactor);

        JsonDocument.Parse(restored).RootElement.GetProperty("merchantPattern").GetString()
            .Should().Be("SKLEP \"POD LIPĄ\"");
    }
}
