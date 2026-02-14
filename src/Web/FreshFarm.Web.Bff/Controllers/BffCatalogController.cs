using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

namespace FreshFarm.Web.Bff.Controllers
{
    [ApiController] //dùng cho api, auto binding, auto 400, auto validation
    [Route("bff")] // đường dẫn bắt đầu từ bff
    public class BffCatalogController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        //Tạo constructure để làm nhiệm vụ là yêu cầu hệ thống cấp cho một  nhà máy tạo HttpClient, lấy từ ở trong program.cs thông qua DI(Dependency Injection)
        //=> thay cho {}
        public BffCatalogController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;
        
        [HttpGet("products")] //Tạo hàm get cho products nó sẽ chạy theo url đầy đủ là /bff/products
        public async Task<IActionResult> GetProducts([FromQuery] string? name) //dùng fromquery để lấy tham số name từ query string
        {
            var client = _httpClientFactory.CreateClient("Catalog"); //Tạo một HttpClient từ nhà máy tọa HttpClientFactory đã được cấu hình trong program.cs với tên là "Catlog"
            var url = string.IsNullOrWhiteSpace(name) //Kiểm tra nếu name là rỗng thì gọi api/products không có tham số, ngược lại thì thêm tham số name vào query string(đã mã hóa để tránh lỗi kí tự đặc biệt)
                ? "/api/products" 
                : $"/api/products?name={Uri.EscapeDataString(name)}"; 
            var auth = Request.Headers.Authorization.ToString();//Lấy header Authorization từ request gốc gửi đến BFF
            if (!String.IsNullOrEmpty(auth)) // Nếu header Authorization không rỗng thì thêm header này vào request gửi dến dịch vụ catalog
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth); //thêm header authorization vào httpclient mà không kiểm tra tính hợp lệ của header
            }
            var resp = await client.GetAsync(url); //Gửi yêu cầu GET đến dịch vụ catalog với url đã xây dựng
            var content = await resp.Content.ReadAsStringAsync(); //Đọc nội dung phản hồi từ dịch vụ catalog dưới dạng chuỗi
            return Content(content, "application/json"); //trả về nội dung nhận được từ dịch vu catalog với kiểu nội dung là application/json
        }
        //không cấu hình auth với seller ở trong này bỏi vì chúng ta sẽ chuyển tiếp thông tin này qua catalog để catalog xử lí mà thôi

        [HttpPost("products")] // tạo hàm post để tạo sản phẩm mới với url đầy đủ là /bff/products
        public async Task<IActionResult> CreateProduct([FromBody] object request)
        {
            var client = _httpClientFactory.CreateClient("Catalog"); //Tạo một httpclient từ nhà máy tạo httpclientfactory đã được cấu hình trong program.cs với tên là 'catalog'
            //Forward jwt nếu client có gửi(để catalog tự kiểm role seller)
            var auth = Request.Headers.Authorization.ToString(); //dòng này lấy header authorization từ http request và chuyển nó thành chuỗi
            if (!string.IsNullOrWhiteSpace(auth)) // nếu chuỗi auth không rỗng hoặc không chỉ chứa khoảng trắng 
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", auth); //thêm header authorization vào httpclient mà không kiểm tra tính hợp lệ của header
            var resp = await client.PostAsJsonAsync("/api/products", request); //gủi yêu cầu post đến dịch vụ catalog với url là api/products và nộ dung request đã được truyền vào 
            var body = await resp.Content.ReadAsStringAsync(); // đọc nội dung phản hồi từ dịch vụ catalog dưới dạng chuỗi
            return StatusCode((int)resp.StatusCode, body); // trả về mã trạng thái và nội dung nhận được từ dịch vụ catalog
    }

    }
}
