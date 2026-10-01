using CMS.ContentSynchronization;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.Http;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncTargetSecretValidatorTests
{
    private static ContentSyncTargetSecretValidator CreateValidator(bool enabled, string? configuredSecret)
    {
        // The target role and secret are Xperience's Content Sync target settings.
        var contentSync = new ContentSynchronizationOptions();
        contentSync.Target.Enabled = enabled;
        contentSync.Target.Secret = configuredSecret!;

        return new ContentSyncTargetSecretValidator(
            new ContentSyncToolkitSettings(Options.Create(new ContentSyncToolkitOptions()), Options.Create(contentSync)));
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenTargetDisabled_RegardlessOfSecretMatch()
    {
        var validator = CreateValidator(enabled: false, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid("correct-secret"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenConfiguredSecretIsNull()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: null);

        Assert.That(validator.IsValid("anything"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenConfiguredSecretIsEmpty()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "");

        Assert.That(validator.IsValid("anything"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenProvidedSecretIsNull()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid(null), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenProvidedSecretIsEmpty()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid(""), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenProvidedSecretIsWrong()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid("wrong-secret"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenProvidedSecretHasDifferentLength()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid("correct-secret-but-longer"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsFalse_WhenProvidedSecretDiffersOnlyByCase()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid("CORRECT-SECRET"), Is.False);
    }

    [Test]
    public void IsValid_ReturnsTrue_WhenEnabledAndSecretMatchesExactly()
    {
        var validator = CreateValidator(enabled: true, configuredSecret: "correct-secret");

        Assert.That(validator.IsValid("correct-secret"), Is.True);
    }
}
