namespace FreshFarm.Web.Bff.Services;

public interface ISignUpCaptchaService
{
    string GenerateCode(int length = 5);
    string BuildSvg(string captchaCode);
    bool Matches(string? expectedCode, string? submittedCode);
}
