namespace FreshFarm.Web.Bff.Services;

public sealed class SellerKycStoredFile
{
    public SellerKycStoredFile(Stream content, string contentType)
    {
        Content = content;
        ContentType = contentType;
    }

    public Stream Content { get; }

    public string ContentType { get; }
}
