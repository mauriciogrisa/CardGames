using CardGames.Models;

namespace CardGames.Services;

public class GameLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public GameLogger(IConfiguration config)
    {
        // In production (Azure App Service) write to D:\home\LogFiles which persists
        // across restarts. Locally fall back to a "logs" folder beside the executable.
        // Override with the "GameLogPath" config key in appsettings or an env variable.
        var configured = config["GameLogPath"];
        string dir;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            dir = configured;
        }
        else
        {
            // On Azure App Service, WEBSITE_SITE_NAME is always set; use D:\home\LogFiles
            // (persists across restarts). Otherwise fall back to a local logs folder.
            var isAzure = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") != null;
            var home = Environment.GetEnvironmentVariable("HOME");
            dir = isAzure
                ? Path.Combine(home ?? @"D:\home", "LogFiles")
                : Path.Combine(AppContext.BaseDirectory, "logs");
        }
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "game_log.txt");
    }

    public void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logPath, entry + Environment.NewLine);
            }
        }
        catch { /* non-critical */ }
    }

    public void LogSeparator(string title)
    {
        Log(new string('─', 55));
        Log($"  {title}");
        Log(new string('─', 55));
    }

    // ── Per-session wrapper ───────────────────────────────────────────────
    public GameLogSession OpenSession()
    {
        var id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return new GameLogSession(this, id);
    }

    public class GameLogSession
    {
        private readonly GameLogger _logger;
        public string SessionId { get; }

        internal GameLogSession(GameLogger logger, string sessionId)
        {
            _logger = logger;
            SessionId = sessionId;
        }

        public void Log(string message) => _logger.Log($"[{SessionId}] {message}");
        public void LogSeparator(string title)
        {
            _logger.Log(new string('─', 55));
            _logger.Log($"[{SessionId}]   {title}");
            _logger.Log(new string('─', 55));
        }
    }

    // ── Card helpers ──────────────────────────────────────────────────────
    public static string C(Card card) =>
        card.IsJoker ? "JKR" : $"{Rank(card.Rank)}{Suit(card.Suit)}";

    public static string Hand(IEnumerable<Card> cards) =>
        string.Join("  ", cards.Select(C));

    public static string Combo(IEnumerable<Card> cards, CombinationType type) =>
        $"{Hand(cards)}  [{type}]";

    private static string Rank(CardGames.Models.Rank r) => r switch
    {
        Models.Rank.Ace   => "A",
        Models.Rank.Jack  => "J",
        Models.Rank.Queen => "Q",
        Models.Rank.King  => "K",
        Models.Rank.Joker => "JKR",
        _                 => ((int)r).ToString()
    };

    private static string Suit(CardGames.Models.Suit s) => s switch
    {
        Models.Suit.Hearts   => "♥",
        Models.Suit.Diamonds => "♦",
        Models.Suit.Clubs    => "♣",
        Models.Suit.Spades   => "♠",
        _                    => ""
    };
}
