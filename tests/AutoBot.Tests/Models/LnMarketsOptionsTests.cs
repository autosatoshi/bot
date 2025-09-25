using AutoBot.Models;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;

namespace AutoBot.Tests.Models;

public class LnMarketsOptionsTests
{
    [Fact]
    public void LnMarketsOptions_WithValidValues_ShouldPassValidation()
    {
        // Arrange
        var options = new LnMarketsOptions
        {
            Endpoint = "https://test.endpoint",
            Key = "valid-key",
            Passphrase = "valid-passphrase",
            Secret = "valid-secret",
            Pause = false,
            Quantity = 1,
            Leverage = 2,
            Takeprofit = 100,
            MaxTakeprofitPrice = 110000,
            MaxRunningTrades = 10,
            Factor = 1000,
            AddMarginInUsd = 1.5m,
            MaxLossInPercent = -25
        };

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "The Key field is required.")]
    [InlineData(null, "The Key field is required.")]
    public void Key_WithInvalidValues_ShouldFailValidation(string? key, string expectedMessage)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Key = key!;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(vr => vr.ErrorMessage == expectedMessage);
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("required") || r.ErrorMessage.Contains("MinLength"));
    }

    [Theory]
    [InlineData("", "The Passphrase field is required.")]
    [InlineData(null, "The Passphrase field is required.")]
    public void Passphrase_WithInvalidValues_ShouldFailValidation(string? passphrase, string expectedMessage)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Passphrase = passphrase!;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(vr => vr.ErrorMessage == expectedMessage);
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("required") || r.ErrorMessage.Contains("MinLength"));
    }

    [Theory]
    [InlineData("", "The Secret field is required.")]
    [InlineData(null, "The Secret field is required.")]
    public void Secret_WithInvalidValues_ShouldFailValidation(string? secret, string expectedMessage)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Secret = secret!;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(vr => vr.ErrorMessage == expectedMessage);
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("required") || r.ErrorMessage.Contains("MinLength"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_WithInvalidValues_ShouldFailValidation(int quantity)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Quantity = quantity;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Leverage_WithInvalidValues_ShouldFailValidation(int leverage)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Leverage = leverage;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Takeprofit_WithInvalidValues_ShouldFailValidation(int takeprofit)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Takeprofit = takeprofit;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxTakeprofitPrice_WithInvalidValues_ShouldFailValidation(int maxTakeprofitPrice)
    {
        // Arrange
        var options = CreateValidOptions();
        options.MaxTakeprofitPrice = maxTakeprofitPrice;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void MaxRunningTrades_WithInvalidValues_ShouldFailValidation(int maxRunningTrades)
    {
        // Arrange
        var options = CreateValidOptions();
        options.MaxRunningTrades = maxRunningTrades;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Factor_WithInvalidValues_ShouldFailValidation(int factor)
    {
        // Arrange
        var options = CreateValidOptions();
        options.Factor = factor;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(0.005)]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddMarginInUsd_WithInvalidValues_ShouldFailValidation(double addMarginInUsd)
    {
        // Arrange
        var options = CreateValidOptions();
        options.AddMarginInUsd = (decimal)addMarginInUsd;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(1)]
    public void MaxLossInPercent_WithInvalidValues_ShouldFailValidation(int maxLossInPercent)
    {
        // Arrange
        var options = CreateValidOptions();
        options.MaxLossInPercent = maxLossInPercent;

        // Act
        var validationResults = ValidateModel(options);

        // Assert
        validationResults.Should().NotBeEmpty();
        validationResults.Should().Contain(r => r.ErrorMessage!.Contains("range") || r.ErrorMessage!.Contains("between"));
    }

    [Fact]
    public void SectionName_ShouldBeCorrect()
    {
        // Assert
        LnMarketsOptions.SectionName.Should().Be("ln");
    }

    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new LnMarketsOptions();

        // Assert
        options.Key.Should().Be(string.Empty);
        options.Passphrase.Should().Be(string.Empty);
        options.Secret.Should().Be(string.Empty);
        options.Pause.Should().BeTrue(); // Safety first - default to paused
        options.Quantity.Should().Be(1);
        options.Leverage.Should().Be(1);
        options.Takeprofit.Should().Be(100);
        options.MaxTakeprofitPrice.Should().Be(110000);
        options.MaxRunningTrades.Should().Be(10);
        options.Factor.Should().Be(1000);
        options.AddMarginInUsd.Should().Be(1);
        options.MaxLossInPercent.Should().Be(-50);
    }

    private static LnMarketsOptions CreateValidOptions()
    {
        return new LnMarketsOptions
        {
            Endpoint = "https://test.endpoint",
            Key = "valid-key",
            Passphrase = "valid-passphrase",
            Secret = "valid-secret",
            Pause = false,
            Quantity = 1,
            Leverage = 2,
            Takeprofit = 100,
            MaxTakeprofitPrice = 110000,
            MaxRunningTrades = 10,
            Factor = 1000,
            AddMarginInUsd = 1.5m,
            MaxLossInPercent = -25
        };
    }

    private static List<ValidationResult> ValidateModel(LnMarketsOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, true);
        return results;
    }
}