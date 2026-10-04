namespace FreshFarm.Web.Bff.Options;

public sealed class SellerKycStorageOptions
{
    public const string SectionName = "Storage:SellerKyc";

    public string RootPath { get; set; } = "App_Data/SellerKyc";
}
