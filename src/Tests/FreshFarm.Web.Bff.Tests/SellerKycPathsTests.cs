using FreshFarm.Web.Bff.Services;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class SellerKycPathsTests
{
    [Theory]
    [InlineData("/uploads/seller-kyc/../appsettings.json")]
    [InlineData("/uploads/seller-kyc/subfolder/document.pdf")]
    [InlineData("/uploads/seller-kyc/%2e%2e/appsettings.json")]
    public void NormalizeStoredFileName_RejectsTraversalAndNestedPaths(string storedReference)
    {
        Assert.Null(SellerKycPaths.NormalizeStoredFileName(storedReference));
    }

    [Theory]
    [InlineData("seller-kyc:cccd-front-0123456789abcdef.jpg", "/uploads/seller-kyc/cccd-front-0123456789abcdef.jpg")]
    [InlineData("seller-kyc:business-license-0123456789abcdef.pdf", "seller-kyc:business-license-0123456789abcdef.pdf")]
    public void AreEquivalentStoredReferences_AcceptsOpaqueAndLegacyReferences(
        string firstReference,
        string secondReference)
    {
        Assert.True(SellerKycPaths.AreEquivalentStoredReferences(firstReference, secondReference));
    }

    [Theory]
    [InlineData("seller-kyc:cccd-front-0123456789abcdef.jpg", "seller-kyc:cccd-back-0123456789abcdef.jpg")]
    [InlineData("seller-kyc:cccd-front-0123456789abcdef.jpg", "/uploads/products/cccd-front-0123456789abcdef.jpg")]
    public void AreEquivalentStoredReferences_RejectsDifferentOrUnsupportedReferences(
        string firstReference,
        string secondReference)
    {
        Assert.False(SellerKycPaths.AreEquivalentStoredReferences(firstReference, secondReference));
    }
}
