namespace FreshFarm.Ordering.Api.Dtos;

public sealed class CartItemResponse
{
    public int ProductId { get; set; }
    public int SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string UnitSymbol { get; set; } = "don vi";
    public int Quantity { get; set; }
}

public sealed class CartResponse
{
    public List<CartItemResponse> Items { get; set; } = new();
}

public sealed class UpsertCartItemRequest
{
    public int ProductId { get; set; }
    public int SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ImageFileName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string UnitSymbol { get; set; } = "don vi";
    public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartItemQuantityRequest
{
    public int ProductId { get; set; }
    public int SellerId { get; set; }
    public int Quantity { get; set; }
}

public sealed class RemoveCartItemRequest
{
    public int ProductId { get; set; }
    public int SellerId { get; set; }
}

public sealed class ReplaceCartRequest
{
    public List<UpsertCartItemRequest> Items { get; set; } = new();
}
