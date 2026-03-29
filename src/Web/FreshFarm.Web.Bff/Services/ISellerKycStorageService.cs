using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Services;

public interface ISellerKycStorageService
{
    (bool isValid, string errorMessage) Validate(IFormFile file, bool allowPdf = false);

    string Save(IFormFile file, string prefix, bool allowPdf = false);

    void Delete(string? requestPath);
}
