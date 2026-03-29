using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR.Client;

var result = new VerificationResult
{
    BaseUrl = RuntimeConfig.BaseUrl,
    BuyerUserName = RuntimeConfig.BuyerUserName,
    SellerUserName = RuntimeConfig.SellerUserName,
    StartedAtUtc = DateTime.UtcNow
};

BuyerSession? buyer = null;
SellerSession? seller = null;
HubConnection? buyerHub = null;
HubConnection? sellerHub = null;

try
{
    buyer = await BuyerSession.LoginAsync();
    seller = await SellerSession.LoginAsync();

    result.BuyerLoginOk = true;
    result.SellerLoginOk = true;

    var conversation = await buyer.GetConversationAsync(RuntimeConfig.SellerId, createIfMissing: true);
    result.ConversationId = conversation.ConversationId;
    result.BuyerNegotiateProbe = await buyer.ProbeHubNegotiateAsync();
    result.SellerNegotiateProbe = await seller.ProbeHubNegotiateAsync();

    var buyerEvents = new RealtimeCollector();
    var sellerEvents = new RealtimeCollector();

    buyerHub = BuildHubConnection(buyer.CookieContainer, buyerEvents, role: "buyer");
    sellerHub = BuildHubConnection(seller.CookieContainer, sellerEvents, role: "seller");

    await sellerHub.StartAsync();
    await buyerHub.StartAsync();
    await buyerHub.InvokeAsync("JoinConversation", conversation.ConversationId);
    await sellerHub.InvokeAsync("JoinConversation", conversation.ConversationId);

    result.BuyerHubConnected = buyerHub.State == HubConnectionState.Connected;
    result.SellerHubConnected = sellerHub.State == HubConnectionState.Connected;

    var buyerMessage = $"Codex realtime verify buyer -> seller {DateTime.UtcNow:O}";
    var sellerEventStopwatch = Stopwatch.StartNew();
    await buyer.SendMessageAsync(conversation.ConversationId, buyerMessage, RuntimeConfig.SellerId);
    var sellerEvent = await sellerEvents.WaitForAsync(
        e => e.EventName == "newConversationOrMessage" && e.ConversationId == conversation.ConversationId,
        TimeSpan.FromSeconds(4),
        "seller realtime event");
    sellerEventStopwatch.Stop();

    result.BuyerToSeller = new MessageFlowResult
    {
        SentContent = buyerMessage,
        EventName = sellerEvent.EventName,
        ConversationId = sellerEvent.ConversationId,
        ReceivedContent = sellerEvent.MessageContent,
        ElapsedMilliseconds = sellerEventStopwatch.ElapsedMilliseconds
    };

    var sellerMessage = $"Codex realtime verify seller -> buyer {DateTime.UtcNow:O}";
    var buyerEventStopwatch = Stopwatch.StartNew();
    await seller.SendMessageAsync(conversation.ConversationId, sellerMessage);
    var buyerEvent = await buyerEvents.WaitForAsync(
        e => e.EventName == "receiveMessage" &&
             e.ConversationId == conversation.ConversationId &&
             string.Equals(e.MessageContent, sellerMessage, StringComparison.Ordinal),
        TimeSpan.FromSeconds(4),
        "buyer realtime event");
    buyerEventStopwatch.Stop();

    result.SellerToBuyer = new MessageFlowResult
    {
        SentContent = sellerMessage,
        EventName = buyerEvent.EventName,
        ConversationId = buyerEvent.ConversationId,
        ReceivedContent = buyerEvent.MessageContent,
        ElapsedMilliseconds = buyerEventStopwatch.ElapsedMilliseconds
    };

    result.Success =
        result.BuyerToSeller.ConversationId == conversation.ConversationId &&
        string.Equals(result.BuyerToSeller.EventName, "newConversationOrMessage", StringComparison.OrdinalIgnoreCase) &&
        result.BuyerToSeller.ElapsedMilliseconds < 4000 &&
        result.SellerToBuyer.ConversationId == conversation.ConversationId &&
        string.Equals(result.SellerToBuyer.EventName, "receiveMessage", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(result.SellerToBuyer.ReceivedContent, sellerMessage, StringComparison.Ordinal) &&
        result.SellerToBuyer.ElapsedMilliseconds < 4000;
}
catch (Exception ex)
{
    result.Error = ex.ToString();
}
finally
{
    result.CompletedAtUtc = DateTime.UtcNow;

    if (buyerHub is not null)
    {
        await buyerHub.DisposeAsync();
    }

    if (sellerHub is not null)
    {
        await sellerHub.DisposeAsync();
    }

    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true
    });
    await File.WriteAllTextAsync(RuntimeConfig.ArtifactPath, json);
    Console.WriteLine(json);
}

return result.Success ? 0 : 1;

static HubConnection BuildHubConnection(
    CookieContainer cookies,
    RealtimeCollector collector,
    string role)
{
    var connection = new HubConnectionBuilder()
        .WithUrl($"{RuntimeConfig.BaseUrl}/hubs/support-chat", options =>
        {
            options.Cookies = cookies;
            options.HttpMessageHandlerFactory = handler =>
            {
                if (handler is HttpClientHandler httpClientHandler)
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                return handler;
            };
        })
        .WithAutomaticReconnect()
        .Build();

    connection.On<JsonElement>("newConversationOrMessage", payload =>
    {
        if (TryReadConversationId(payload, out var conversationId))
        {
            collector.Add(new RealtimeEvent(
                "newConversationOrMessage",
                role,
                conversationId,
                null));
        }
    });

    connection.On<JsonElement>("receiveMessage", payload =>
    {
        if (!TryReadConversationId(payload, out var conversationId))
        {
            return;
        }

        string? content = null;
        if (payload.TryGetProperty("message", out var messagePayload) &&
            messagePayload.ValueKind == JsonValueKind.Object &&
            messagePayload.TryGetProperty("content", out var contentProperty) &&
            contentProperty.ValueKind == JsonValueKind.String)
        {
            content = contentProperty.GetString();
        }

        collector.Add(new RealtimeEvent(
            "receiveMessage",
            role,
            conversationId,
            content));
    });

    return connection;
}

static bool TryReadConversationId(JsonElement payload, out int conversationId)
{
    conversationId = 0;
    return payload.ValueKind == JsonValueKind.Object &&
           payload.TryGetProperty("conversationId", out var conversationIdProperty) &&
           conversationIdProperty.TryGetInt32(out conversationId);
}

sealed class RealtimeCollector
{
    private readonly ConcurrentQueue<RealtimeEvent> _events = new();

    public void Add(RealtimeEvent @event)
    {
        _events.Enqueue(@event);
    }

    public async Task<RealtimeEvent> WaitForAsync(
        Func<RealtimeEvent, bool> predicate,
        TimeSpan timeout,
        string operationName)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            foreach (var @event in _events.ToArray())
            {
                if (predicate(@event))
                {
                    return @event;
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out while waiting for {operationName}.");
    }
}

sealed class BuyerSession : SessionBase
{
    public BuyerSession(HttpClient client, CookieContainer cookieContainer)
        : base(client, cookieContainer)
    {
    }

    public static async Task<BuyerSession> LoginAsync()
    {
        var session = Create<BuyerSession>();
        var loginHtml = await session.GetHtmlAsync("/account/signin");
        var token = ExtractRequestVerificationToken(loginHtml);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Identifier"] = RuntimeConfig.BuyerUserName,
            ["Password"] = RuntimeConfig.Password,
            ["returnUrl"] = $"/shop/{RuntimeConfig.SellerId}#shop-chat",
            ["__RequestVerificationToken"] = token
        });

        using var response = await session.Client.PostAsync("/account/signin", content);
        response.EnsureSuccessStatusCode();

        var landing = await response.Content.ReadAsStringAsync();
        if (landing.Contains("Đăng nhập", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Buyer login did not leave the sign-in screen.");
        }

        session.ChatAntiForgeryToken = ExtractRequestVerificationToken(await session.GetHtmlAsync($"/shop/{RuntimeConfig.SellerId}"));
        return session;
    }

    public async Task<ConversationPayload> GetConversationAsync(int sellerId, bool createIfMissing)
    {
        var response = await Client.GetFromJsonAsync<JsonElement>($"/bff/support-chat/sellers/{sellerId}/conversation?createIfMissing={createIfMissing.ToString().ToLowerInvariant()}");
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("conversation", out var conversation) ||
            conversation.ValueKind != JsonValueKind.Object ||
            !conversation.TryGetProperty("conversationId", out var conversationIdProp) ||
            !conversationIdProp.TryGetInt32(out var conversationId))
        {
            throw new InvalidOperationException("Buyer conversation payload was not valid.");
        }

        return new ConversationPayload(conversationId);
    }

    public async Task SendMessageAsync(int conversationId, string content, int sellerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/bff/support-chat/conversations/{conversationId}/messages");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("RequestVerificationToken", ChatAntiForgeryToken);
        request.Content = JsonContent.Create(new
        {
            content,
            sellerId
        });

        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}

sealed class SellerSession : SessionBase
{
    public SellerSession(HttpClient client, CookieContainer cookieContainer)
        : base(client, cookieContainer)
    {
    }

    public static async Task<SellerSession> LoginAsync()
    {
        var session = Create<SellerSession>();
        var loginHtml = await session.GetHtmlAsync("/Seller/SellerAccount/Login?returnUrl=%2FSeller%2FSupportChat%2FIndex");
        var token = ExtractRequestVerificationToken(loginHtml);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = RuntimeConfig.SellerUserName,
            ["Password"] = RuntimeConfig.Password,
            ["RememberMe"] = "false",
            ["returnUrl"] = "/Seller/SupportChat/Index",
            ["__RequestVerificationToken"] = token
        });

        using var response = await session.Client.PostAsync("/Seller/SellerAccount/Login?returnUrl=%2FSeller%2FSupportChat%2FIndex", content);
        response.EnsureSuccessStatusCode();

        var landing = await response.Content.ReadAsStringAsync();
        if (landing.Contains("Xác thực 2 bước", StringComparison.OrdinalIgnoreCase))
        {
            var twoFactorToken = ExtractRequestVerificationToken(landing);
            var manualEntryKey = TryExtractManualEntryKey(landing) ?? RuntimeConfig.SellerMfaSecret;
            var code = GenerateTotpCode(manualEntryKey);
            var twoFactorForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = code,
                ["__RequestVerificationToken"] = twoFactorToken
            });

            using var twoFactorResponse = await session.Client.PostAsync("/Seller/SellerAccount/TwoFactor", twoFactorForm);
            twoFactorResponse.EnsureSuccessStatusCode();
            landing = await twoFactorResponse.Content.ReadAsStringAsync();
        }

        if (landing.Contains("Xác thực 2 bước", StringComparison.OrdinalIgnoreCase) ||
            landing.Contains("Đăng nhập Nhà bán", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Seller login did not complete authentication.");
        }

        session.ChatAntiForgeryToken = ExtractRequestVerificationToken(await session.GetHtmlAsync("/Seller/SupportChat/Index"));
        return session;
    }

    public async Task SendMessageAsync(int conversationId, string content)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["conversationId"] = conversationId.ToString(),
            ["content"] = content,
            ["__RequestVerificationToken"] = ChatAntiForgeryToken
        });

        using var response = await Client.PostAsync("/Seller/SupportChat/SendMessage", form);
        response.EnsureSuccessStatusCode();
    }
}

abstract class SessionBase
{
    protected SessionBase(HttpClient client, CookieContainer cookieContainer)
    {
        Client = client;
        CookieContainer = cookieContainer;
    }

    protected HttpClient Client { get; }
    public CookieContainer CookieContainer { get; }
    public string ChatAntiForgeryToken { get; protected set; } = string.Empty;

    protected static TSession Create<TSession>() where TSession : SessionBase
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(RuntimeConfig.BaseUrl)
        };

        return (TSession)Activator.CreateInstance(typeof(TSession), client, cookies)!;
    }

    protected async Task<string> GetHtmlAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<NegotiateProbeResult> ProbeHubNegotiateAsync()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = CookieContainer,
            UseCookies = true,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(RuntimeConfig.BaseUrl)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/support-chat/negotiate?negotiateVersion=1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return new NegotiateProbeResult
        {
            StatusCode = (int)response.StatusCode,
            Location = response.Headers.Location?.ToString(),
            BodySnippet = body.Length <= 300 ? body : body[..300]
        };
    }

    protected static string ExtractRequestVerificationToken(string html)
    {
        var match = Regex.Match(
            html,
            "<input[^>]*name=[\"']__RequestVerificationToken[\"'][^>]*value=[\"'](?<value>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
        {
            match = Regex.Match(
                html,
                "<input[^>]*value=[\"'](?<value>[^\"']+)[\"'][^>]*name=[\"']__RequestVerificationToken[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        if (!match.Success)
        {
            throw new InvalidOperationException("Could not extract anti-forgery token from HTML.");
        }

        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    protected static string? TryExtractManualEntryKey(string html)
    {
        var match = Regex.Match(
            html,
            "<div class=\"fs-5 fw-bold\">(?<value>[^<]+)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["value"].Value).Trim()
            : null;
    }

    protected static string GenerateTotpCode(string manualEntryKey)
    {
        var secret = DecodeBase32(manualEntryKey);
        var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(timestep & 0xff);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);

        var code = binaryCode % 1_000_000;
        return code.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string input)
    {
        var normalized = Regex.Replace(input.ToUpperInvariant(), "[^A-Z2-7]", string.Empty);
        var output = new List<byte>(normalized.Length * 5 / 8);

        var buffer = 0;
        var bitsLeft = 0;
        foreach (var ch in normalized)
        {
            var value = ch switch
            {
                >= 'A' and <= 'Z' => ch - 'A',
                >= '2' and <= '7' => ch - '2' + 26,
                _ => throw new FormatException($"Invalid Base32 character '{ch}'.")
            };

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}

sealed record ConversationPayload(int ConversationId);

sealed record RealtimeEvent(
    string EventName,
    string ReceiverRole,
    int ConversationId,
    string? MessageContent);

sealed class VerificationResult
{
    public string BaseUrl { get; set; } = string.Empty;
    public string BuyerUserName { get; set; } = string.Empty;
    public string SellerUserName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public bool BuyerLoginOk { get; set; }
    public bool SellerLoginOk { get; set; }
    public bool BuyerHubConnected { get; set; }
    public bool SellerHubConnected { get; set; }
    public int ConversationId { get; set; }
    public NegotiateProbeResult BuyerNegotiateProbe { get; set; } = new();
    public NegotiateProbeResult SellerNegotiateProbe { get; set; } = new();
    public MessageFlowResult BuyerToSeller { get; set; } = new();
    public MessageFlowResult SellerToBuyer { get; set; } = new();
    public bool Success { get; set; }
    public string? Error { get; set; }
}

sealed class MessageFlowResult
{
    public string SentContent { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public int ConversationId { get; set; }
    public string? ReceivedContent { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

sealed class NegotiateProbeResult
{
    public int StatusCode { get; set; }
    public string? Location { get; set; }
    public string? BodySnippet { get; set; }
}

static class RuntimeConfig
{
    public const string BaseUrl = "https://localhost:7085";
    public const string BuyerUserName = "thu";
    public const string SellerUserName = "binh";
    public const string Password = "FreshFarm123!";
    public const string SellerMfaSecret = "TD6Z2FV2C4R7DZSDNGXAYVVHQA42FWM2";
    public const int SellerId = 2;
    public const string ArtifactPath = "D:\\NCKH\\DOAN\\NCKH-FRESH-FARM\\output\\support-chat-realtime-verify\\runtime-result.json";
}
