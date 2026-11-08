using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>
/// The redaction rules (<see cref="PromptRedactor"/>) and the session factory
/// (<see cref="PromptAnonymizer"/>). All names, addresses, phones and identifiers are synthetic.
/// </summary>
public class PromptAnonymizerTests
{
    private static PromptRedactor Redactor(string[]? owners = null, string[]? counterparties = null) =>
        new(owners ?? [], counterparties ?? []);

    [Fact]
    public void Redact_RedactsPolishIban()
    {
        var result = Redactor().Redact("Przelew na PL61 1090 1014 0000 0712 1981 2874 od Jana");

        result.Should().Contain("[IBAN]").And.NotContain("1090 1014");
    }

    [Fact]
    public void Redact_RedactsBareAccountNumber()
    {
        var result = Redactor().Redact("Konto 82 1020 5604 0000 0102 8996 3017 zasilone");

        result.Should().Contain("[ACCOUNT]").And.NotContain("1020 5604");
    }

    [Fact]
    public void Redact_RedactsNip()
    {
        var result = Redactor().Redact("Faktura NIP 123-456-78-90 za usługę");

        result.Should().Contain("[NIP]").And.NotContain("123-456-78-90");
    }

    [Theory]
    [InlineData("Składka 44051401458 za styczeń")]
    [InlineData("Tytuł: /TI/P44051401458/OKR/26M01/SFP/PIT37/")]
    public void Redact_RedactsPesel(string text)
    {
        var result = Redactor().Redact(text);

        result.Should().Contain("[PESEL]").And.NotContain("44051401458");
    }

    [Theory]
    [InlineData("Numer telefonu: +48 500 600 700", "500 600 700")]
    [InlineData("Przelew na telefon +48500600700", "500600700")]
    [InlineData("Przelew na telefon 0048-500-600-700", "500-600-700")]
    [InlineData("BLIK OD: 48500600700 DO: 48501234567", "500600700")]
    [InlineData("zwrot na nr 500 600 700 dzięki", "500 600 700")]
    [InlineData("zwrot na nr 500-600-700", "500-600-700")]
    public void Redact_RedactsPolishPhoneNumbers(string text, string digits)
    {
        var result = Redactor().Redact(text);

        result.Should().Contain("[PHONE]").And.NotContain(digits);
    }

    [Theory]
    [InlineData("BIEDRONKA 1234 TORUN — zakupy spożywcze")]
    [InlineData("ZABKA Z1234 K.1 TORUN")]
    [InlineData("2026-01-05 -1200.00 PLN")]
    [InlineData("Saldo 1 234,56 zł po operacji")]
    [InlineData("Kwota 12345678901.50 PLN")]
    [InlineData("WYNAGRODZENIE 01/2026 PIT37")]
    [InlineData("Lokalizacja: Adres: BIEDRONKA 1234 Miasto: TORUN Kraj: POLSKA")]
    [InlineData("Tytuł: Zakupy spożywcze, napoje i przekąski")]
    public void Redact_KeepsMerchantsAmountsAndDates(string text)
    {
        Redactor().Redact(text).Should().Be(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Redact_NullOrEmpty_ReturnsInput(string? text)
    {
        Redactor().Redact(text!).Should().Be(text);
    }

    [Fact]
    public void Redact_LabelledTransfer_ReplacesNameAndAddressKeepsTitle()
    {
        const string text =
            "PRZELEW WYCHODZACY NAZWA ODBIORCY: JAN TESTOWY ADRES ODBIORCY: UL. FIKCYJNA 12/3 00-001 TESTOWO "
            + "TYTUŁ: ZWROT ZA BILETY";

        var result = Redactor().Redact(text);

        result.Should().Be(
            "PRZELEW WYCHODZACY NAZWA ODBIORCY: [PARTY_1] ADRES ODBIORCY: [ADDRESS] TYTUŁ: ZWROT ZA BILETY");
    }

    [Fact]
    public void Redact_CombinedPkoLabels_AreRecognised()
    {
        var result = Redactor().Redact(
            "Nazwa nadawcy/odbiorcy: ANNA PRZYKLADOWA Adres nadawcy/odbiorcy: UL TESTOWA 1 TORUN Tytul: Czynsz");

        result.Should().Be("Nazwa nadawcy/odbiorcy: [PARTY_1] Adres nadawcy/odbiorcy: [ADDRESS] Tytul: Czynsz");
    }

    [Fact]
    public void Redact_IdentifierCardPhoneAndAccountFields_AreReplaced_PublicOfficeNameIsKept()
    {
        var result = Redactor().Redact(
            "Rachunek odbiorcy: 11 1111 1111 1111 1111 1111 1111 Nazwa odbiorcy: URZAD SKARBOWY W TESTOWIE "
            + "Nazwa i nr identyfikatora: P 44051401458 Numer karty: 425125******1234 Numer telefonu: 500600700");

        result.Should().Be(
            "Rachunek odbiorcy: [ACCOUNT] Nazwa odbiorcy: URZAD SKARBOWY W TESTOWIE Nazwa i nr identyfikatora: [ID] "
            + "Numer karty: [CARD] Numer telefonu: [PHONE]");
    }

    [Fact]
    public void Redact_LabelledName_IsAlsoRemovedWhereItRepeatsUnlabelled()
    {
        var result = Redactor().Redact("Nazwa odbiorcy: Jan Testowy Tytuł: Prezent dla JAN TESTOWY");

        result.Should().Be("Nazwa odbiorcy: [PARTY_1] Tytuł: Prezent dla [PARTY_1]");
    }

    [Fact]
    public void RedactDocument_RawCsvRow_KeepsQuotesAndDelimiters()
    {
        const string row =
            "\"2026-01-09\",\"-1200.00\",\"Nazwa odbiorcy: JAN TESTOWY\",\"Adres odbiorcy: UL. FIKCYJNA 1\",\"Tytuł: Zwrot\",\"\"";

        var result = Redactor().RedactDocument(row);

        result.Should().Be(
            "\"2026-01-09\",\"-1200.00\",\"Nazwa odbiorcy: [PARTY_1]\",\"Adres odbiorcy: [ADDRESS]\",\"Tytuł: Zwrot\",\"\"");
    }

    [Fact]
    public void Redact_ParsedDescription_ValueRunsPastQuotesAndSemicolons()
    {
        // The CSV parser has already unescaped "" to ", so a sole trader's name (which legally
        // contains the person's name) and an address may hold a bare quote or a semicolon.
        var result = Redactor().Redact(
            "Nazwa odbiorcy: P.H.U. \"ANEX\" ANNA FIKCYJNA Adres odbiorcy: UL. POLNA 1; 87-100 TORUN Tytuł: FV 12/2026");

        result.Should().Be("Nazwa odbiorcy: [PARTY_1] Adres odbiorcy: [ADDRESS] Tytuł: FV 12/2026");
    }

    [Fact]
    public void RedactDocument_QuotedCsvField_HonoursEscapedQuotesAndInnerSemicolons()
    {
        const string row =
            "\"2026-01-09\";\"-1200,00\";\"Nazwa odbiorcy: P.H.U. \"\"ANEX\"\" ANNA FIKCYJNA\";"
            + "\"Adres odbiorcy: UL. POLNA 1; 87-100 TORUN\";\"Tytuł: FV 12/2026\"";

        var redactor = Redactor();
        var result = redactor.RedactDocument(row);

        result.Should().Be(
            "\"2026-01-09\";\"-1200,00\";\"Nazwa odbiorcy: [PARTY_1]\";\"Adres odbiorcy: [ADDRESS]\";\"Tytuł: FV 12/2026\"");
        redactor.Restore("[PARTY_1]").Should().Be("P.H.U. \"ANEX\" ANNA FIKCYJNA");
    }

    [Fact]
    public void RedactDocument_UnquotedSemicolonCsv_EndsValuesAtTheDelimiter()
    {
        var result = Redactor().RedactDocument("2026-01-09;-1200,00;Odbiorca: JAN TESTOWY;Tytuł: Zwrot\n");

        result.Should().Be("2026-01-09;-1200,00;Odbiorca: [PARTY_1];Tytuł: Zwrot\n");
    }

    [Fact]
    public void Redact_QuotedBusinessName_IsLearntWholeNotAsAFragment()
    {
        var redactor = Redactor();
        redactor.Redact("Nazwa odbiorcy: FIRMA \"ANEX\" ANNA FIKCYJNA Tytuł: Zwrot");

        redactor.Redact("FIRMA HANDLOWA i JAN").Should().Be("FIRMA HANDLOWA i JAN");
        redactor.Redact("dla FIRMA \"ANEX\" ANNA FIKCYJNA").Should().Be("dla [PARTY_1]");
    }

    [Theory]
    [InlineData("Nazwa odbiorcy: ANNA FIKCYJNA Tytuł: zwrot dla FIKCYJNA ANNA")]
    [InlineData("Nazwa odbiorcy: Anna Fikcyjna Tytuł: zwrot dla FIKCYJNA  ANNA")]
    public void Redact_LabelledName_IsAlsoRemovedInReversedWordOrder(string text)
    {
        var redactor = Redactor();

        var result = redactor.Redact(text);

        result.Should().Be("Nazwa odbiorcy: [PARTY_1] Tytuł: zwrot dla [PARTY_1]");
    }

    [Fact]
    public void Redact_KnownCounterpartyInReversedOrder_SharesThePseudonym()
    {
        var redactor = Redactor(counterparties: ["Jan Maria Testowy"]);

        var result = redactor.Redact("od TESTOWY JAN MARIA i od Jan Maria Testowy");

        result.Should().Be("od [PARTY_1] i od [PARTY_1]");
        redactor.Restore("[PARTY_1]").Should().Be("Jan Maria Testowy");
    }

    [Theory]
    [InlineData("Tytuł: CZYNSZ UL. KWIATOWA 5/3 87-100 TORUN", "Tytuł: CZYNSZ [ADDRESS] TORUN")]
    [InlineData("oplata za lokal al. Jana Pawla II 12 m. 4", "oplata za lokal [ADDRESS]")]
    [InlineData("Zwrot, os. Testowe 7, 00-950 i reszta", "Zwrot, [ADDRESS] i reszta")]
    [InlineData("Czynsz ul. 3 Maja 15/2 luty", "Czynsz [ADDRESS] luty")]
    [InlineData("Wysyłka na kod 87-100", "Wysyłka na kod [ADDRESS]")]
    [InlineData("Tytul: oplata Plac Testowy 1A lok. 2", "Tytul: oplata [ADDRESS]")]
    public void Redact_StreetAddressesAndPostalCodesInFreeText_AreRedacted(string text, string expected)
    {
        Redactor().Redact(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("Lokalizacja: Adres: UL. MARSZALKOWSKA 10 Miasto: WARSZAWA Kraj: POLSKA")]
    [InlineData("Tytuł: FV 12/2026 z dnia 2026-01-15, kwota 1 234,56 zł")]
    [InlineData("Ulica Testowa bez numeru")]
    [InlineData("PLATNOSC PL 12 SZT")]
    public void Redact_MerchantLocationsDatesAndAmounts_AreNotTakenForAddresses(string text)
    {
        Redactor().Redact(text).Should().Be(text);
    }

    [Theory]
    [InlineData("Odbiorca: JAN TESTOWY Tytuł: Zwrot", "Odbiorca: [PARTY_1] Tytuł: Zwrot")]
    [InlineData("Nadawca: JAN TESTOWY ul. Polna 1", "Nadawca: [PARTY_1] [ADDRESS]")]
    [InlineData("Kontrahent: MARIA TESTOWA", "Kontrahent: [PARTY_1]")]
    [InlineData("Dane odbiorcy: PIOTR TESTOWY UL. POLNA 1 00-001 TESTOWO", "Dane odbiorcy: [PARTY_1] [ADDRESS]")]
    [InlineData("Nazwa i adres nadawcy: ANNA TESTOWA, UL. POLNA 1", "Nazwa i adres nadawcy: [PARTY_1] [ADDRESS]")]
    [InlineData("Odbiorca: JAN TESTOWY Adres: UL. POLNA 1 TESTOWO", "Odbiorca: [PARTY_1] Adres: [ADDRESS]")]
    [InlineData("Zleceniodawca: JAN TESTOWY", "Zleceniodawca: [PARTY_1]")]
    [InlineData("Beneficjent: JAN TESTOWY", "Beneficjent: [PARTY_1]")]
    public void Redact_OtherBanksCounterpartyLabels_AreRecognised(string text, string expected)
    {
        Redactor().Redact(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("Nazwa odbiorcy: ZUS Tytuł: Składka")]
    [InlineData("Nazwa nadawcy/odbiorcy: KAWIARNIA CENTRUM Tytul: Kawa")]
    [InlineData("Nazwa odbiorcy: TESTOWA TELEKOMUNIKACJA S.A. Tytuł: FV 01/2026")]
    [InlineData("Nazwa nadawcy: PRZYKLADOWA SPÓŁKA Z O.O. Tytuł: Wynagrodzenie 01/2026")]
    public void Redact_LegalEntityCounterparty_IsKept(string text)
    {
        Redactor().Redact(text).Should().Be(text);
    }

    [Fact]
    public void Redact_LegalEntityWithAddress_KeepsTheNameRedactsTheAddress()
    {
        Redactor().Redact("Odbiorca: TESTOWA SP. Z O.O. ul. Polna 1")
            .Should().Be("Odbiorca: TESTOWA SP. Z O.O. [ADDRESS]");
    }

    [Fact]
    public void Redact_KnownLegalEntity_IsNotPseudonymisedElsewhere()
    {
        var redactor = Redactor(counterparties: ["ALLEGRO", "URZAD SKARBOWY W TESTOWIE"]);

        redactor.Redact("Zakupy ALLEGRO, adres allegro.pl, URZAD SKARBOWY W TESTOWIE")
            .Should().Be("Zakupy ALLEGRO, adres allegro.pl, URZAD SKARBOWY W TESTOWIE");
    }

    [Fact]
    public void Redact_GuidWithAnElevenDigitRun_IsLeftIntact()
    {
        var result = Redactor().Redact("- goal 3f2a9c1e-7b4d-4e2a-9c3b-a12345678901: PESEL 44051401458");

        result.Should().Be("- goal 3f2a9c1e-7b4d-4e2a-9c3b-a12345678901: PESEL [PESEL]");
    }

    [Theory]
    [InlineData("Przelew od LUKASZ ZDZBLO")]
    [InlineData("Przelew od łukasz źdźbło")]
    [InlineData("Przelew od ZDZBLO  LUKASZ")]
    [InlineData("Przelew od Łukasz Źdźbło")]
    public void Redact_OwnerName_IsCaseAndDiacriticInsensitiveInEitherOrder(string text)
    {
        var result = Redactor(owners: ["Łukasz Źdźbło"]).Redact(text);

        result.Should().Be("Przelew od [OWNER]");
    }

    [Fact]
    public void Redact_OwnerName_DoesNotClipLongerWords()
    {
        var result = Redactor(owners: ["Testowy"]).Redact("TESTOWYCH USŁUG i Testowy");

        result.Should().Be("TESTOWYCH USŁUG i [OWNER]");
    }

    [Fact]
    public void Redact_LabelledNameThatIsTheOwner_BecomesOwnerPlaceholder()
    {
        var result = Redactor(owners: ["Łukasz Źdźbło"]).Redact("Nazwa nadawcy: LUKASZ ZDZBLO Tytuł: Przelew własny");

        result.Should().Be("Nazwa nadawcy: [OWNER] Tytuł: Przelew własny");
    }

    [Fact]
    public void Redact_KnownCounterparty_IsPseudonymisedWithoutItsLabel()
    {
        var redactor = Redactor(counterparties: ["JAN TESTOWY"]);

        var result = redactor.Redact("Nowy sprzedawca: Jan Testowy, merchant=JAN TESTOWY");

        result.Should().Be("Nowy sprzedawca: [PARTY_1], merchant=[PARTY_1]");
    }

    [Fact]
    public void Redact_DifferentCounterparties_GetDifferentStablePseudonyms()
    {
        var redactor = Redactor(counterparties: ["JAN TESTOWY", "ANNA PRZYKLADOWA"]);

        var first = redactor.Redact("ANNA PRZYKLADOWA");
        var second = redactor.Redact("JAN TESTOWY i ANNA PRZYKLADOWA");

        first.Should().Be("[PARTY_1]");
        second.Should().Be("[PARTY_2] i [PARTY_1]");
    }

    [Fact]
    public void Redact_CounterpartyNamedLikeAPlaceholder_DoesNotCorruptPlaceholders()
    {
        var redactor = Redactor(counterparties: ["PARTY", "ADDRESS"]);

        var result = redactor.Redact("Nazwa odbiorcy: JAN TESTOWY Adres odbiorcy: UL. X 1");

        result.Should().Be("Nazwa odbiorcy: [PARTY_1] Adres odbiorcy: [ADDRESS]");
    }

    [Fact]
    public void Redact_VeryShortCounterparty_IsIgnored()
    {
        Redactor(counterparties: ["PL"]).Redact("PLATNOSC PL").Should().Be("PLATNOSC PL");
    }

    [Fact]
    public void Restore_MapsPseudonymsBackToTheOriginalNames()
    {
        var redactor = Redactor(counterparties: ["Jan Testowy"]);
        redactor.Redact("Przelew do JAN TESTOWY");

        var restored = redactor.Restore("Największy przelew trafił do [PARTY_1]; [PARTY_9] i [ADDRESS] zostają.");

        restored.Should().Be("Największy przelew trafił do Jan Testowy; [PARTY_9] i [ADDRESS] zostają.");
    }

    [Fact]
    public void HasOwnerNames_ReflectsConfiguration()
    {
        Redactor().HasOwnerNames.Should().BeFalse();
        Redactor(owners: ["  "]).HasOwnerNames.Should().BeFalse();
        Redactor(owners: ["Jan Testowy"]).HasOwnerNames.Should().BeTrue();
    }

    [Fact]
    public async Task BeginSession_LoadsOwnerNamesFromSettings()
    {
        var anonymizer = new PromptAnonymizer(
            new StubAiSettings { OwnerNames = "Łukasz Źdźbło,\nZDZBLO L." },
            new StaticCounterpartyNameSource());

        var session = await anonymizer.BeginSessionAsync(CancellationToken.None);

        session.HasOwnerNames.Should().BeTrue();
        session.Redact("od LUKASZ ZDZBLO oraz ZDZBLO L.").Should().Be("od [OWNER] oraz [OWNER]");
    }

    [Fact]
    public async Task BeginSession_NoOwnerNames_ReportsItAndStillRedactsPatterns()
    {
        var anonymizer = new PromptAnonymizer(new StubAiSettings(), new StaticCounterpartyNameSource());

        var session = await anonymizer.BeginSessionAsync(CancellationToken.None);

        session.HasOwnerNames.Should().BeFalse();
        session.Redact("tel. +48 500 600 700").Should().Be("tel. [PHONE]");
    }

    [Fact]
    public async Task BeginSession_LoadsKnownCounterparties()
    {
        var anonymizer = new PromptAnonymizer(new StubAiSettings(), new StaticCounterpartyNameSource("JAN TESTOWY"));

        var session = await anonymizer.BeginSessionAsync(CancellationToken.None);

        session.Redact("Czynsz JAN TESTOWY").Should().Be("Czynsz [PARTY_1]");
    }

    [Fact]
    public void ParseOwnerNames_SplitsOnSeparatorsAndDeduplicates()
    {
        PromptAnonymizer.ParseOwnerNames(" Jan Testowy ;JAN TESTOWY\r\nTestowy J.,, ")
            .Should().Equal("Jan Testowy", "Testowy J.");
    }
}
