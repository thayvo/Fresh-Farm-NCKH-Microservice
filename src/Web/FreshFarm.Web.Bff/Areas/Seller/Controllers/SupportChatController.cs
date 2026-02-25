using System;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using FreshFram.Models;
using FreshFram.Services;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class SupportChatController : LegacySellerControllerBase
    {
        private readonly FreshFarmDBEntities db = new FreshFarmDBEntities();
        private readonly ChatService _chatService;

        public SupportChatController()
        {
            _chatService = new ChatService();
        }

        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Conversations()
        {
            var conversations = _chatService.GetConversationsForAdmin();
            return Json(new { ok = true, conversations });
        }

        [HttpGet]
        public ActionResult Messages(int conversationId, int take = 100)
        {
            var messages = _chatService.GetMessages(conversationId, take);
            return Json(new { ok = true, messages });
        }

        [HttpGet]
        public ActionResult ConversationDetails(int conversationId)
        {
            var conv = db.SupportConversations
                         .Include("User.RankLevel")
                         .FirstOrDefault(c => c.ConversationId == conversationId);

            if (conv == null)
            {
                return Json(new { ok = false, message = "Không tìm thấy hội thoại." });
            }

            var user = conv.User;
            object profile = null;

            if (user != null)
            {
                profile = new
                {
                    userId = user.UserID,
                    fullName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName,
                    email = user.Email,
                    phone = user.Phone,
                    avatarUrl = Url.Action("AvatarById", "Account", new { id = user.UserID, area = "" }),
                    totalPoints = user.TotalPoints,
                    rankName = user.RankLevel != null
                        ? (string.IsNullOrWhiteSpace(user.RankLevel.LevelName)
                            ? (user.RankLevel.RankType != null ? user.RankLevel.RankType.RankTypeName : null)
                            : user.RankLevel.LevelName)
                        : null
                };
            }

            var orders = new System.Collections.Generic.List<object>();
            if (user != null)
            {
                orders = db.Orders
                           .Where(o => o.UserID == user.UserID)
                           .OrderByDescending(o => o.OrderDate)
                           .Take(5)
                           .ToList()
                           .Select(o => new
                           {
                               orderId = o.OrderID,
                               orderCode = "#" + o.OrderID.ToString("D6"),
                               orderDate = o.OrderDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                               totalAmount = o.TotalAmount,
                               status = o.Status
                           }).ToList<object>();
            }

            return Json(new { ok = true, profile, orders });
        }

        [HttpPost]
        public ActionResult Close(int conversationId)
        {
            _chatService.CloseConversation(conversationId);
            return Json(new { ok = true });
        }

        [HttpPost]
        public ActionResult MarkAsRead(int conversationId)
        {
            _chatService.MarkAsRead(conversationId, true); // true = admin is reading
            return Json(new { ok = true });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
                _chatService.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
