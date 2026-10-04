using System.Text;
using FreshFarm.Identity.Api.Options;
using FreshFarm.Identity.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FreshFarm.Identity.Api.Tests;

public sealed class AccountEmailSenderTests
{
    [Fact]
    public void CreateMailMessage_UsesUtf8Encoding_ForVietnameseSubjectBodyAndDisplayNames()
    {
        var sender = new SmtpAccountEmailSender(
            Microsoft.Extensions.Options.Options.Create(new SmtpOptions
            {
                Host = "smtp.gmail.com",
                Port = 587,
                UserName = "freshfarm@example.com",
                Password = "secret",
                FromEmail = "freshfarm@example.com",
                FromName = "Trang web FreshFarm",
                EnableSsl = true
            }),
            NullLogger<SmtpAccountEmailSender>.Instance);

        using var message = sender.CreateMailMessage(
            "buyer@example.com",
            "Bùi Khắc Vinh",
            "FreshFarm - Hồ sơ người bán cần bổ sung",
            "<p>Vui lòng bổ sung ảnh CCCD mặt sau rõ nét.</p>");
        var fromAddress = Assert.IsType<System.Net.Mail.MailAddress>(message.From);

        Assert.Equal(Encoding.UTF8.WebName, message.SubjectEncoding?.WebName);
        Assert.Equal(Encoding.UTF8.WebName, message.BodyEncoding?.WebName);
        Assert.Equal(Encoding.UTF8.WebName, message.HeadersEncoding?.WebName);
        Assert.Equal("Trang web FreshFarm", fromAddress.DisplayName);
        Assert.Equal("Bùi Khắc Vinh", message.To[0].DisplayName);
        Assert.Equal("FreshFarm - Hồ sơ người bán cần bổ sung", message.Subject);
        Assert.Contains("bổ sung ảnh CCCD", message.Body);
    }
}
