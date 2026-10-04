using System.Security.Claims;

namespace FreshFarm.Web.Bff.Utilities;

public static class AuthLaneResolver
{
    public static string ResolveLoginPath(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return "/account/signin";
        }

        if (user.IsInRole("Admin"))
        {
            return "/Admin/AdminAccount/Login";
        }

        if (user.IsInRole("Seller"))
        {
            return "/Seller/SellerAccount/Login";
        }

        return "/account/signin";
    }
}
