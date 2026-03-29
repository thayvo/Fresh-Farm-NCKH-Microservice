using System.Net;
using System.Net.Mail;
using System.Text;
using FreshFarm.Identity.Api.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }

    Task SendPasswordResetEmailAsync(string toEmail, string? toName, string resetUrl, int expiresInMinutes, CancellationToken cancellationToken = default);

    Task SendEmailVerificationAsync(string toEmail, string? toName, string verifyUrl, int expiresInMinutes, CancellationToken cancellationToken = default);

    Task SendSellerApplicationReviewAsync(string toEmail, string? toName, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default);
}

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private static readonly Encoding Utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

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

        using var message = CreateMailMessage(
            toEmail,
            displayName,
            "FreshFarm - Đặt lại mật khẩu",
            $"""
            <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
            <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản FreshFarm của bạn.</p>
            <p>Vui lòng bấm vào liên kết dưới đây để tạo mật khẩu mới:</p>
            <p><a href="{safeResetUrl}">Đặt lại mật khẩu</a></p>
            <p>Liên kết này có hiệu lực trong {expiresInMinutes} phút.</p>
            <p>Nếu bạn không yêu cầu thao tác này, bạn có thể bỏ qua email này.</p>
            <p>Trân trọng,<br/>FreshFarm</p>
            """);

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

        using var message = CreateMailMessage(
            toEmail,
            displayName,
            "FreshFarm - Xác minh email",
            $"""
            <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
            <p>Cảm ơn bạn đã đăng ký tài khoản FreshFarm.</p>
            <p>Vui lòng bấm vào liên kết dưới đây để xác minh email trước khi đăng nhập:</p>
            <p><a href="{safeVerifyUrl}">Xác minh email</a></p>
            <p>Liên kết này có hiệu lực trong {expiresInMinutes} phút.</p>
            <p>Nếu bạn không tạo tài khoản này, bạn có thể bỏ qua email.</p>
            <p>Trân trọng,<br/>FreshFarm</p>
            """);

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

    public async Task SendSellerApplicationReviewAsync(string toEmail, string? toName, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP chưa được cấu hình đầy đủ.");
        }

        var displayName = string.IsNullOrWhiteSpace(toName) ? "bạn" : toName.Trim();
        var safeStoreName = string.IsNullOrWhiteSpace(storeName) ? "gian hàng của bạn" : WebUtility.HtmlEncode(storeName.Trim());
        var safeReviewNote = WebUtility.HtmlEncode(reviewNote?.Trim() ?? string.Empty);
        var subject = isApproved
            ? "FreshFarm - Hồ sơ người bán đã được duyệt"
            : "FreshFarm - Hồ sơ người bán cần bổ sung";
        var body = isApproved
            ? $"""
                <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
                <p>Hồ sơ đăng ký người bán cho <strong>{safeStoreName}</strong> đã được FreshFarm phê duyệt.</p>
                <p>Bạn có thể đăng nhập lại để tiếp tục thiết lập và vận hành gian hàng.</p>
                <p>Trân trọng,<br/>FreshFarm</p>
                """
            : $"""
                <p>Xin chào {WebUtility.HtmlEncode(displayName)},</p>
                <p>Hồ sơ đăng ký người bán cho <strong>{safeStoreName}</strong> hiện cần bổ sung thêm thông tin trước khi FreshFarm có thể phê duyệt.</p>
                <p>Lý do / ghi chú từ đội vận hành:</p>
                <p>{(string.IsNullOrWhiteSpace(safeReviewNote) ? "Vui lòng đăng nhập lại và kiểm tra phần hồ sơ người bán để cập nhật." : safeReviewNote)}</p>
                <p>Sau khi cập nhật, bạn có thể gửi lại hồ sơ để được rà soát tiếp.</p>
                <p>Trân trọng,<br/>FreshFarm</p>
                """;

        using var message = CreateMailMessage(toEmail, displayName, subject, body);

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.UserName, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _logger.LogInformation(
            "Đang gửi email ket qua review seller toi {Email} qua SMTP host {Host}. Approved={Approved}",
            toEmail,
            _options.Host,
            isApproved);
        cancellationToken.ThrowIfCancellationRequested();
        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    internal MailMessage CreateMailMessage(string toEmail, string? toName, string subject, string body, bool isBodyHtml = true)
    {
        var message = new MailMessage
        {
            From = CreateMailAddress(_options.FromEmail, _options.FromName),
            Subject = subject,
            SubjectEncoding = Utf8Encoding,
            Body = body,
            BodyEncoding = Utf8Encoding,
            HeadersEncoding = Utf8Encoding,
            IsBodyHtml = isBodyHtml
        };

        message.To.Add(CreateMailAddress(toEmail, toName));
        return message;
    }

    private static MailAddress CreateMailAddress(string email, string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? new MailAddress(email)
            : new MailAddress(email, displayName.Trim(), Utf8Encoding);
    }
}
