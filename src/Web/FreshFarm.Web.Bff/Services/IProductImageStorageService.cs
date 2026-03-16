using Microsoft.AspNetCore.Http;

namespace FreshFarm.Web.Bff.Services;

public interface IProductImageStorageService
{
    (bool isValid, string errorMessage) Validate(IFormFile file);

    string Save(IFormFile file);

    void Delete(string? imageFileName);
}
