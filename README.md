# AutoBot - Bitcoin Futures Trading Bot

An automated Bitcoin futures trading bot for the LN Markets platform, built with ASP.NET Core 8.0.

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## 🚀 Features

- **Automated Bitcoin futures trading** on LN Markets
- **Real-time price monitoring** via WebSocket connections
- **Algorithmic trading strategies** with configurable parameters
- **Intelligent risk management** with margin management
- **Automatic position sizing** based on losses
- **Rate limiting** and protection against excessive trading
- **Emergency pause functionality** for immediate trading halt

## ⚠️ Important Security Notice

**WARNING:** This bot trades with real money on cryptocurrency markets.
- Only use money you can afford to lose
- Test all settings thoroughly with small amounts
- The bot runs continuously - monitor it regularly
- Keep your API keys secure

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- LN Markets account with API access
- Basic knowledge of C# and trading

## 🛠️ Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/autosatoshi/bot.git
   cd bot
   ```

2. **Build the project**
   ```bash
   dotnet build AutoBot.sln
   ```

3. **Configure settings** (see Configuration below)

4. **Start the bot**
   ```bash
   dotnet run --project src/Backend/AutoBot.csproj
   ```

## ⚙️ Configuration

Edit `src/Backend/appsettings.json`:

```json
{
  "ln": {
    "key": "YOUR_LN_MARKETS_API_KEY",
    "passphrase": "YOUR_PASSPHRASE", 
    "secret": "YOUR_SECRET",
    "pause": true,
    "quantity": 1,
    "leverage": 1,
    "takeprofit": 100,
    "maxTakeprofitPrice": 110000,
    "maxRunningTrades": 10,
    "factor": 1000,
    "addMarginInUsd": 1,
    "maxLossInPercent": -50
  }
}
```

### Configuration Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `key` | LN Markets API Key | `""` |
| `passphrase` | API Passphrase | `""` |
| `secret` | API Secret | `""` |
| `pause` | Emergency stop (true = no trading) | `true` |
| `quantity` | Trade size multiplier | `1` |
| `leverage` | Leverage for positions | `1` |
| `takeprofit` | Profit target in USD | `100` |
| `maxTakeprofitPrice` | Max price for take-profit orders | `110000` |
| `maxRunningTrades` | Max concurrent positions | `10` |
| `factor` | Price interval factor (1000 = $1000 intervals) | `1000` |
| `addMarginInUsd` | Margin addition on losing positions | `1` |
| `maxLossInPercent` | Max loss before adding margin | `-50` |

## 🏗️ Architecture

```
AutoBot/
├── src/Backend/
│   ├── Program.cs                    # Application entry point
│   ├── Services/
│   │   ├── LnMarketsApiService.cs    # REST API client
│   │   └── LnMarketsBackgroundService.cs # Trading logic
│   ├── Models/LnMarkets/             # Data models
│   └── appsettings.json              # Configuration
```

### Trading Algorithm

1. **WebSocket connection** to LN Markets for real-time price data
2. **Price filtering** (discards data older than 5 seconds)
3. **Price rounding** to $50 increments for position tracking
4. **Rate limiting** (min. 10 seconds between actions)
5. **Margin management** for existing losing positions
6. **Order placement** at calculated price levels
7. **Order updates** through cancellation and replacement

## 🚦 Usage

1. **Configure API credentials**
   - Create LN Markets account
   - Generate API keys
   - Add to `appsettings.json`

2. **Adjust trading parameters**
   - Start with conservative values
   - Set `pause: false` to activate trading

3. **Start and monitor the bot**
   ```bash
   dotnet run --project src/Backend/AutoBot.csproj
   ```

4. **Monitor logs**
   - Console output for trading activities
   - Set `pause: true` immediately if issues occur

## 🔒 Security

- **Never commit API keys to Git**
- **Use environment variables** for production deployment
- **Configure firewall rules** for server deployment
- **Regular monitoring** of trading activities
- **Backup strategies** for configuration and logs

## 🧪 Development

```bash
# Development mode
dotnet run --project src/Backend/AutoBot.csproj --environment Development

# Run tests (if available)
dotnet test

# Format code
dotnet format
```

## 📊 Monitoring

The bot logs important events:
- WebSocket connection status
- Price updates and trading decisions
- API errors and reconnections
- Position updates and margin changes

## ⚖️ Disclaimer

This bot is provided "as is". Use at your own risk.
Trading cryptocurrencies involves significant risk and can result in total loss.

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Create Pull Request

---

**Built with ❤️ and .NET 8.0**
