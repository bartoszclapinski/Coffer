using Coffer.Infrastructure.AI;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

/// <summary>Which counterparty names may be sent to the AI as they are. All names are invented.</summary>
public class CounterpartyNamesTests
{
    [Theory]
    [InlineData("ZUS")]
    [InlineData("URZAD SKARBOWY W TESTOWIE")]
    [InlineData("Urząd Miasta Testowo")]
    [InlineData("KAWIARNIA CENTRUM")]
    [InlineData("APTEKA POD TESTEM")]
    [InlineData("TESTOWA TELEKOMUNIKACJA S.A.")]
    [InlineData("PRZYKLADOWA SA")]
    [InlineData("PRZYKLADOWA SP. Z O.O.")]
    [InlineData("PRZYKLADOWA SP.Z O.O.")]
    [InlineData("Przykładowa Spółka z o.o.")]
    [InlineData("FIKCYJNY TESTOWY SP. Z O.O.")]
    [InlineData("TESTFLIX INTERNATIONAL B.V.")]
    [InlineData("TESTSTREAM AB")]
    [InlineData("ALLEGRO")]
    [InlineData("ZABKA Z1234")]
    [InlineData("SKLEP 24")]
    [InlineData("WWW.TESTOWY-SKLEP.PL")]
    [InlineData("SPOLDZIELNIA MIESZKANIOWA TESTOWO")]
    [InlineData("WSPOLNOTA MIESZKANIOWA UL. TESTOWA 1")]
    [InlineData("TESTOWA POLSKA")]
    public void IsLegalEntity_OrganisationNames_AreKept(string name)
    {
        CounterpartyNames.IsLegalEntity(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("JAN TESTOWY")]
    [InlineData("Łukasz Źdźbło")]
    [InlineData("TESTOWY JAN")]
    [InlineData("PRZYKLADOWA ANNA MARIA")]
    [InlineData("FIKCYJNY TESTOWY")]
    [InlineData("TESTOWY J.")]
    [InlineData("P.H.U. \"ANEX\" ANNA FIKCYJNA")]
    [InlineData("PHU ANEX FIKCYJNA")]
    [InlineData("F.H.U. TESTOWY")]
    [InlineData("FIRMA HANDLOWA TESTOWY")]
    [InlineData("APTEKA ANNA FIKCYJNA")]
    [InlineData("SKLEP 24 JAN TESTOWY")]
    [InlineData("FIKCYJNY I WSPÓLNICY SP.J.")]
    [InlineData("TESTOWY I FIKCYJNY S.C.")]
    [InlineData("TESTOWY SPÓŁKA KOMANDYTOWA")]
    [InlineData("ANNA")]
    [InlineData("[PARTY_1]")]
    [InlineData("[OWNER]")]
    [InlineData("")]
    [InlineData(null)]
    public void IsLegalEntity_PersonsAndPersonNamedBusinesses_AreNot(string? name)
    {
        CounterpartyNames.IsLegalEntity(name).Should().BeFalse();
    }
}
