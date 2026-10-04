using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class BotChallengeServiceTests
{
    [Fact]
    public async Task VerifyAsync_DoesNotDowngradeToRecaptcha_WhenConfiguredTurnstileRejectsToken()
    {
        var turnstile = new FakeTurnstileService
        {
            Result = new BotChallengeVerificationResult
            {
                Success = false,
                Provider = BotChallengeProvider.CloudflareTurnstile,
                ErrorMessage = "Rejected"
            }
        };
        var recaptcha = new FakeGoogleRecaptchaService();
        var service = CreateService(
            "CloudflareTurnstile",
            Environments.Production,
            turnstile,
            recaptcha);

        var result = await service.VerifyAsync(new BotChallengeVerificationRequest
        {
            Purpose = BotChallengePurpose.SignUp,
            Token = "token"
        });

        Assert.False(result.Success);
        Assert.Equal(1, turnstile.CallCount);
        Assert.Equal(0, recaptcha.CallCount);
    }

    [Fact]
    public async Task VerifyAsync_UsesRecaptcha_WhenItIsTheExplicitFallbackProvider()
    {
        var turnstile = new FakeTurnstileService();
        var recaptcha = new FakeGoogleRecaptchaService();
        var service = CreateService(
            "GoogleRecaptcha",
            Environments.Production,
            turnstile,
            recaptcha);

        var result = await service.VerifyAsync(new BotChallengeVerificationRequest
        {
            Purpose = BotChallengePurpose.ForgotPassword,
            Token = "recaptcha-token"
        });

        Assert.True(result.Success);
        Assert.Equal(BotChallengeProvider.GoogleRecaptcha, result.Provider);
        Assert.Equal("forgot_password", recaptcha.LastExpectedAction);
        Assert.Equal(0, turnstile.CallCount);
        Assert.Equal(1, recaptcha.CallCount);
    }

    [Fact]
    public void GetPresentation_FailsClosed_WhenLocalSvgIsSelectedOutsideDevelopment()
    {
        var service = CreateService(
            "LocalSvg",
            Environments.Production,
            new FakeTurnstileService(),
            new FakeGoogleRecaptchaService());

        var presentation = service.GetPresentation(BotChallengePurpose.SignUp);

        Assert.False(presentation.IsAvailable);
        Assert.Equal(BotChallengeProvider.Unavailable, presentation.Provider);
    }

    [Fact]
    public async Task VerifyAsync_UsesLocalSvgOnlyInDevelopment()
    {
        var localCaptcha = new FakeLocalCaptchaService();
        var service = CreateService(
            "LocalSvg",
            Environments.Development,
            new FakeTurnstileService(),
            new FakeGoogleRecaptchaService(),
            localCaptcha);

        var result = await service.VerifyAsync(new BotChallengeVerificationRequest
        {
            Purpose = BotChallengePurpose.SignUp,
            ExpectedLocalCode = "ABCDE",
            SubmittedLocalCode = "abcde"
        });

        Assert.True(result.Success);
        Assert.Equal(BotChallengeProvider.LocalSvg, result.Provider);
        Assert.Equal(1, localCaptcha.MatchCallCount);
    }

    private static BotChallengeService CreateService(
        string provider,
        string environmentName,
        FakeTurnstileService turnstile,
        FakeGoogleRecaptchaService recaptcha,
        FakeLocalCaptchaService? localCaptcha = null)
        => new(
            Microsoft.Extensions.Options.Options.Create(new BotChallengeOptions
            {
                Provider = provider,
                SignUpAction = "signup",
                ForgotPasswordAction = "forgot_password",
                AllowLocalSvgInDevelopment = true
            }),
            Microsoft.Extensions.Options.Options.Create(new CloudflareTurnstileOptions
            {
                SiteKey = "turnstile-site-key",
                SecretKey = "turnstile-secret",
                AllowedHostnames = ["app.example.com"]
            }),
            Microsoft.Extensions.Options.Options.Create(new GoogleRecaptchaOptions
            {
                SiteKey = "recaptcha-site-key",
                SecretKey = "recaptcha-secret",
                AllowedHostnames = ["app.example.com"]
            }),
            turnstile,
            recaptcha,
            localCaptcha ?? new FakeLocalCaptchaService(),
            new FakeHostEnvironment { EnvironmentName = environmentName });

    private sealed class FakeTurnstileService : ICloudflareTurnstileService
    {
        public bool IsConfigured { get; set; } = true;

        public int CallCount { get; private set; }

        public BotChallengeVerificationResult Result { get; set; } = new()
        {
            Success = true,
            Provider = BotChallengeProvider.CloudflareTurnstile,
            Action = "signup",
            Hostname = "app.example.com"
        };

        public Task<BotChallengeVerificationResult> VerifyAsync(
            string token,
            string expectedAction,
            string? remoteIp,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeGoogleRecaptchaService : IGoogleRecaptchaService
    {
        public bool IsConfigured { get; set; } = true;

        public int CallCount { get; private set; }

        public string? LastExpectedAction { get; private set; }

        public Task<GoogleRecaptchaVerificationResult> VerifyAsync(
            string token,
            string expectedAction,
            string? remoteIp,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastExpectedAction = expectedAction;
            return Task.FromResult(new GoogleRecaptchaVerificationResult
            {
                Success = true,
                Score = 1m,
                Action = expectedAction
            });
        }
    }

    private sealed class FakeLocalCaptchaService : ISignUpCaptchaService
    {
        public int MatchCallCount { get; private set; }

        public string GenerateCode(int length = 5) => "ABCDE";

        public string BuildSvg(string captchaCode) => "<svg></svg>";

        public bool Matches(string? expectedCode, string? submittedCode)
        {
            MatchCallCount++;
            return string.Equals(expectedCode, submittedCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "FreshFarm.Web.Bff.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
