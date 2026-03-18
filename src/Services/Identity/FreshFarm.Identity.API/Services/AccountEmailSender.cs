using System.Net;
using System.Net.Mail;
using FreshFarm.Identity.Api.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }

    Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default);

    Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default);
}

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpAccountEmailSender> _logger;

    public SmtpAccountEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpAccountEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Host) &&
        !string.IsNullOrWhiteSpace(_options.UserName) &&
        !string.IsNullOrWhiteSpace(_options.Password) &&
        !string.IsNullOrWhiteSpace(_options.FromEmail);

    public async Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP chưa được cấu hình đầy đủ.");
        }

        var displayName = string.IsNullOrWhiteSpace(toName) ? "bạn" : toName.Trim();
        var safeResetUrl = WebUtility.HtmlEncode(resetUrl);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = "FreshFarm - Đặt lại mật khẩu",
            Body = $"""
                <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
                <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản FreshFarm của bạn.</p>
                <p>Vui lòng bấm vào liên kết dưới đây để tạo mật khẩu mới:</p>
                <p><a href="{safeResetUrl}">Đặt lại mật khẩu</a></p>
                <p>Liên kết này có hiệu lực trong {expiresInMinutes} phút.</p>
                <p>Nếu bạn không yêu cầu thao tác này, bạn có thể bỏ qua email này.</p>
                <p>Trân trọng,<br/>FreshFarm</p>
                """,
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(toEmail, displayName));

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.UserName, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _logger.LogInformation("Đang gửi email đặt lại mật khẩu tới {Email} qua SMTP host {Host}.", toEmail, _options.Host);
        cancellationToken.ThrowIfCancellationRequested();
        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    public async Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP chưa được cấu hình đầy đủ.");
        }

        var displayName = string.IsNullOrWhiteSpace(toName) ? "bạn" : toName.Trim();
        var safeVerifyUrl = WebUtility.HtmlEncode(verifyUrl);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = "FreshFarm - Xác minh email",
            Body = $"""
                <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
                <p>Cảm ơn bạn đã đăng ký tài khoản FreshFarm.</p>
                <p>Vui lòng bấm vào liên kết dưới đây để xác minh email trước khi đăng nhập:</p>
                <p><a href="{safeVerifyUrl}">Xác minh email</a></p>
                <p>Liên kết này có hiệu lực trong {expiresInMinutes} phút.</p>
                <p>Nếu bạn không tạo tài khoản này, bạn có thể bỏ qua email.</p>
                <p>Trân trọng,<br/>FreshFarm</p>
                """,
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(toEmail, displayName));

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.UserName, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _logger.LogInformation("Đang gửi email xác minh tài khoản tới {Email} qua SMTP host {Host}.", toEmail, _options.Host);
        cancellationToken.ThrowIfCancellationRequested();
        await smtpClient.SendMailAsync(message, cancellationToken);
    }
}
