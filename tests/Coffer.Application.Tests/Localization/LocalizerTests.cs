using System.ComponentModel;
using Coffer.Application.Localization;
using Coffer.Core.Localization;
using FluentAssertions;

namespace Coffer.Application.Tests.Localization;

public class LocalizerTests
{
    [Fact]
    public void Indexer_ReturnsLanguageSpecificString()
    {
        var localizer = new Localizer();

        localizer.SetLanguage(AppLanguage.Polish);
        var polish = localizer["Nav.Dashboard"];

        localizer.SetLanguage(AppLanguage.English);
        var english = localizer["Nav.Dashboard"];

        polish.Should().NotBeNullOrEmpty();
        english.Should().NotBeNullOrEmpty();
        polish.Should().NotBe(english);
    }

    [Fact]
    public void Indexer_ReturnsKey_WhenMissing()
    {
        var localizer = new Localizer();

        localizer["This.Key.Does.Not.Exist"].Should().Be("This.Key.Does.Not.Exist");
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged_AndIndexerNotification()
    {
        var localizer = new Localizer();
        localizer.SetLanguage(AppLanguage.Polish);

        var languageChanged = false;
        localizer.LanguageChanged += (_, _) => languageChanged = true;
        var indexerNotified = false;
        ((INotifyPropertyChanged)localizer).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                indexerNotified = true;
            }
        };

        localizer.SetLanguage(AppLanguage.English);

        localizer.Current.Should().Be(AppLanguage.English);
        languageChanged.Should().BeTrue();
        indexerNotified.Should().BeTrue();
    }

    [Fact]
    public void SetLanguage_NoOp_WhenAlreadyActive()
    {
        var localizer = new Localizer();
        localizer.SetLanguage(AppLanguage.English);

        var raised = false;
        localizer.LanguageChanged += (_, _) => raised = true;

        localizer.SetLanguage(AppLanguage.English);

        raised.Should().BeFalse();
    }

    [Fact]
    public void Format_AppliesArguments()
    {
        var localizer = new Localizer();
        localizer.SetLanguage(AppLanguage.English);

        localizer.Format("Nav.Version", "1.2.3").Should().Contain("1.2.3");
    }
}
