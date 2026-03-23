using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var cesarBotToken    = config["Telegram:CesarBotToken"]!;
var autoBotToken     = config["Telegram:AutoBotToken"]!;
var httpAuthToken    = config["Http:AuthToken"]!;
var httpPrefix       = config["Http:Prefix"] ?? "http://+:5100/";
var googleApiKey     = config["Google:ApiKey"]!;
var elevenLabsApiKey = config["ElevenLabs:ApiKey"]!;
var anthropicApiKey  = config["Anthropic:ApiKey"]!;
var openAiApiKey     = config["OpenAI:ApiKey"]!;
var dbConnectionString = config["Database:ConnectionString"]!;

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

                var reply = await GenerateGuestReplyAsync(airbnbQuestion, kbEntries, convHistory, airbnbLanguage);

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
                    var translated = await TranslateWithClaudeAsync(message.Text, "English", "Russian", conversationHistory);
                    Console.WriteLine($"[RU]: {translated}");
                    AddToHistory(conversationHistory, senderName, message.Text);
                    await cesarBot.SendMessage(CasaRosaManagementGroupId, $"<b>{senderName}:</b>\n{translated}", parseMode: ParseMode.Html);
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

                    var translated = await TranslateWithClaudeAsync(transcript, "English", "Russian", conversationHistory);
                    Console.WriteLine($"[RU]: {translated}");
                    AddToHistory(conversationHistory, senderName, transcript);

                    var audioOut = await TextToSpeechAsync(translated, cesarVoiceId);
                    using var stream = new MemoryStream(audioOut);
                    await cesarBot.SendVoice(CasaRosaManagementGroupId, InputFile.FromStream(stream, "voice.mp3"),
                        caption: $"<b>{senderName}:</b> {translated}", parseMode: ParseMode.Html);
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

async Task<string> GenerateGuestReplyAsync(string question, List<(string Question, string Answer)> matches, List<(string Role, string Content)> history, string language = "English")
{
    var kbContext = string.Join("\n\n", matches.Select(m => $"Q: {m.Question}\nA: {m.Answer}"));

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

        REMINDER: Your reply must be in {language} only. Never deviate from this.
        """;

    var messages = new List<object>();
    foreach (var (role, content) in history)
        messages.Add(new { role, content });
    messages.Add(new { role = "user", content = question });

    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
    request.Headers.Add("x-api-key", anthropicApiKey);
    request.Headers.Add("anthropic-version", "2023-06-01");
    request.Content = JsonContent.Create(new
    {
        model      = "claude-sonnet-4-6",
        max_tokens = 1024,
        system     = systemPrompt,
        messages
    });

    var response = await httpClient.SendAsync(request);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
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
