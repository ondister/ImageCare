using System.Reflection;

using ImageCare.Core.Domain.Configuration;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

namespace ImageCare.UI.Avalonia.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class AdaptiveHelpCatalogTests
{
    private string _originalCulture = null!;
    private AdaptiveHelpCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _originalCulture = LocalizationManager.Instance.Culture.Name;
        LocalizationManager.Instance.SetCulture("en");
        _catalog = new AdaptiveHelpCatalog(LocalizationManager.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        LocalizationManager.Instance.SetCulture(_originalCulture);
    }

    [Test]
    public void Hints_ContainEveryPolicyHint_WithLocalizedContent()
    {
        var policyHintIds = typeof(AdaptiveHelpPolicy)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral
                && field.FieldType == typeof(string)
                && field.Name.EndsWith("HintId", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.That(_catalog.Hints.Select(hint => hint.Id), Is.EquivalentTo(policyHintIds));
        Assert.Multiple(() =>
        {
            foreach (var hint in _catalog.Hints)
            {
                Assert.That(_catalog.GetTitle(hint), Is.Not.EqualTo(hint.TitleKey), hint.Id);
                Assert.That(_catalog.GetMessage(hint, new Configuration()), Is.Not.EqualTo(hint.MessageKey), hint.Id);
                if (!string.IsNullOrEmpty(hint.ActionKey))
                {
                    Assert.That(_catalog.GetActionText(hint), Is.Not.EqualTo(hint.ActionKey), hint.Id);
                }
            }
        });
    }

    [Test]
    public void GetMessage_FormatsWorkspaceAndTransferSettings()
    {
        var configuration = new Configuration
        {
            LastSourceDirectoryPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), "photos", "Holiday"),
            CopyOnDragDropByDefault = true,
            DragDropModifier = DragDropModifierKey.Alt
        };
        _catalog.TryGetHint(AdaptiveHelpPolicy.ReturningHintId, out var returningHint);
        _catalog.TryGetHint(AdaptiveHelpPolicy.RestoreFoldersHintId, out var restoreHint);
        _catalog.TryGetHint(AdaptiveHelpPolicy.TransferHintId, out var transferHint);

        Assert.Multiple(() =>
        {
            Assert.That(_catalog.GetMessage(returningHint, configuration), Does.Contain("Holiday"));
            Assert.That(_catalog.GetMessage(restoreHint, configuration), Does.Contain("Holiday"));
            Assert.That(_catalog.GetMessage(transferHint, configuration), Does.Contain("Copy"));
            Assert.That(_catalog.GetMessage(transferHint, configuration), Does.Contain("Alt"));
        });
    }

    [Test]
    public void GetReason_FormatsDecisionSpecificValues()
    {
        var context = new AdaptiveHelpContext(
            false,
            true,
            true,
            4,
            3,
            false,
            false,
            false,
            true,
            5);

        Assert.Multiple(() =>
        {
            Assert.That(
                _catalog.GetReason(
                    new AdaptiveHelpDecision("hint", 0, "Help_ReasonReturning"),
                    context,
                    TimeSpan.FromDays(15)),
                Does.Contain("15"));
            Assert.That(
                _catalog.GetReason(
                    new AdaptiveHelpDecision("hint", 0, "Help_ReasonRepeatedErrors"),
                    context,
                    TimeSpan.Zero),
                Does.Contain("3"));
            Assert.That(
                _catalog.GetReason(
                    new AdaptiveHelpDecision("hint", 0, "Help_ReasonSelectionReady"),
                    context,
                    TimeSpan.Zero),
                Does.Contain("4"));
            Assert.That(
                _catalog.GetReason(
                    new AdaptiveHelpDecision("hint", 0, "Help_ReasonOverviewSelection"),
                    context,
                    TimeSpan.Zero),
                Does.Contain("5"));
        });
    }
}
