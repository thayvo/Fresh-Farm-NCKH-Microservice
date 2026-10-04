using System.Security.Cryptography;
using System.Text;
using FreshFarm.Identity.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Infrastructure;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalBffOnlyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<InternalBffOptions>>()
            .Value;

        if (!options.IsConfigured)
        {
            context.Result = new ObjectResult(new
            {
                message = "Kênh xác thực nội bộ giữa Web BFF và Identity chưa được cấu hình."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return Task.CompletedTask;
        }

        var suppliedKey = context.HttpContext.Request.Headers[options.HeaderName].ToString();
        if (!FixedTimeEquals(suppliedKey, options.SharedKey))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "Yêu cầu xác thực không hợp lệ."
            });
        }

        return Task.CompletedTask;
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
