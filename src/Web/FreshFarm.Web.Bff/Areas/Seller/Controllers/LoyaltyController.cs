using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFram.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Mvc.Area("Seller")]
    public class LoyaltyController : LegacySellerControllerBase
    {
        private readonly FreshFarmDBEntities db = new FreshFarmDBEntities();

        // Dashboard: KPIs + top users + recent activities
        [HttpGet]
        public ActionResult Index()
        {
            var now = DateTime.Now;
            int year = now.Year;
            byte quarter = (byte)((now.Month - 1) / 3 + 1);

            // Totals from history
            var totalEarned = db.LoyaltyPointHistories
                .Where(h => h.Direction == "EARN")
                .Select(h => (int?)h.Points)
                .Sum() ?? 0;
            var totalRedeemed = db.LoyaltyPointHistories
                .Where(h => h.Direction == "REDEEM")
                .Select(h => (int?)h.Points)
                .Sum() ?? 0;

            // Users with any points
            var usersWithPoints = db.Users.Count(u => u.TotalPoints > 0);

            // Top users of current quarter
            var topQuarter = (from rq in db.UserRankQuarters
                              join u in db.Users on rq.UserID equals u.UserID
                              where rq.Year == year && rq.Quarter == quarter
                              orderby rq.RankPoints descending, u.FullName ascending
                              select new LoyaltyTopUserVM
                              {
                                  UserID = u.UserID,
                                  FullName = u.FullName,
                                  TotalPoints = u.TotalPoints,
                                  QuarterPoints = rq.RankPoints
                              }).Take(10).ToList();

            // Map ranks by active RankLevels and quarter points
            var rankLevels = db.RankLevels.Where(r => r.IsActive).ToList();
            foreach (var t in topQuarter)
            {
                var pts = t.QuarterPoints;
                var lvl = rankLevels
                    .Where(l => l.MinPoints <= pts && (!l.MaxPoints.HasValue || l.MaxPoints.Value >= pts))
                    .OrderByDescending(l => l.MinPoints)
                    .FirstOrDefault();
                t.RankName = lvl?.LevelName;
            }

            // Recent activities (top 10)
            var recent = (from h in db.LoyaltyPointHistories
                          join u in db.Users on h.UserID equals u.UserID
                          orderby h.CreatedAt descending
                          select new LoyaltyHistoryRowVM
                          {
                              CreatedAt = h.CreatedAt,
                              UserID = h.UserID,
                              UserName = u.FullName,
                              OrderID = h.OrderID,
                              Points = h.Points,
                              Direction = h.Direction,
                              Reason = h.Reason
                          }).Take(10).ToList();

            var model = new LoyaltyDashboardVM
            {
                TotalEarned = totalEarned,
                TotalRedeemed = -totalRedeemed, // REDEEM entries should be negative; display as positive total used
                UsersWithPoints = usersWithPoints,
                CurrentYear = year,
                CurrentQuarter = quarter,
                TopQuarterUsers = topQuarter,
                RecentActivities = recent
            };

            return View(model);
        }

        // Users listing with search + paging
        [HttpGet]
        public ActionResult Users(string q = null, int page = 1, int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            // Use read-only summary view when available; fallback to Users
            var vq = db.vw_Loyalty_UserSummary.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                vq = vq.Where(x => x.FullName.Contains(term));
            }

            var total = vq.Count();
            var data = vq.OrderByDescending(x => x.CurrentQuarterPoints ?? 0)
                         .ThenByDescending(x => x.TotalPoints)
                         .Skip((page - 1) * pageSize)
                         .Take(pageSize)
                         .ToList();

            var model = new LoyaltyUsersVM
            {
                Query = q,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Rows = data.Select(x => new LoyaltyUserRowVM
                {
                    UserID = x.UserID,
                    FullName = x.FullName,
                    TotalPoints = x.TotalPoints,
                    CurrentQuarterPoints = x.CurrentQuarterPoints ?? 0
                }).ToList()
            };

            // Assign rank names based on quarter points and active RankLevels
            var rankLevels = db.RankLevels.Where(r => r.IsActive).ToList();
            foreach (var row in model.Rows)
            {
                var pts = row.CurrentQuarterPoints;
                var lvl = rankLevels
                    .Where(l => l.MinPoints <= pts && (!l.MaxPoints.HasValue || l.MaxPoints.Value >= pts))
                    .OrderByDescending(l => l.MinPoints)
                    .FirstOrDefault();
                row.RankName = lvl?.LevelName;
            }
            return View(model);
        }

        // History listing
        [HttpGet]
        public ActionResult History(int? userId = null, string direction = null, DateTime? start = null, DateTime? end = null, int page = 1, int pageSize = 20)
        {
            var q = db.LoyaltyPointHistories.AsNoTracking().AsQueryable();
            if (userId.HasValue) q = q.Where(h => h.UserID == userId.Value);
            if (!string.IsNullOrWhiteSpace(direction)) q = q.Where(h => h.Direction == direction);
            if (start.HasValue) q = q.Where(h => h.CreatedAt >= start.Value);
            if (end.HasValue) q = q.Where(h => h.CreatedAt < end.Value.AddDays(1));

            var total = q.Count();
            var rows = (from h in q
                        join u in db.Users on h.UserID equals u.UserID
                        orderby h.CreatedAt descending
                        select new LoyaltyHistoryRowVM
                        {
                            CreatedAt = h.CreatedAt,
                            UserID = h.UserID,
                            UserName = u.FullName,
                            OrderID = h.OrderID,
                            Points = h.Points,
                            Direction = h.Direction,
                            Reason = h.Reason
                        })
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

            var model = new LoyaltyHistoryVM
            {
                UserID = userId,
                Direction = direction,
                Start = start,
                End = end,
                Page = page,
                PageSize = pageSize,
                Total = total,
                Rows = rows
            };
            return View(model);
        }

        // Config GET
        [HttpGet]
        public ActionResult Config()
        {
            var cfg = db.LoyaltyConfigs.OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
            var model = new LoyaltyConfigVM
            {
                EarnRate = cfg?.EarnRate ?? 0.01m,
                IncludeShippingFee = cfg?.IncludeShippingFee ?? false,
                PaidKeywords = cfg?.PaidKeywords ?? "Đã thanh toán;Da thanh toan",
                UpdatedAt = cfg?.UpdatedAt
            };
            return View(model);
        }

        // Config POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Config(LoyaltyConfigVM input)
        {
            if (input == null) return RedirectToAction("Config");
            if (input.EarnRate < 0) ModelState.AddModelError("EarnRate", "EarnRate không hợp lệ");
            if (!ModelState.IsValid) return View(input);

            var cfg = db.LoyaltyConfigs.OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
            if (cfg == null)
            {
                cfg = new LoyaltyConfig
                {
                    EarnRate = input.EarnRate,
                    IncludeShippingFee = input.IncludeShippingFee,
                    PaidKeywords = input.PaidKeywords ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                db.LoyaltyConfigs.Add(cfg);
            }
            else
            {
                cfg.EarnRate = input.EarnRate;
                cfg.IncludeShippingFee = input.IncludeShippingFee;
                cfg.PaidKeywords = input.PaidKeywords ?? string.Empty;
                cfg.UpdatedAt = DateTime.Now;
                db.Entry(cfg).State = EntityState.Modified;
            }
            db.SaveChanges();
            TempData["SuccessMessage"] = "Đã lưu cấu hình tích điểm.";
            return RedirectToAction("Config");
        }

        // Manual adjust points (add/subtract), optional: affect quarter rank
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AdjustPoints(int userId, int points, string reason, bool includeInRank = false)
        {
            try
            {
                var user = db.Users.FirstOrDefault(u => u.UserID == userId);
                if (user == null) return Json(new { success = false, message = "Không tìm thấy người dùng." });

                user.TotalPoints = user.TotalPoints + points;
                db.LoyaltyPointHistories.Add(new LoyaltyPointHistory
                {
                    UserID = userId,
                    OrderID = null,
                    Points = points,
                    Direction = "ADJUST",
                    Reason = string.IsNullOrWhiteSpace(reason) ? "Điều chỉnh thủ công" : reason,
                    CreatedAt = DateTime.Now
                });

                if (includeInRank && points != 0)
                {
                    var now = DateTime.Now;
                    int year = now.Year; byte q = (byte)((now.Month - 1) / 3 + 1);
                    var rq = db.UserRankQuarters.FirstOrDefault(x => x.UserID == userId && x.Year == year && x.Quarter == q);
                    if (rq == null)
                    {
                        rq = new UserRankQuarter { UserID = userId, Year = year, Quarter = q, RankPoints = points, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
                        db.UserRankQuarters.Add(rq);
                    }
                    else
                    {
                        rq.RankPoints += points;
                        rq.UpdatedAt = DateTime.Now;
                        db.Entry(rq).State = EntityState.Modified;
                    }
                }

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Sync award for eligible delivered+paid orders in range
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SyncAward(DateTime? start, DateTime? end)
        {
            try
            {
                var q = db.Orders.AsQueryable();
                if (start.HasValue) q = q.Where(o => o.OrderDate >= start.Value);
                if (end.HasValue) q = q.Where(o => o.OrderDate < end.Value.AddDays(1));

                // Delivered only, not yet awarded
                var orderIds = q.Where(o => o.Status == "Delivered" && o.PointsEarned == 0)
                                .Select(o => o.OrderID)
                                .ToList();

                int ok = 0; int fail = 0;
                foreach (var id in orderIds)
                {
                    try
                    {
                        // SP is idempotent and will only award if paid & delivered
                        db.usp_Loyalty_AwardPointsIfEligible(id);
                        ok++;
                    }
                    catch { fail++; }
                }
                return Json(new { success = true, ok, fail });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }

    // ====== ViewModels ======
    public class LoyaltyDashboardVM
    {
        public int TotalEarned { get; set; }
        public int TotalRedeemed { get; set; }
        public int UsersWithPoints { get; set; }
        public int CurrentYear { get; set; }
        public byte CurrentQuarter { get; set; }
        public List<LoyaltyTopUserVM> TopQuarterUsers { get; set; } = new List<LoyaltyTopUserVM>();
        public List<LoyaltyHistoryRowVM> RecentActivities { get; set; } = new List<LoyaltyHistoryRowVM>();
    }

    public class LoyaltyTopUserVM
    {
        public int UserID { get; set; }
        public string FullName { get; set; }
        public int TotalPoints { get; set; }
        public int QuarterPoints { get; set; }
        public string RankName { get; set; }
    }

    public class LoyaltyUsersVM
    {
        public string Query { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<LoyaltyUserRowVM> Rows { get; set; } = new List<LoyaltyUserRowVM>();
    }

    public class LoyaltyUserRowVM
    {
        public int UserID { get; set; }
        public string FullName { get; set; }
        public int TotalPoints { get; set; }
        public int CurrentQuarterPoints { get; set; }
        public string RankName { get; set; }
    }

    public class LoyaltyHistoryVM
    {
        public int? UserID { get; set; }
        public string Direction { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<LoyaltyHistoryRowVM> Rows { get; set; } = new List<LoyaltyHistoryRowVM>();
    }

    public class LoyaltyHistoryRowVM
    {
        public DateTime CreatedAt { get; set; }
        public int UserID { get; set; }
        public string UserName { get; set; }
        public int? OrderID { get; set; }
        public int Points { get; set; }
        public string Direction { get; set; }
        public string Reason { get; set; }
    }

    public class LoyaltyConfigVM
    {
        public decimal EarnRate { get; set; }
        public bool IncludeShippingFee { get; set; }
        public string PaidKeywords { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
