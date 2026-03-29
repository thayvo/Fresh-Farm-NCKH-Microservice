using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Web.Bff.Areas.Admin.Controllers;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class UserControllerTests
{
    [Fact]
    public async Task ManageUsers_DedupesDuplicateUsersAndRoles_FromIdentity()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/admin/users")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      {
                        "userId": 18,
                        "userName": "",
                        "fullName": "",
                        "email": "",
                        "phone": "",
                        "roleId": 2,
                        "created": "2026-03-24T00:00:00Z"
                      },
                      {
                        "userId": 18,
                        "userName": "seller18",
                        "fullName": "Seller 18",
                        "email": "seller18@example.com",
                        "phone": "0912345678",
                        "roleId": 2,
                        "role": {
                          "roleId": 2,
                          "roleName": "Seller",
                          "isActive": true
                        },
                        "isActive": true,
                        "created": "2026-03-24T00:00:00Z",
                        "updated": "2026-03-24T01:00:00Z"
                      }
                    ]
                    """)
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/admin/users/roles")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    [
                      { "roleId": 2, "roleName": "", "isActive": false },
                      { "roleId": 2, "roleName": "Seller", "isActive": true }
                    ]
                    """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var controller = CreateController(handler);

        var result = await controller.ManageUsers(userType: "seller");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminUserManagementPageViewModel>(view.Model);
        var user = Assert.Single(model.Users);
        var role = Assert.Single(model.Roles);

        Assert.Equal(18, user.AdminID);
        Assert.Equal("seller18", user.UserName);
        Assert.Equal("Seller 18", user.FullName);
        Assert.Equal("seller18@example.com", user.Email);
        Assert.Equal("Seller", user.Role?.RoleName);

        Assert.Equal(2, role.RoleID);
        Assert.Equal("Seller", role.RoleName);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static UserController CreateController(RecordingHttpMessageHandler handler)
    {
        var token = CreateAccessToken("7");
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://identity.test")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim("ff_access_token", token)
        ], "TestAuth"));

        return new UserController(new StaticHttpClientFactory(client))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
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

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(responder(request));
        }
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
