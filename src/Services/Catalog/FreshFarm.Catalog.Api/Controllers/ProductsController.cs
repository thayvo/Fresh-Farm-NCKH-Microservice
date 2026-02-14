using FreshFarm.Catalog.Api.Dtos;
using FreshFarm.Catalog.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Security.Claims;
namespace FreshFarm.Catalog.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly FreshFarmCatalogDBContext _db;
        public ProductsController(FreshFarmCatalogDBContext db)
        {
            _db = db;
        }
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string ?Name)
        {
           var query = _db.Products
                .AsNoTracking()
                .Include(p=>p.Category)
                .Include(p=>p.Unit)
                .AsQueryable();
            if (!String.IsNullOrWhiteSpace(Name))
            {
                query = query.Where((p=> p.ProductName.Contains(Name)));
            }
            var result = await query
                .Select(p=> new
                {
                    ProductID = p.ProductId,
                    ProductName = p.ProductName,
                    Price = p.Price,
                    CategoryName = p.Category.CategoryName,
                    UnitName = p.Unit.UnitName
                }).ToListAsync();
            return Ok(result);
        }
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById([FromRoute] int id)
        {
            var item = await _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Unit)
                .Where(p => p.ProductId == id)
                .Select(p=> new
                {
                    ProductID = p.ProductId,
                    ProductName = p.ProductName,
                    Price = p.Price,
                    CategoryName = p.Category.CategoryName,
                    UnitName = p.Unit.UnitName
                }).FirstOrDefaultAsync();
            if (item == null) return NotFound();
            return Ok(item);
        }
        [Authorize(Roles ="Seller")]
        [HttpPost]
        public async  Task<IActionResult> Create([FromBody] CreateProductRequest request)
        {
            var sellerUserId =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if(string.IsNullOrEmpty(sellerUserId))
            {
                return Unauthorized("Người bán không hợp lệ");
            }
            var product = new Product
            {
                CategoryId = request.CategoryId,
                UnitId = request.UnitId,
                ProductName = request.ProductName,
                Price = request.Price,
                Sku = string.IsNullOrEmpty(request.Sku) ? null : request.Sku.Trim(),

                Status = true,
                StockQuantity = 0,
                ImageFileName = null,
                ShortDescription = "",
                LongDescription = "",
                NearExpiryDays = null,
                IsManuallyDisabled = false,
                CreatedDate = DateTime.UtcNow
            };
            _db.Products.Add(product);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new {detail = ex.Message});
            }
            return CreatedAtAction(nameof(GetById), new { id = product.ProductId }, new { product.ProductId });
        }
        
    }
}
