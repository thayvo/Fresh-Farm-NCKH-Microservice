namespace FreshFarm.Web.Bff.Services;

public interface IVnPayService
{
    bool IsConfigured { get; }

    string CreatePaymentUrl(VnPayCreatePaymentRequest request);

    VnPayReturnValidationResult ValidateReturn(IQueryCollection query);
}
