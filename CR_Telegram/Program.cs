using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

// ── Telemetry logging ──────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .Enrich.WithProperty("Source", "cr_telegram")
    .WriteTo.Http(
        requestUri:    "http://192.168.48.1:5100/api/logs",
        queueLimitBytes: null,
        textFormatter: new CompactJsonFormatter(),
        httpClient:    new TelemetryHttpClient())
    .CreateLogger();

var cesarBotToken    = config["Telegram:CesarBotToken"]!;
var autoBotToken     = config["Telegram:AutoBotToken"]!;
var httpAuthToken    = config["Http:AuthToken"]!;
var httpPrefix       = config["Http:Prefix"] ?? "http://+:5100/";
var googleApiKey     = config["Google:ApiKey"]!;
var elevenLabsApiKey = config["ElevenLabs:ApiKey"]!;
var anthropicApiKey  = config["Anthropic:ApiKey"]!;
var openAiApiKey     = config["OpenAI:ApiKey"]!;
var dbConnectionString    = config["Database:ConnectionString"]!;
var reservationApiBaseUrl = config["ReservationApi:BaseUrl"] ?? "http://localhost:8103";
var reservationApiToken   = config["ReservationApi:Token"]!;
var adminApiBaseUrl       = config["AdminApi:BaseUrl"] ?? "http://localhost:8104";
var adminApiToken         = config["AdminApi:Token"]!;

// ── Bot identifiers ───────────────────────────────────────────────────────────
const string CesarBotId = "Cesar_bot";
const string AutoBotId  = "Auto_Bot";

// ── Telegram group IDs ────────────────────────────────────────────────────────
const long CasaRosaManagementGroupId = -5186091931;
const long CasaRosaEnglishGroupId    = -5129864639;
const long TranslatorGroupId         = -5209557963;
const long RapidResponseGroupId      = -5271439382;

// ── Bot clients ───────────────────────────────────────────────────────────────
var cesarBot = new TelegramBotClient(cesarBotToken);
var autoBot  = new TelegramBotClient(autoBotToken);
var bots     = new Dictionary<string, TelegramBotClient>(StringComparer.OrdinalIgnoreCase)
{
    [CesarBotId] = cesarBot,
    [AutoBotId]  = autoBot,
};

var httpClient = new HttpClient();
var me = await cesarBot.GetMe();

Console.WriteLine("Fetching ElevenLabs voices...");
var cesarVoiceId = await GetVoiceIdAsync("Cesar real voice");
var annaVoiceId  = await GetVoiceIdAsync("Anna real voice");
Console.WriteLine($"Cesar voice ID: {cesarVoiceId}");
Console.WriteLine($"Anna voice ID: {annaVoiceId}");

Console.WriteLine("Loading knowledge base embeddings...");
var kbEmbeddings = await LoadEmbeddingsAsync();
Console.WriteLine($"Loaded {kbEmbeddings.Count} KB embeddings.");

Console.WriteLine($"Bot started: @{me.Username}");

// ── Subscriber registry ───────────────────────────────────────────────────────
// groupId -> list of (Url, Token, consecutive failures)
var subscribers     = new Dictionary<long, List<(string Url, string Token, int Failures)>>();
var subscribersLock = new object();

// ── Start HTTP API ────────────────────────────────────────────────────────────
var listener = new HttpListener();
listener.Prefixes.Add(httpPrefix);
listener.Start();
Console.WriteLine($"HTTP API listening on {httpPrefix}");

_ = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        try
        {
            var ctx = await listener.GetContextAsync();
            _ = Task.Run(() => HandleHttpAsync(ctx));
        }
        catch { }
    }
});

// ── Telegram log helper ───────────────────────────────────────────────────────
async Task TelegramLog(string eventType, string messageType, string? channel = null, string? summary = null, bool isError = false)
{
    try
    {
        await httpClient.PostAsJsonAsync(
            $"{reservationApiBaseUrl}/admin/telegram-log",
            new { eventType, messageType, channel, summary, isError });
    }
    catch { }
}

DateTime NextFire(DateTime now, TimeOnly time)
{
    var candidate = now.Date.Add(time.ToTimeSpan());
    return now < candidate ? candidate : candidate.AddDays(1);
}

string ChannelName(long id) => id switch
{
    CasaRosaManagementGroupId => "Management",
    CasaRosaEnglishGroupId    => "English Group",
    _                         => id.ToString()
};

// ── Briefing scheduler ────────────────────────────────────────────────────────
_ = Task.Run(async () =>
{
    var portugalTz        = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var lastTomorrowFired = DateTime.MinValue;
    var lastTodayFired    = DateTime.MinValue;
    await Task.Delay(TimeSpan.FromMinutes(2)); // wait for all services to be ready
    var tomorrowTimeLog = await GetTomorrowBriefingTimeAsync();
    var todayTimeLog    = await GetTodayBriefingTimeAsync();
    var tripleTimeLog   = await GetTripleCleaningTimeAsync();
    var nowLog          = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz);
    Console.WriteLine($"[Scheduler] Ready. Tomorrow briefing: {tomorrowTimeLog:HH\\:mm}, Today briefing: {todayTimeLog:HH\\:mm} (Portugal time)");
    await TelegramLog("scheduled", "Tomorrow Briefing", summary: $"Next: {NextFire(nowLog, tomorrowTimeLog):ddd d MMM 'at' HH:mm} Portugal");
    await TelegramLog("scheduled", "Today Briefing",    summary: $"Next: {NextFire(nowLog, todayTimeLog):ddd d MMM 'at' HH:mm} Portugal");
    await TelegramLog("scheduled", "Triple Cleaning Alert", summary: $"Next: {NextFire(nowLog, tripleTimeLog):ddd d MMM 'at' HH:mm} Portugal");
    while (true)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz);

        var tomorrowTime   = await GetTomorrowBriefingTimeAsync();
        var tomorrowFireDt = now.Date.Add(tomorrowTime.ToTimeSpan());
        if (now.Hour == tomorrowTime.Hour && now.Minute == tomorrowTime.Minute && lastTomorrowFired < tomorrowFireDt)
        {
            lastTomorrowFired = tomorrowFireDt;
            Console.WriteLine($"[Scheduler] Firing tomorrow briefing at {now:HH\\:mm}");
            try
            {
                await SendTomorrowBriefingAsync();
                await TelegramLog("scheduled", "Tomorrow Briefing", summary: $"Next: {NextFire(now.AddMinutes(1), tomorrowTime):ddd d MMM 'at' HH:mm} Portugal");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tomorrow Briefing Error] {ex.Message}");
                await TelegramLog("error", "Tomorrow Briefing", summary: ex.Message, isError: true);
            }
        }

        var todayTime   = await GetTodayBriefingTimeAsync();
        var todayFireDt = now.Date.Add(todayTime.ToTimeSpan());
        if (now.Hour == todayTime.Hour && now.Minute == todayTime.Minute && lastTodayFired < todayFireDt)
        {
            lastTodayFired = todayFireDt;
            Console.WriteLine($"[Scheduler] Firing today briefing at {now:HH\\:mm}");
            try
            {
                await SendTodayBriefingAsync();
                await TelegramLog("scheduled", "Today Briefing", summary: $"Next: {NextFire(now.AddMinutes(1), todayTime):ddd d MMM 'at' HH:mm} Portugal");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Today Briefing Error] {ex.Message}");
                await TelegramLog("error", "Today Briefing", summary: ex.Message, isError: true);
            }
        }

        await Task.Delay(TimeSpan.FromMinutes(1));
    }
});

// ── Triple cleaning scheduler ─────────────────────────────────────────────────
_ = Task.Run(async () =>
{
    var portugalTz      = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var lastTripleFired = DateTime.MinValue;
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        try
        {
            var now        = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz);
            var tripleTime = await GetTripleCleaningTimeAsync();
            var tripleFireDt = now.Date.Add(tripleTime.ToTimeSpan());
            if (now.Hour == tripleTime.Hour && now.Minute == tripleTime.Minute && lastTripleFired < tripleFireDt)
            {
                lastTripleFired = tripleFireDt;
                try
                {
                    await SendTripleCleaningAlertAsync();
                    await TelegramLog("scheduled", "Triple Cleaning Alert", summary: $"Next: {NextFire(now.AddMinutes(1), tripleTime):ddd d MMM 'at' HH:mm} Portugal");
                }
                catch (Exception ex)
                {
                    await TelegramLog("error", "Triple Cleaning Alert", summary: ex.Message, isError: true);
                    throw;
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Triple Cleaning Scheduler] {ex.Message}"); }
    }
});

// ── Reminder scheduler ────────────────────────────────────────────────────────
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        try { await FireDueRemindersAsync(); }
        catch (Exception ex) { Console.WriteLine($"[Reminder Scheduler] {ex.Message}"); }
    }
});

// ── Telegram polling state ────────────────────────────────────────────────────
// Rolling conversation history for Management<->English groups (stored in English)
var conversationHistory = new List<string>();
const int MaxHistoryItems = 20;

// Pending language selections for Translator group: messageId -> (text, isVoice)
var pendingTranslations = new Dictionary<int, (string Text, bool IsVoice)>();

// Per-chat conversation history for RapidResponse group
var airbnbConversations = new Dictionary<long, List<(string Role, string Content)>>();

// Current reply language for RapidResponse group (default English)
var airbnbLanguage = "English";

Console.WriteLine("Listening for messages... Press Ctrl+C to stop.");

int offset = 0;

while (true)
{
    try
    {
        var updates = await cesarBot.GetUpdates(offset, limit: 100, timeout: 30);

        foreach (var update in updates)
        {
            offset = update.Id + 1;

            // Handle language selection button press
            if (update.CallbackQuery is { } callback)
            {
                await cesarBot.AnswerCallbackQuery(callback.Id);

                var parts = callback.Data?.Split(':');

                // Cancel reminder button
                if (parts?.Length == 2 && parts[0] == "cancel_reminder" && int.TryParse(parts[1], out var remId))
                {
                    await CancelReminderViaApiAsync(remId);
                    await cesarBot.EditMessageText(
                        callback.Message!.Chat.Id, callback.Message.MessageId,
                        callback.Message.Text + "\n\n❌ Reminder cancelled.");
                    continue;
                }

                if (parts?.Length == 2 && int.TryParse(parts[0], out var msgId) && pendingTranslations.TryGetValue(msgId, out var pending))
                {
                    var targetLang = parts[1] switch { "ru" => "Russian", "pt" => "Portuguese", "es" => "Spanish", "pl" => "Polish", "he" => "Hebrew", _ => "Russian" };

                    var translated = await TranslateWithClaudeAsync(pending.Text, "English", targetLang);
                    Console.WriteLine($"[{targetLang}]: {translated}");

                    if (pending.IsVoice)
                    {
                        var audioOut = await TextToSpeechAsync(translated, cesarVoiceId);
                        using var stream = new MemoryStream(audioOut);
                        await cesarBot.SendVoice(TranslatorGroupId, InputFile.FromStream(stream, "voice.mp3"), caption: translated);
                    }
                    else
                    {
                        await cesarBot.SendMessage(TranslatorGroupId, translated);
                    }

                    pendingTranslations.Remove(msgId);
                }
                continue;
            }

            if (update.Message is not { } message) continue;
            if (message.From?.Id == me.Id) continue;

            var chatId = message.Chat.Id;
            if (chatId != CasaRosaManagementGroupId && chatId != CasaRosaEnglishGroupId && chatId != TranslatorGroupId && chatId != RapidResponseGroupId) continue;

            var senderName = message.From?.FirstName ?? "Unknown";
            if (!string.IsNullOrEmpty(message.From?.LastName))
                senderName += $" {message.From.LastName}";

            // RapidResponse guest Q&A
            if (chatId == RapidResponseGroupId)
            {
                string? airbnbQuestion = null;
                var airbnbIsVoice = false;

                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    airbnbQuestion = message.Text;
                    Console.WriteLine($"[Airbnb][{senderName}]: {airbnbQuestion}");
                }
                else if (message.Voice is { } airbnbVoice)
                {
                    var audioBytes = await DownloadFileAsync(airbnbVoice.FileId);
                    airbnbQuestion = await TranscribeAutoAsync(audioBytes);
                    airbnbIsVoice = true;
                    Console.WriteLine($"[Airbnb Voice][{senderName}]: {airbnbQuestion}");
                }

                if (string.IsNullOrWhiteSpace(airbnbQuestion)) continue;

                // Detect language switch command: "Language <name>"
                var langMatch = System.Text.RegularExpressions.Regex.Match(
                    airbnbQuestion.Trim(), @"^language\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (langMatch.Success)
                {
                    var raw = langMatch.Groups[1].Value.Trim();
                    airbnbLanguage = char.ToUpper(raw[0]) + raw.Substring(1).ToLower();
                    var confirmation = $"Language switched to {airbnbLanguage}.";
                    Console.WriteLine($"[Airbnb] {confirmation}");
                    if (airbnbIsVoice)
                    {
                        var audioOut = await TextToSpeechAsync(confirmation, cesarVoiceId);
                        using var stream = new MemoryStream(audioOut);
                        await cesarBot.SendVoice(RapidResponseGroupId, InputFile.FromStream(stream, "voice.mp3"), caption: confirmation);
                    }
                    else
                    {
                        await cesarBot.SendMessage(RapidResponseGroupId, confirmation);
                    }
                    continue;
                }

                await cesarBot.SendChatAction(RapidResponseGroupId, ChatAction.Typing);

                var questionVec = await EmbedQuestionAsync(airbnbQuestion);
                var topIds = FindTopMatches(kbEmbeddings, questionVec, 5);
                var kbEntries = await FetchKbEntriesAsync(topIds);

                if (!airbnbConversations.TryGetValue(chatId, out var convHistory))
                    airbnbConversations[chatId] = convHistory = new List<(string, string)>();

                var weekReservations  = await FetchReservationsAsync("week");
                var reservationContext = FormatReservationsForContext(weekReservations);
                var reply = await GenerateGuestReplyAsync(airbnbQuestion, kbEntries, convHistory, airbnbLanguage, reservationContext);

                convHistory.Add(("user", airbnbQuestion));
                convHistory.Add(("assistant", reply));
                if (convHistory.Count > 20) convHistory.RemoveRange(0, convHistory.Count - 20);

                await LogConversationAsync(chatId, "user", airbnbQuestion, null);
                await LogConversationAsync(chatId, "assistant", reply, topIds);

                if (airbnbIsVoice)
                {
                    var audioOut = await TextToSpeechAsync(reply, cesarVoiceId);
                    using var stream = new MemoryStream(audioOut);
                    await cesarBot.SendVoice(RapidResponseGroupId, InputFile.FromStream(stream, "voice.mp3"), caption: reply);
                }
                else
                {
                    await cesarBot.SendMessage(RapidResponseGroupId, reply);
                }

                await DispatchWebhooksAsync(chatId, senderName, airbnbQuestion);
                continue;
            }

            // Translator group: ask which language to translate to
            if (chatId == TranslatorGroupId)
            {
                string? inputText = null;
                var isVoice = false;

                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    inputText = message.Text;
                    Console.WriteLine($"[Translator][{senderName}]: {inputText}");
                }
                else if (message.Voice is { } mlVoice)
                {
                    var audioBytes = await DownloadFileAsync(mlVoice.FileId);
                    inputText = await TranscribeAsync(audioBytes, "en-US");
                    isVoice = true;
                    Console.WriteLine($"[Translator Voice][{senderName}]: {inputText}");
                }

                if (string.IsNullOrWhiteSpace(inputText)) continue;

                pendingTranslations[message.MessageId] = (inputText, isVoice);

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🇷🇺 Russian",    $"{message.MessageId}:ru"),
                        InlineKeyboardButton.WithCallbackData("🇧🇷 Portuguese", $"{message.MessageId}:pt"),
                        InlineKeyboardButton.WithCallbackData("🇪🇸 Spanish",    $"{message.MessageId}:es"),
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🇵🇱 Polish",     $"{message.MessageId}:pl"),
                        InlineKeyboardButton.WithCallbackData("🇮🇱 Hebrew",     $"{message.MessageId}:he"),
                    }
                });

                await cesarBot.SendMessage(TranslatorGroupId, "Which language would you like to translate to?", replyMarkup: keyboard);

                await DispatchWebhooksAsync(chatId, senderName, inputText);
                continue;
            }

            // Text messages (Management<->English with context)
            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                Console.WriteLine($"[Text][{senderName}]: {message.Text}");

                if (chatId == CasaRosaManagementGroupId)
                {
                    var translated = await TranslateWithClaudeAsync(message.Text, "Russian", "English", conversationHistory);
                    Console.WriteLine($"[EN]: {translated}");
                    AddToHistory(conversationHistory, senderName, translated);
                    await cesarBot.SendMessage(CasaRosaEnglishGroupId, $"<b>{senderName}:</b>\n{translated}", parseMode: ParseMode.Html);
                }
                else
                {
                    var extraction = await ExtractReminderAsync(message.Text);
                    if (extraction != null)
                    {
                        var id = await CreateReminderViaApiAsync(extraction.Message, extraction.ScheduledAt, CasaRosaManagementGroupId, AutoBotId, "Russian");
                        var localTime = extraction.ScheduledAt; // already Portugal time
                        var confirmText = $"✅ Reminder set for {localTime:MMM d 'at' h:mm tt} (Portugal time):\n\"{extraction.Message}\"\nWill be sent to the Russian channel.";
                        var keyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel reminder", $"cancel_reminder:{id}") }
                        });
                        await cesarBot.SendMessage(CasaRosaEnglishGroupId, confirmText, replyMarkup: keyboard);
                        Console.WriteLine($"[Reminder] Created #{id}: {extraction.Message} at {extraction.ScheduledAt:O}");
                    }
                    else
                    {
                        var translated = await TranslateWithClaudeAsync(message.Text, "English", "Russian", conversationHistory);
                        Console.WriteLine($"[RU]: {translated}");
                        AddToHistory(conversationHistory, senderName, message.Text);
                        await cesarBot.SendMessage(CasaRosaManagementGroupId, $"<b>{senderName}:</b>\n{translated}", parseMode: ParseMode.Html);
                    }
                }

                await DispatchWebhooksAsync(chatId, senderName, message.Text);
            }
            // Voice messages (Management<->English with context)
            else if (message.Voice is { } voice)
            {
                Console.WriteLine($"[Voice][{senderName}]: {voice.Duration}s");

                var audioBytes = await DownloadFileAsync(voice.FileId);

                if (chatId == CasaRosaManagementGroupId)
                {
                    var transcript = await TranscribeAsync(audioBytes, "ru-RU");
                    Console.WriteLine($"[Transcript RU]: {transcript}");
                    if (string.IsNullOrWhiteSpace(transcript)) continue;

                    var translated = await TranslateWithClaudeAsync(transcript, "Russian", "English", conversationHistory);
                    Console.WriteLine($"[EN]: {translated}");
                    AddToHistory(conversationHistory, senderName, translated);

                    var audioOut = await TextToSpeechAsync(translated, annaVoiceId);
                    using var stream = new MemoryStream(audioOut);
                    await cesarBot.SendVoice(CasaRosaEnglishGroupId, InputFile.FromStream(stream, "voice.mp3"),
                        caption: $"<b>{senderName}:</b> {translated}", parseMode: ParseMode.Html);
                }
                else
                {
                    var transcript = await TranscribeAsync(audioBytes, "en-US");
                    Console.WriteLine($"[Transcript EN]: {transcript}");
                    if (string.IsNullOrWhiteSpace(transcript)) continue;

                    var voiceExtraction = await ExtractReminderAsync(transcript);
                    if (voiceExtraction != null)
                    {
                        var id = await CreateReminderViaApiAsync(voiceExtraction.Message, voiceExtraction.ScheduledAt, CasaRosaManagementGroupId, AutoBotId, "Russian");
                        var ptTz      = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(voiceExtraction.ScheduledAt, ptTz);
                        var confirmText = $"✅ Reminder set for {localTime:MMM d 'at' h:mm tt} (Portugal time):\n\"{voiceExtraction.Message}\"\nWill be sent to the Russian channel.";
                        var keyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel reminder", $"cancel_reminder:{id}") }
                        });
                        await cesarBot.SendMessage(CasaRosaEnglishGroupId, confirmText, replyMarkup: keyboard);
                        Console.WriteLine($"[Reminder] Created #{id} (voice): {voiceExtraction.Message} at {voiceExtraction.ScheduledAt:O}");
                    }
                    else
                    {
                        var translated = await TranslateWithClaudeAsync(transcript, "English", "Russian", conversationHistory);
                        Console.WriteLine($"[RU]: {translated}");
                        AddToHistory(conversationHistory, senderName, transcript);

                        var audioOut = await TextToSpeechAsync(translated, cesarVoiceId);
                        using var stream = new MemoryStream(audioOut);
                        await cesarBot.SendVoice(CasaRosaManagementGroupId, InputFile.FromStream(stream, "voice.mp3"),
                            caption: $"<b>{senderName}:</b> {translated}", parseMode: ParseMode.Html);
                    }
                }

                await DispatchWebhooksAsync(chatId, senderName, "[voice message]");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] {ex.Message}");
        await Task.Delay(2000);
    }
}

// ── HTTP API handlers ─────────────────────────────────────────────────────────

async Task HandleHttpAsync(HttpListenerContext ctx)
{
    var req  = ctx.Request;
    var resp = ctx.Response;

    try
    {
        var token = req.QueryString["Token"] ?? req.QueryString["token"] ?? "";
        if (token != httpAuthToken)
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        var path   = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        var method = req.HttpMethod.ToUpperInvariant();

        if      (path == "/subscribe" && method == "POST")   { await HandleSubscribeAsync(ctx); return; }
        else if (path == "/subscribe" && method == "DELETE") { HandleUnsubscribe(ctx);           return; }
        else if (path == "/push"      && method == "POST")   { await HandlePushAsync(ctx);       return; }
        else if (path == "/briefing"       && method == "POST") { await SendTomorrowBriefingAsync(); resp.StatusCode = 200; resp.Close(); return; }
        else if (path == "/briefing/today" && method == "POST") { await SendTodayBriefingAsync();    resp.StatusCode = 200; resp.Close(); return; }
        else if (path == "/notify/apartment-ready"   && method == "POST") { await HandleApartmentReadyAsync(ctx); return; }
        else if (path == "/briefing/triple-cleaning" && method == "POST") { await SendTripleCleaningAlertAsync(); resp.StatusCode = 200; resp.Close(); return; }

        resp.StatusCode = 404;
        resp.Close();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[HTTP Error] {ex.Message}");
        try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
    }
}

async Task HandleSubscribeAsync(HttpListenerContext ctx)
{
    using var reader = new StreamReader(ctx.Request.InputStream);
    var body = await reader.ReadToEndAsync();
    var doc  = JsonDocument.Parse(body).RootElement;

    if (!doc.TryGetProperty("groupId", out var groupIdEl) ||
        !doc.TryGetProperty("url",     out var urlEl))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }

    var groupId = groupIdEl.GetInt64();
    var url     = urlEl.GetString() ?? "";
    var tok     = doc.TryGetProperty("token", out var tokEl) ? tokEl.GetString() ?? "" : "";

    lock (subscribersLock)
    {
        if (!subscribers.TryGetValue(groupId, out var list))
            subscribers[groupId] = list = new();

        var idx = list.FindIndex(s => s.Url == url);
        if (idx >= 0)
            list[idx] = (url, tok, 0);
        else
            list.Add((url, tok, 0));
    }

    Console.WriteLine($"[Subscribe] {url} -> group {groupId}");
    ctx.Response.StatusCode = 200;
    ctx.Response.Close();
}

void HandleUnsubscribe(HttpListenerContext ctx)
{
    var url        = ctx.Request.QueryString["url"]     ?? "";
    var groupIdStr = ctx.Request.QueryString["groupId"] ?? "";

    if (string.IsNullOrEmpty(url) || !long.TryParse(groupIdStr, out var groupId))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }

    lock (subscribersLock)
    {
        if (subscribers.TryGetValue(groupId, out var list))
            list.RemoveAll(s => s.Url == url);
    }

    Console.WriteLine($"[Unsubscribe] {url} from group {groupId}");
    ctx.Response.StatusCode = 200;
    ctx.Response.Close();
}

async Task HandleApartmentReadyAsync(HttpListenerContext ctx)
{
    var aptStr = ctx.Request.QueryString["apt"] ?? "";
    if (!int.TryParse(aptStr, out var apt) || apt < 1 || apt > 3)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }
    await autoBot.SendMessage(CasaRosaEnglishGroupId,    $"🏠 Apartment {apt} is ready");
    await autoBot.SendMessage(CasaRosaManagementGroupId, $"🏠 Апартамент {apt} готов");
    await TelegramLog("sent", "Apartment Ready", "English Group + Management", $"Apartment {apt} is ready");
    ctx.Response.StatusCode = 200;
    ctx.Response.Close();
}

async Task HandlePushAsync(HttpListenerContext ctx)
{
    using var reader = new StreamReader(ctx.Request.InputStream);
    var body = await reader.ReadToEndAsync();
    var doc  = JsonDocument.Parse(body).RootElement;

    if (!doc.TryGetProperty("botId",   out var botIdEl) ||
        !doc.TryGetProperty("groupId", out var groupIdEl) ||
        !doc.TryGetProperty("message", out var messageEl))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }

    var botId   = botIdEl.GetString()   ?? "";
    var groupId = groupIdEl.GetInt64();
    var text    = messageEl.GetString() ?? "";

    if (!bots.TryGetValue(botId, out var targetBot))
    {
        var err = Encoding.UTF8.GetBytes($"Unknown botId '{botId}'");
        ctx.Response.StatusCode      = 400;
        ctx.Response.ContentType     = "text/plain";
        ctx.Response.ContentLength64 = err.Length;
        await ctx.Response.OutputStream.WriteAsync(err);
        ctx.Response.Close();
        return;
    }

    await targetBot.SendMessage(groupId, text);
    Console.WriteLine($"[Push] [{botId}] -> group {groupId}: {text}");
    ctx.Response.StatusCode = 200;
    ctx.Response.Close();
}

async Task DispatchWebhooksAsync(long groupId, string sender, string text)
{
    List<(string Url, string Token, int Failures)> targets;
    lock (subscribersLock)
    {
        if (!subscribers.TryGetValue(groupId, out var list) || list.Count == 0) return;
        targets = list.ToList();
    }

    var payload = JsonSerializer.Serialize(new
    {
        @event    = "MessageReceived",
        groupId,
        sender,
        message   = text,
        timestamp = DateTime.UtcNow.ToString("O")
    });

    foreach (var (url, token, _) in targets)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            req.Headers.Add("X-Webhook-Token", token);
            var resp = await httpClient.SendAsync(req);

            lock (subscribersLock)
            {
                if (!subscribers.TryGetValue(groupId, out var list)) return;
                var idx = list.FindIndex(s => s.Url == url);
                if (idx < 0) return;

                if (resp.IsSuccessStatusCode)
                {
                    list[idx] = (url, token, 0);
                }
                else
                {
                    var failures = list[idx].Failures + 1;
                    if (failures >= 3) { list.RemoveAt(idx); Console.WriteLine($"[Webhook] Removed {url} after 3 failures."); }
                    else               { list[idx] = (url, token, failures); }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Webhook Error] {url}: {ex.Message}");
            lock (subscribersLock)
            {
                if (!subscribers.TryGetValue(groupId, out var list)) return;
                var idx = list.FindIndex(s => s.Url == url);
                if (idx < 0) return;
                var failures = list[idx].Failures + 1;
                if (failures >= 3) { list.RemoveAt(idx); Console.WriteLine($"[Webhook] Removed {url} after 3 failures."); }
                else               { list[idx] = (url, token, failures); }
            }
        }
    }
}

// ── Briefings ─────────────────────────────────────────────────────────────────

async Task<TimeOnly> GetTripleCleaningTimeAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/triple_cleaning_time?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "08:00";
            if (TimeOnly.TryParse(value, out var t)) return t;
        }
    }
    catch { }
    return new TimeOnly(8, 0);
}

async Task<long> GetTripleCleaningChannelAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/triple_cleaning_channel?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "";
            if (long.TryParse(value, out var id)) return id;
        }
    }
    catch { }
    return CasaRosaManagementGroupId;
}

async Task SendTripleCleaningAlertAsync()
{
    var portugalTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var today      = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz));
    var channel    = await GetTripleCleaningChannelAsync();

    // Check each of the next 7 days for all 3 apartments checking out on the same day
    var tripleDay = (DateOnly?)null;
    for (int i = 1; i <= 7; i++)
    {
        var day    = today.AddDays(i);
        var dayStr = day.ToString("yyyy-MM-dd");
        var url    = $"{reservationApiBaseUrl}/checkout?Token={reservationApiToken}&date={dayStr}";
        var resp   = await httpClient.GetAsync(url);
        if (!resp.IsSuccessStatusCode) continue;
        var rows = await resp.Content.ReadFromJsonAsync<List<BriefingReservation>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        var checkoutApts = rows.Select(r => r.ApartmentNumber).Distinct().Count();
        if (checkoutApts >= 3) { tripleDay = day; break; }
    }

    if (tripleDay == null)
    {
        Console.WriteLine("[Triple Cleaning] No triple cleaning day found in next 7 days.");
        await TelegramLog("sent", "Triple Cleaning Alert", ChannelName(channel), "No triple cleaning day found in next 7 days — alert skipped.");
        return;
    }

    var dateStr = tripleDay.Value.ToString("d MMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
    var msg = $"🧹🧹🧹 Тройная уборка!\n" +
              $"Все 3 апартамента нуждаются в уборке {dateStr}";
    await autoBot.SendMessage(channel, msg);
    Console.WriteLine($"[Triple Cleaning] Alert sent for {tripleDay} to {channel}");
    await TelegramLog("sent", "Triple Cleaning Alert", ChannelName(channel), $"Triple cleaning on {dateStr}");
}

async Task<TimeOnly> GetTomorrowBriefingTimeAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/briefing_time?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "18:00";
            if (TimeOnly.TryParse(value, out var t)) return t;
        }
    }
    catch { }
    return new TimeOnly(18, 0);
}

async Task<long> GetTomorrowBriefingChannelAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/briefing_channel?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "";
            if (long.TryParse(value, out var id)) return id;
        }
    }
    catch { }
    return CasaRosaEnglishGroupId;
}

async Task<TimeOnly> GetTodayBriefingTimeAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/today_briefing_time?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "09:00";
            if (TimeOnly.TryParse(value, out var t)) return t;
        }
    }
    catch { }
    return new TimeOnly(9, 0);
}

async Task<long> GetTodayBriefingChannelAsync()
{
    try
    {
        var url  = $"{adminApiBaseUrl}/api/config/today_briefing_channel?Token={adminApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (resp.IsSuccessStatusCode)
        {
            var json  = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var value = json.GetProperty("value").GetString() ?? "";
            if (long.TryParse(value, out var id)) return id;
        }
    }
    catch { }
    return CasaRosaEnglishGroupId;
}

string? BuildBriefingMessage(string labelRu, DateOnly date, List<BriefingReservation> reservations,
    Dictionary<int, string?> ciNotes, Dictionary<int, string?> coNotes)
{
    var dateIso = date.ToString("yyyy-MM-dd");
    var dateStr = date.ToString("d MMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
    var lines   = new List<string> { $"📅 {labelRu} — {dateStr}" };
    bool any    = false;

    for (int apt = 1; apt <= 3; apt++)
    {
        var checkout = reservations.FirstOrDefault(r => r.ApartmentNumber == apt && r.CheckOutDate == dateIso);
        var checkin  = reservations.FirstOrDefault(r => r.ApartmentNumber == apt && r.CheckInDate  == dateIso);

        if (checkout == null && checkin == null) continue;
        any = true;

        var coTime = checkout != null ? (coNotes.GetValueOrDefault(apt) ?? "11:00") : null;
        lines.Add("");
        lines.Add($"🏠 Апт {apt}");
        lines.Add($"📤 {(coTime ?? "—")}");
        lines.Add("🧹");

        if (checkin != null)
        {
            var arrivalTime = ciNotes.GetValueOrDefault(apt)
                ?? (!string.IsNullOrWhiteSpace(checkin.Registration?.ArrivalTime)
                    ? checkin.Registration.ArrivalTime : "неизвестно");
            var guests = $"{checkin.Adults}/{checkin.Children}/{checkin.Infants}";
            lines.Add($"📥 {arrivalTime} ({guests})");

            var reqs = new List<string>();
            if (checkin.Registration != null)
            {
                reqs.Add(checkin.Registration.EarlyCheckIn ? "⏰✅" : "⏰❌");
                reqs.Add(checkin.Registration.CribSetup    ? "🛏️✅" : "🛏️❌");
                reqs.Add(checkin.Registration.SofaSetup    ? "🛋️✅" : "🛋️❌");
                if (!string.IsNullOrWhiteSpace(checkin.Registration.OtherRequests))
                    reqs.Add(checkin.Registration.OtherRequests.Trim());
            }
            if (reqs.Count > 0) lines.Add($"   {string.Join("  ", reqs)}");
        }
        else
        {
            lines.Add("📥 —");
        }
    }

    return any ? string.Join("\n", lines) : null;
}

async Task SendTomorrowBriefingAsync()
{
    var portugalTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var tomorrow   = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz).AddDays(1));
    var channel    = await GetTomorrowBriefingChannelAsync();
    var reservations = await FetchReservationsAsync("tomorrow");
    var (ciNotes, coNotes) = await FetchCalNotesForBriefingAsync(tomorrow);
    var msg = BuildBriefingMessage("Завтра", tomorrow, reservations, ciNotes, coNotes);
    if (msg == null)
    {
        await autoBot.SendMessage(channel, "Уборки завтра нет.");
        Console.WriteLine("[Tomorrow Briefing] Nothing to report.");
        await TelegramLog("sent", "Tomorrow Briefing", ChannelName(channel), "No cleaning tomorrow.");
        return;
    }
    await autoBot.SendMessage(channel, msg);
    Console.WriteLine($"[Tomorrow Briefing] Sent to {channel}:\n{msg}");
    await TelegramLog("sent", "Tomorrow Briefing", ChannelName(channel), msg.Length > 200 ? msg[..200] + "…" : msg);
}

async Task SendTodayBriefingAsync()
{
    var portugalTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var today      = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, portugalTz));
    var channel    = await GetTodayBriefingChannelAsync();
    var reservations = await FetchReservationsAsync("today");
    var (ciNotes, coNotes) = await FetchCalNotesForBriefingAsync(today);
    var msg = BuildBriefingMessage("Сегодня", today, reservations, ciNotes, coNotes);
    if (msg == null)
    {
        await autoBot.SendMessage(channel, "Уборки сегодня нет.");
        Console.WriteLine("[Today Briefing] Nothing to report.");
        await TelegramLog("sent", "Today Briefing", ChannelName(channel), "No cleaning today.");
        return;
    }
    await autoBot.SendMessage(channel, msg);
    Console.WriteLine($"[Today Briefing] Sent to {channel}:\n{msg}");
    await TelegramLog("sent", "Today Briefing", ChannelName(channel), msg.Length > 200 ? msg[..200] + "…" : msg);
}

// ── Reservation context helpers ───────────────────────────────────────────────

async Task<List<BriefingReservation>> FetchReservationsAsync(string date)
{
    var url  = $"{reservationApiBaseUrl}/reservations?Token={reservationApiToken}&date={date}";
    var resp = await httpClient.GetAsync(url);
    if (!resp.IsSuccessStatusCode) return new();
    return await resp.Content.ReadFromJsonAsync<List<BriefingReservation>>(
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

async Task<(Dictionary<int, string?> ciNotes, Dictionary<int, string?> coNotes)> FetchCalNotesForBriefingAsync(DateOnly date)
{
    var ciNotes = new Dictionary<int, string?>();
    var coNotes = new Dictionary<int, string?>();
    try
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        var url  = $"{reservationApiBaseUrl}/manage/reservations?from={dateStr}&to={dateStr}&status=active&Token={reservationApiToken}";
        var resp = await httpClient.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return (ciNotes, coNotes);
        var rows = await resp.Content.ReadFromJsonAsync<List<BriefingManageRow>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var r in rows)
        {
            var noteResp = await httpClient.GetAsync(
                $"{reservationApiBaseUrl}/manage/reservations/{r.Id}/cal-note?Token={reservationApiToken}");
            if (!noteResp.IsSuccessStatusCode) continue;
            var note = await noteResp.Content.ReadFromJsonAsync<BriefingCalNote>(opts);
            if (note == null) continue;
            if (r.CheckInDate == date  && !string.IsNullOrWhiteSpace(note.CheckInTime))
                ciNotes[r.ApartmentNumber] = note.CheckInTime;
            if (r.CheckOutDate == date && !string.IsNullOrWhiteSpace(note.CheckOutTime))
                coNotes[r.ApartmentNumber] = note.CheckOutTime;
        }
    }
    catch (Exception ex) { Console.WriteLine($"[CalNotes] Error: {ex.Message}"); }
    return (ciNotes, coNotes);
}

string FormatReservationsForContext(List<BriefingReservation> reservations)
{
    if (reservations.Count == 0) return "No reservations in the upcoming week.";
    var sb = new StringBuilder();
    foreach (var r in reservations.OrderBy(x => x.CheckInDate).ThenBy(x => x.ApartmentNumber))
    {
        sb.Append($"Apt {r.ApartmentNumber} | {r.ReservationName ?? "?"} | Check-in: {r.CheckInDate} | Check-out: {r.CheckOutDate} | Guests: {r.Adults}A/{r.Children}C/{r.Infants}I");
        if (r.Registration != null)
        {
            if (!string.IsNullOrWhiteSpace(r.Registration.ArrivalTime))
                sb.Append($" | Arrival: {r.Registration.ArrivalTime}");
            var reqs = new List<string>();
            if (r.Registration.EarlyCheckIn) reqs.Add("early check-in");
            if (r.Registration.CribSetup)    reqs.Add("crib");
            if (r.Registration.SofaSetup)    reqs.Add("sofa bed");
            if (r.Registration.FoldableBed)  reqs.Add("foldable bed");
            if (!string.IsNullOrWhiteSpace(r.Registration.OtherRequests))
                reqs.Add(r.Registration.OtherRequests.Trim());
            if (reqs.Count > 0) sb.Append($" | Requests: {string.Join(", ", reqs)}");
        }
        sb.AppendLine();
    }
    return sb.ToString().TrimEnd();
}

// ── Reminder helpers ──────────────────────────────────────────────────────────

async Task<ReminderExtraction?> ExtractReminderAsync(string text)
{
    var ptTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var now  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ptTz);

    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
    request.Headers.Add("x-api-key", anthropicApiKey);
    request.Headers.Add("anthropic-version", "2023-06-01");
    request.Content = JsonContent.Create(new
    {
        model      = "claude-haiku-4-5-20251001",
        max_tokens = 256,
        system     = $$"""
            Determine if the message is a reminder request. If yes, extract details.
            Current time in Portugal: {{now:yyyy-MM-dd HH:mm}}
            Return JSON only:
            {
              "is_reminder": boolean,
              "message": "reminder content in English or null",
              "scheduled_at_utc": "ISO 8601 UTC datetime or null"
            }
            """,
        messages = new[] { new { role = "user", content = text } }
    });

    var response = await httpClient.SendAsync(request);
    var json     = await response.Content.ReadFromJsonAsync<JsonElement>();
    var raw      = json.GetProperty("content")[0].GetProperty("text").GetString() ?? "{}";

    try
    {
        var doc      = JsonDocument.Parse(raw).RootElement;
        if (!doc.GetProperty("is_reminder").GetBoolean()) return null;
        var message  = doc.GetProperty("message").GetString();
        var dtStr    = doc.GetProperty("scheduled_at_utc").GetString();
        if (message == null || dtStr == null) return null;
        if (!DateTime.TryParse(dtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var scheduledAt)) return null;
        if (scheduledAt.ToUniversalTime() <= DateTime.UtcNow) return null; // ignore past times
        var ptTime = TimeZoneInfo.ConvertTimeFromUtc(scheduledAt.ToUniversalTime(), ptTz);
        return new ReminderExtraction(message, DateTime.SpecifyKind(ptTime, DateTimeKind.Unspecified));
    }
    catch { return null; }
}

async Task<int> CreateReminderViaApiAsync(string message, DateTime scheduledAtUtc, long channelId, string botId, string language)
{
    var url  = $"{reservationApiBaseUrl}/admin/reminders?token={reservationApiToken}";
    var resp = await httpClient.PostAsJsonAsync(url, new { message, scheduledAt = scheduledAtUtc, channelId, botId, language });
    var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("id").GetInt32();
}

async Task CancelReminderViaApiAsync(int id)
{
    await httpClient.PostAsync(
        $"{reservationApiBaseUrl}/admin/reminders/{id}/cancel?token={reservationApiToken}", null);
}

async Task FireDueRemindersAsync()
{
    var resp = await httpClient.GetAsync($"{reservationApiBaseUrl}/admin/reminders?token={reservationApiToken}");
    if (!resp.IsSuccessStatusCode) return;

    var pending = await resp.Content.ReadFromJsonAsync<List<ApiReminderRow>>(
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

    var ptTz  = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    var ptNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ptTz);
    foreach (var r in pending.Where(x => x.ScheduledAt <= ptNow))
    {
        try
        {
            var translated = await TranslateWithClaudeAsync(r.Message, "English", r.Language);
            if (bots.TryGetValue(r.BotId, out var bot))
                await bot.SendMessage(r.ChannelId, translated);

            await httpClient.PostAsync(
                $"{reservationApiBaseUrl}/admin/reminders/{r.Id}/sent?token={reservationApiToken}", null);

            Console.WriteLine($"[Reminder] Fired #{r.Id}: {translated}");
            await TelegramLog("sent", "Reminder", ChannelName(r.ChannelId), translated.Length > 200 ? translated[..200] + "…" : translated);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Reminder] Failed #{r.Id}: {ex.Message}");
            await TelegramLog("error", "Reminder", null, $"#{r.Id}: {ex.Message}", isError: true);
        }
    }
}

// ── Airbnb Q&A functions ──────────────────────────────────────────────────────

async Task<List<(int KbId, float[] Vector)>> LoadEmbeddingsAsync()
{
    var result = new List<(int, float[])>();
    using var conn = new SqlConnection(dbConnectionString);
    await conn.OpenAsync();
    using var cmd = new SqlCommand("SELECT KbId, EmbeddingVector FROM dbo.KnowledgeBase_Embeddings", conn);
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var kbId  = reader.GetInt32(0);
        var bytes = (byte[])reader[1];
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        result.Add((kbId, floats));
    }
    return result;
}

async Task<float[]> EmbedQuestionAsync(string text)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
    request.Headers.Add("Authorization", $"Bearer {openAiApiKey}");
    request.Content = JsonContent.Create(new { model = "text-embedding-3-small", input = text });
    var response = await httpClient.SendAsync(request);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("data")[0].GetProperty("embedding")
               .EnumerateArray().Select(x => x.GetSingle()).ToArray();
}

List<int> FindTopMatches(List<(int KbId, float[] Vector)> embeddings, float[] query, int topK)
{
    return embeddings
        .Select(e => (e.KbId, Score: CosineSimilarity(e.Vector, query)))
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .Select(x => x.KbId)
        .ToList();
}

float CosineSimilarity(float[] a, float[] b)
{
    float dot = 0, normA = 0, normB = 0;
    for (int i = 0; i < a.Length; i++)
    {
        dot   += a[i] * b[i];
        normA += a[i] * a[i];
        normB += b[i] * b[i];
    }
    return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
}

async Task<List<(string Question, string Answer)>> FetchKbEntriesAsync(List<int> ids)
{
    var result = new List<(string, string)>();
    if (ids.Count == 0) return result;
    using var conn = new SqlConnection(dbConnectionString);
    await conn.OpenAsync();
    using var cmd = new SqlCommand($"SELECT Question, Answer FROM dbo.KnowledgeBase WHERE Id IN ({string.Join(",", ids)})", conn);
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        result.Add((reader.GetString(0), reader.GetString(1)));
    return result;
}

async Task<string> GenerateGuestReplyAsync(string question, List<(string Question, string Answer)> matches, List<(string Role, string Content)> history, string language = "English", string? reservationContext = null)
{
    var kbContext = string.Join("\n\n", matches.Select(m => $"Q: {m.Question}\nA: {m.Answer}"));

    var reservationSection = reservationContext is not null ? $"""

        ## Management queries
        You also assist the internal management team with operational questions. When asked about reservations (who is checking in/out, guest counts, arrival times, upcoming activity), answer using the live reservation data below. Be concise and factual.
        - Apartment 1 = ground floor / 1st floor
        - Apartment 2 = 2nd floor
        - Apartment 3 = 3rd floor
        - Today's date: {DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon"))):yyyy-MM-dd}

        Upcoming reservations (next 7 days):
        {reservationContext}
        """ : "";

    var systemPrompt = $"""
        LANGUAGE RULE: You MUST reply exclusively in {language}. This is mandatory. Do not use any other language under any circumstances, regardless of what language the guest uses.

        You are an Airbnb host assistant for Casa Rosa, Cascais, Portugal.

        Property facts:
        - Address: Beco dos Inválidos 5, Cascais, Portugal
        - Check-in: 15:00 (self check-in, keypad code, no physical keys)
        - Check-out: 11:00
        - Tourist tax: €4 per adult per night (children under 13 exempt), payable cash or via Airbnb
        - Emergency phone: +351-912-947-429
        - GPS landmark: Restaurant "Armazém 22" — house is right next to it, pink building
        - Website: https://casarosahouse.com
        - Helper: Anastasia (on-site cleaning/handover)
        - Parking: Estacionamento Marechal Carmona (€27/day, 8 min walk); free parking ~12 min walk near Cascais Beauty Clinic

        The following are example Q&A pairs for context only — they may be in English but your reply must still be in {language}:
        {kbContext}
        {reservationSection}
        REMINDER: Your reply must be in {language} only. Never deviate from this.
        """;

    var messages = new List<object>();
    foreach (var (role, content) in history)
        messages.Add(new { role, content });
    messages.Add(new { role = "user", content = question });

    var tools = new[]
    {
        new
        {
            name        = "get_reservations",
            description = "Fetch reservations for a specific date range. Only call this when the question refers to dates NOT already covered by the reservation data in the system prompt.",
            input_schema = new
            {
                type       = "object",
                properties = new
                {
                    from_date = new { type = "string",  description = "Start date in yyyy-MM-dd format" },
                    to_date   = new { type = "string",  description = "End date in yyyy-MM-dd format" },
                    apartment = new { type = "integer", description = "Apartment number 1-3 — omit for all apartments" }
                },
                required = new[] { "from_date", "to_date" }
            }
        }
    };

    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
    request.Headers.Add("x-api-key", anthropicApiKey);
    request.Headers.Add("anthropic-version", "2023-06-01");
    request.Content = JsonContent.Create(new
    {
        model      = "claude-sonnet-4-6",
        max_tokens = 1024,
        system     = systemPrompt,
        tools,
        messages
    });

    var response = await httpClient.SendAsync(request);
    var json     = await response.Content.ReadFromJsonAsync<JsonElement>();

    if (json.GetProperty("stop_reason").GetString() == "tool_use")
    {
        var contentArr   = json.GetProperty("content");
        var toolUseBlock = contentArr.EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use");

        var toolId   = toolUseBlock.GetProperty("id").GetString()!;
        var input    = toolUseBlock.GetProperty("input");
        var fromDate = input.GetProperty("from_date").GetString()!;
        var toDate   = input.GetProperty("to_date").GetString()!;
        var aptParam = input.TryGetProperty("apartment", out var aptEl) ? $"&apt={aptEl.GetInt32()}" : "";

        var resUrl  = $"{reservationApiBaseUrl}/manage/reservations?from={fromDate}&to={toDate}{aptParam}&Token={reservationApiToken}";
        var resResp = await httpClient.GetAsync(resUrl);
        string toolResult;
        if (resResp.IsSuccessStatusCode)
        {
            var fetched = await resResp.Content.ReadFromJsonAsync<List<ToolReservation>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            toolResult = fetched.Count > 0
                ? string.Join("\n", fetched.Select(r =>
                    $"Apt {r.ApartmentNumber} | {r.ReservationName ?? "?"} | Check-in: {r.CheckInDate} | Check-out: {r.CheckOutDate} | Guests: {r.Adults}A/{r.Children}C/{r.Infants}I"))
                : "No reservations found for that date range.";
        }
        else
        {
            toolResult = "Could not retrieve reservation data.";
        }

        messages.Add(new { role = "assistant", content = contentArr });
        messages.Add(new
        {
            role    = "user",
            content = new[] { new { type = "tool_result", tool_use_id = toolId, content = toolResult } }
        });

        var request2 = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request2.Headers.Add("x-api-key", anthropicApiKey);
        request2.Headers.Add("anthropic-version", "2023-06-01");
        request2.Content = JsonContent.Create(new
        {
            model      = "claude-sonnet-4-6",
            max_tokens = 1024,
            system     = systemPrompt,
            tools,
            messages
        });

        var response2 = await httpClient.SendAsync(request2);
        var json2     = await response2.Content.ReadFromJsonAsync<JsonElement>();
        return json2.GetProperty("content")[0].GetProperty("text").GetString()
               ?? "I'll have the host follow up with you shortly.";
    }

    return json.GetProperty("content")[0].GetProperty("text").GetString()
           ?? "I'll have the host follow up with you shortly.";
}

async Task LogConversationAsync(long sessionId, string role, string message, List<int>? matchedIds)
{
    try
    {
        using var conn = new SqlConnection(dbConnectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.ConversationLog (SessionId, Role, Message, MatchedKbIds) VALUES (@sid, @role, @msg, @mids)", conn);
        cmd.Parameters.AddWithValue("@sid",  sessionId.ToString());
        cmd.Parameters.AddWithValue("@role", role);
        cmd.Parameters.AddWithValue("@msg",  message);
        cmd.Parameters.AddWithValue("@mids", matchedIds != null ? (object)string.Join(",", matchedIds) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Log Error] {ex.Message}");
    }
}

// ── Translation functions ─────────────────────────────────────────────────────

void AddToHistory(List<string> history, string speaker, string englishText)
{
    history.Add($"{speaker}: {englishText}");
    if (history.Count > MaxHistoryItems)
        history.RemoveAt(0);
}

async Task<string> TranslateWithClaudeAsync(string text, string fromLang, string toLang, List<string>? history = null)
{
    var contextBlock = history is { Count: > 0 }
        ? $"\n\nConversation context (use this to resolve pronouns and references):\n{string.Join("\n", history)}\n"
        : "";

    var userMessage = $"Translate from {fromLang} to {toLang}.{contextBlock}\nMessage to translate: {text}";

    var formalNote = toLang == "Russian"
        ? " Always address Anastasia in formal Russian (use Вы/Вас/Вам, never ты/тебя/тебе)."
        : "";

    var systemPrompt = $"""
        You are a precise translator for a property management business.

        Background context:
        - We manage Casa Rosa, a property with 3 apartments (Apartment 1, 2, 3) in Cascais, Portugal.
        - Anastasia is the cleaner who cleans the apartments between guest stays.
        - Conversations typically involve: guest arrival and checkout times, cleaning schedules, when Anastasia can access apartments, and occasional issues reported by guests.
        - The two people communicating are Cesar (the manager, writes in English) and Anastasia (the cleaner, writes in Russian).
        {formalNote}

        Return ONLY the translated text, no explanations, no quotes, nothing else.
        """;

    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
    request.Headers.Add("x-api-key", anthropicApiKey);
    request.Headers.Add("anthropic-version", "2023-06-01");
    request.Content = JsonContent.Create(new
    {
        model      = "claude-haiku-4-5-20251001",
        max_tokens = 1024,
        system     = systemPrompt,
        messages   = new[] { new { role = "user", content = userMessage } }
    });

    var response = await httpClient.SendAsync(request);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("content")[0].GetProperty("text").GetString() ?? text;
}

// ── Voice / Audio functions ───────────────────────────────────────────────────

async Task<byte[]> DownloadFileAsync(string fileId)
{
    var file = await cesarBot.GetFile(fileId);
    var url  = $"https://api.telegram.org/file/bot{cesarBotToken}/{file.FilePath}";
    return await httpClient.GetByteArrayAsync(url);
}

async Task<string> TranscribeAsync(byte[] audioBytes, string languageCode)
{
    var url  = $"https://speech.googleapis.com/v1/speech:recognize?key={googleApiKey}";
    var body = new
    {
        config = new { encoding = "OGG_OPUS", sampleRateHertz = 48000, languageCode },
        audio  = new { content = Convert.ToBase64String(audioBytes) }
    };

    var response = await httpClient.PostAsJsonAsync(url, body);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

    if (json.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
        return results[0].GetProperty("alternatives")[0].GetProperty("transcript").GetString() ?? "";

    return "";
}

async Task<string> TranscribeAutoAsync(byte[] audioBytes)
{
    // Use Google STT multi-language detection for Airbnb guests
    var url  = $"https://speech.googleapis.com/v1p1beta1/speech:recognize?key={googleApiKey}";
    var body = new
    {
        config = new
        {
            encoding                = "OGG_OPUS",
            sampleRateHertz         = 48000,
            languageCode            = "en-US",
            alternativeLanguageCodes = new[] { "he-IL", "ru-RU", "pt-PT", "es-ES", "pl-PL", "fr-FR", "de-DE", "it-IT", "nl-NL", "ar-SA" }
        },
        audio = new { content = Convert.ToBase64String(audioBytes) }
    };

    var response = await httpClient.PostAsJsonAsync(url, body);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

    if (json.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
        return results[0].GetProperty("alternatives")[0].GetProperty("transcript").GetString() ?? "";

    return "";
}

async Task<string> GetVoiceIdAsync(string voiceName)
{
    var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
    request.Headers.Add("xi-api-key", elevenLabsApiKey);
    var response = await httpClient.SendAsync(request);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();

    foreach (var voice in json.GetProperty("voices").EnumerateArray())
    {
        if (voice.GetProperty("name").GetString()?.Equals(voiceName, StringComparison.OrdinalIgnoreCase) == true)
            return voice.GetProperty("voice_id").GetString() ?? "";
    }

    throw new Exception($"Voice '{voiceName}' not found in ElevenLabs account.");
}

async Task<byte[]> TextToSpeechAsync(string text, string voiceId)
{
    var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}");
    request.Headers.Add("xi-api-key", elevenLabsApiKey);
    request.Content = JsonContent.Create(new
    {
        text,
        model_id      = "eleven_multilingual_v2",
        voice_settings = new { stability = 0.5, similarity_boost = 0.75 }
    });

    var res = await httpClient.SendAsync(request);
    res.EnsureSuccessStatusCode();
    return await res.Content.ReadAsByteArrayAsync();
}

// ── Types ─────────────────────────────────────────────────────────────────────

record BriefingReservation(
    int ApartmentNumber,
    string? ReservationName,
    string? CheckInDate,
    string? CheckOutDate,
    int Adults,
    int Children,
    int Infants,
    BriefingRegistration? Registration);

record ReminderExtraction(string Message, DateTime ScheduledAt);
record ApiReminderRow(int Id, string Message, DateTime ScheduledAt, long ChannelId, string BotId, string Language);
record BriefingManageRow(int Id, int ApartmentNumber, DateOnly CheckInDate, DateOnly CheckOutDate);
record BriefingCalNote(string? CheckInTime, string? CheckOutTime);

record ToolReservation(
    int      ApartmentNumber,
    string?  ReservationName,
    string?  CheckInDate,
    string?  CheckOutDate,
    int      Adults,
    int      Children,
    int      Infants,
    string?  Status);

record BriefingRegistration(
    string? ArrivalTime,
    bool    EarlyCheckIn,
    bool    CribSetup,
    bool    SofaSetup,
    bool    FoldableBed,
    string? OtherRequests);
