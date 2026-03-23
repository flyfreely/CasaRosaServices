using EmailChecker;
using Microsoft.Extensions.Configuration;

var liveConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var config   = AppConfig.Load(liveConfig);
var imap     = new ImapService(config);
var registry = new SubscriberRegistry();
var webhook  = new WebhookSender(registry);
var poller   = new PollingService(config, liveConfig, imap, webhook);
var api      = new ApiServer(config, poller, imap, registry);

new Thread(api.Run)    { IsBackground = true }.Start();
new Thread(poller.Run) { IsBackground = true }.Start();

// Keep the main thread alive when running without an attached console (e.g. Docker).
Thread.Sleep(Timeout.Infinite);
