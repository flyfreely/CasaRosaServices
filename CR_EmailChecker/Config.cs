using Microsoft.Extensions.Configuration;

namespace EmailChecker;

record AppConfig(
    string[]  HttpPrefixes,
    string    ApiToken,
    string    ImapScheme,
    string    ImapHost,
    int       ImapPort,
    bool      CheckCertificateRevocation,
    string    ImapUsername,
    string    ImapPassword,
    bool      UseOAuth2,
    int       ResetToDefaultMinutes,
    int       DefaultPollingIntervalMinutes,
    int       ImmediateRefreshTimeoutSeconds,
    int       LookbackMinutes,
    int       MaxMessagesToScan)
{
    public static AppConfig Load(IConfiguration cfg) => new(
        HttpPrefixes:                   cfg.GetSection("Http:Prefixes").Get<string[]>()!,
        ApiToken:                       cfg["Http:AuthToken"]!,
        ImapScheme:                     cfg["Imap:Scheme"]!,
        ImapHost:                       cfg["Imap:Host"]!,
        ImapPort:                       int.Parse(cfg["Imap:Port"]  ?? "993"),
        CheckCertificateRevocation:     bool.Parse(cfg["Imap:CheckCertificateRevocation"] ?? "false"),
        ImapUsername:                   cfg["Imap:Username"]!,
        ImapPassword:                   cfg["Imap:Password"]!,
        UseOAuth2:                      bool.Parse(cfg["Imap:UseOAuth2"] ?? "false"),
        ResetToDefaultMinutes:          int.Parse(cfg["Airbnb:ResetToDefaultMinutes"]          ?? "10"),
        DefaultPollingIntervalMinutes:  int.Parse(cfg["Polling:DefaultIntervalMinutes"]         ?? "60"),
        ImmediateRefreshTimeoutSeconds: int.Parse(cfg["Polling:ImmediateRefreshTimeoutSeconds"] ?? "30"),
        LookbackMinutes:                int.Parse(cfg["Polling:LookbackMinutes"]                ?? "10"),
        MaxMessagesToScan:              int.Parse(cfg["Polling:MaxMessagesToScan"]              ?? "5")
    );
}
