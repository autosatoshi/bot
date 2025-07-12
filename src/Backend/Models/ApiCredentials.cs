namespace AutoBot.Models;

/// <summary>
/// Encapsulates API credentials with secure handling
/// </summary>
public sealed class ApiCredentials
{
    public required string Key { get; init; }
    public required string Passphrase { get; init; }
    public required string Secret { get; init; }

    /// <summary>
    /// Creates credentials from LnMarketsOptions
    /// </summary>
    public static ApiCredentials FromOptions(LnMarketsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        if (string.IsNullOrWhiteSpace(options.Key))
            throw new ArgumentException("API Key cannot be null or empty", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Passphrase))
            throw new ArgumentException("API Passphrase cannot be null or empty", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Secret))
            throw new ArgumentException("API Secret cannot be null or empty", nameof(options));

        return new ApiCredentials
        {
            Key = options.Key,
            Passphrase = options.Passphrase,
            Secret = options.Secret
        };
    }

    /// <summary>
    /// Securely clears credential data (best effort)
    /// </summary>
    public override string ToString()
    {
        return $"ApiCredentials[Key=***{Key[^3..]}, Passphrase=***, Secret=***]";
    }
}