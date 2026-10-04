using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class CommunicationControllerTests
{
    [Fact]
    public async Task Index_DedupesDuplicateOptionsTemplatesPoliciesAndPreferences_FromOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/admin/communications/center")
            {
                return CreateJsonResponse("""
                {
                  "stats": {
                    "totalTemplates": 1,
                    "activeTemplates": 1,
                    "enabledPolicies": 1,
                    "optedOutCount": 2,
                    "updatedPreferences24h": 3
                  },
                  "filters": {
                    "q": "",
                    "channel": "email",
                    "eventType": "order_created",
                    "channelOptions": [
                      { "value": "email", "text": "" },
                      { "value": "email", "text": "Email" }
                    ],
                    "eventOptions": [
                      { "value": "order_created", "text": "" },
                      { "value": "order_created", "text": "Don hang duoc tao" }
                    ],
                    "audienceOptions": [
                      { "value": "buyer", "text": "" },
                      { "value": "buyer", "text": "Nguoi mua" }
                    ],
                    "deliveryModeOptions": [
                      { "value": "immediate", "text": "" },
                      { "value": "immediate", "text": "Gui ngay" }
                    ],
                    "sourceOptions": [
                      { "value": "admin_console", "text": "" },
                      { "value": "admin_console", "text": "Bang dieu khien admin" }
                    ],
                    "templates": [
                      { "communicationTemplateId": 101, "text": "" },
                      { "communicationTemplateId": 101, "text": "Template tao don email" }
                    ]
                  },
                  "templates": [
                    {
                      "communicationTemplateId": 101,
                      "templateName": "",
                      "eventType": "",
                      "channel": "",
                      "locale": "",
                      "body": "",
                      "isActive": false,
                      "version": 0,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "communicationTemplateId": 101,
                      "templateName": "Email tao don",
                      "eventType": "order_created",
                      "channel": "email",
                      "locale": "vi-VN",
                      "subject": "FreshFarm xac nhan don hang",
                      "body": "Noi dung email tao don",
                      "isActive": true,
                      "version": 3,
                      "lastEditedByUserId": 7,
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "policies": [
                    {
                      "notificationPolicyRuleId": 201,
                      "eventType": "",
                      "channel": "",
                      "audienceType": "",
                      "isEnabled": false,
                      "cooldownMinutes": 0,
                      "priority": 0,
                      "createdAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "notificationPolicyRuleId": 201,
                      "eventType": "order_created",
                      "channel": "email",
                      "audienceType": "buyer",
                      "communicationTemplateId": 101,
                      "templateName": "Email tao don",
                      "isEnabled": true,
                      "cooldownMinutes": 15,
                      "deliveryMode": "immediate",
                      "priority": 80,
                      "notes": "Uu tien gui ngay",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z"
                    }
                  ],
                  "preferences": [
                    {
                      "notificationPreferenceId": 301,
                      "userId": 0,
                      "eventType": "",
                      "channel": "",
                      "isOptedIn": false,
                      "source": "",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-20T00:00:00Z"
                    },
                    {
                      "notificationPreferenceId": 301,
                      "userId": 18,
                      "eventType": "order_created",
                      "channel": "email",
                      "isOptedIn": true,
                      "source": "admin_console",
                      "createdAt": "2026-03-20T00:00:00Z",
                      "updatedAt": "2026-03-24T01:00:00Z"
                    }
                  ]
                }
                """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CommunicationGovernancePageViewModel>(view.Model);
        var channel = Assert.Single(model.ChannelOptions);
        var @event = Assert.Single(model.EventOptions);
        var audience = Assert.Single(model.AudienceOptions);
        var deliveryMode = Assert.Single(model.DeliveryModeOptions);
        var source = Assert.Single(model.SourceOptions);
        var templateOption = Assert.Single(model.TemplateOptions);
        var template = Assert.Single(model.Templates);
        var policy = Assert.Single(model.Policies);
        var preference = Assert.Single(model.Preferences);

        Assert.Equal("Email", channel.Text);
        Assert.Equal("Don hang duoc tao", @event.Text);
        Assert.Equal("Nguoi mua", audience.Text);
        Assert.Equal("Gui ngay", deliveryMode.Text);
        Assert.Equal("Bang dieu khien admin", source.Text);

        Assert.Equal(101, templateOption.CommunicationTemplateId);
        Assert.Equal("Template tao don email", templateOption.Text);

        Assert.Equal(101, template.CommunicationTemplateId);
        Assert.Equal("Email tao don", template.TemplateName);
        Assert.Equal("order_created", template.EventType);
        Assert.Equal("email", template.Channel);
        Assert.Equal("vi-VN", template.Locale);
        Assert.Equal("FreshFarm xac nhan don hang", template.Subject);
        Assert.Equal("Noi dung email tao don", template.Body);
        Assert.True(template.IsActive);
        Assert.Equal(3, template.Version);
        Assert.Equal(7, template.LastEditedByUserId);

        Assert.Equal(201, policy.NotificationPolicyRuleId);
        Assert.Equal("order_created", policy.EventType);
        Assert.Equal("email", policy.Channel);
        Assert.Equal("buyer", policy.AudienceType);
        Assert.Equal(101, policy.CommunicationTemplateId);
        Assert.Equal("Email tao don", policy.TemplateName);
        Assert.True(policy.IsEnabled);
        Assert.Equal(15, policy.CooldownMinutes);
        Assert.Equal("immediate", policy.DeliveryMode);
        Assert.Equal(80, policy.Priority);
        Assert.Equal("Uu tien gui ngay", policy.Notes);

        Assert.Equal(301, preference.NotificationPreferenceId);
        Assert.Equal(18, preference.UserId);
        Assert.Equal("order_created", preference.EventType);
        Assert.Equal("email", preference.Channel);
        Assert.True(preference.IsOptedIn);
        Assert.Equal("admin_console", preference.Source);
        Assert.Equal(1, model.Stats.TotalTemplates);
    }

    private static CommunicationController CreateController(RecordingHttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ordering.test")
        };

        return new CommunicationController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext("7")
            }
        };
    }

    private static HttpContext CreateHttpContext(string userId)
    {
        var token = CreateAccessToken(userId);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("ff_access_token", token)
        ], "TestAuth"));
        return httpContext;
    }

    private static string CreateAccessToken(string userId)
    {
        var jwt = new JwtSecurityToken(
            issuer: "FreshFarm.Tests",
            audience: "FreshFarm.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId)
            ]);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _store.Keys;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
