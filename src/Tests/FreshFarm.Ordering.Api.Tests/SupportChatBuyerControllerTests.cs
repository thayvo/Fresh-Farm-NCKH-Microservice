using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FreshFarm.Ordering.Api.Controllers;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FreshFarm.Ordering.Api.Tests;

public sealed class SupportChatBuyerControllerTests
{
    [Fact]
    public async Task GetConversation_CreateIfMissing_ReusesLatestOpenConversation()
    {
        await using var db = CreateDbContext();
        db.SupportConversations.AddRange(
            new SupportConversation
            {
                ConversationId = 100,
                UserId = 47,
                AdminId = 8,
                GuestId = string.Empty,
                StartedAt = new DateTime(2026, 3, 24, 1, 0, 0, DateTimeKind.Utc),
                Status = "Closed",
                ClosedAt = new DateTime(2026, 3, 24, 2, 0, 0, DateTimeKind.Utc)
            },
            new SupportConversation
            {
                ConversationId = 101,
                UserId = 47,
                AdminId = 8,
                GuestId = string.Empty,
                StartedAt = new DateTime(2026, 3, 24, 3, 0, 0, DateTimeKind.Utc),
                Status = "Open",
                ClosedAt = null
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.GetConversation(8, createIfMissing: true, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(101, json.RootElement.GetProperty("conversation").GetProperty("conversationId").GetInt32());
        Assert.Equal("Open", json.RootElement.GetProperty("conversation").GetProperty("status").GetString());
        Assert.Equal(2, await db.SupportConversations.CountAsync());
    }

    [Fact]
    public async Task GetConversation_CreateIfMissing_CreatesNewConversationWhenLatestIsClosed()
    {
        await using var db = CreateDbContext();
        db.SupportConversations.Add(new SupportConversation
        {
            ConversationId = 200,
            UserId = 47,
            AdminId = 8,
            GuestId = string.Empty,
            StartedAt = new DateTime(2026, 3, 24, 1, 0, 0, DateTimeKind.Utc),
            Status = "Closed",
            ClosedAt = new DateTime(2026, 3, 24, 2, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.GetConversation(8, createIfMissing: true, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var conversation = json.RootElement.GetProperty("conversation");
        var createdConversationId = conversation.GetProperty("conversationId").GetInt32();
        Assert.NotEqual(200, createdConversationId);
        Assert.Equal("Open", conversation.GetProperty("status").GetString());

        var conversations = await db.SupportConversations
            .Where(x => x.UserId == 47 && x.AdminId == 8)
            .OrderBy(x => x.ConversationId)
            .ToListAsync();

        Assert.Equal(2, conversations.Count);
        Assert.Equal("Closed", conversations[0].Status);
        Assert.Equal("Open", conversations[1].Status);
        Assert.Null(conversations[1].ClosedAt);
    }

    [Fact]
    public async Task Summaries_WithoutSellerIds_ReturnsLatestConversationPerSeller()
    {
        await using var db = CreateDbContext();
        db.SupportConversations.AddRange(
            new SupportConversation
            {
                ConversationId = 301,
                UserId = 47,
                AdminId = 8,
                GuestId = string.Empty,
                StartedAt = new DateTime(2026, 3, 24, 1, 0, 0, DateTimeKind.Utc),
                Status = "Open"
            },
            new SupportConversation
            {
                ConversationId = 302,
                UserId = 47,
                AdminId = 9,
                GuestId = string.Empty,
                StartedAt = new DateTime(2026, 3, 24, 2, 0, 0, DateTimeKind.Utc),
                Status = "Closed",
                ClosedAt = new DateTime(2026, 3, 24, 2, 30, 0, DateTimeKind.Utc)
            });

        db.SupportMessages.AddRange(
            new SupportMessage
            {
                MessageId = 401,
                ConversationId = 301,
                SenderType = 1,
                SenderUserId = 8,
                Content = "Shop 8 reply",
                CreatedAt = new DateTime(2026, 3, 24, 4, 0, 0, DateTimeKind.Utc),
                IsRead = false,
                IsDeleted = false
            },
            new SupportMessage
            {
                MessageId = 402,
                ConversationId = 302,
                SenderType = 1,
                SenderUserId = 9,
                Content = "Shop 9 old reply",
                CreatedAt = new DateTime(2026, 3, 24, 3, 0, 0, DateTimeKind.Utc),
                IsRead = true,
                IsDeleted = false
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, 47);

        var result = await controller.Summaries(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var summaries = json.RootElement.GetProperty("summaries");
        Assert.Equal(2, summaries.GetArrayLength());
        Assert.Equal(8, summaries[0].GetProperty("sellerId").GetInt32());
        Assert.Equal(301, summaries[0].GetProperty("conversationId").GetInt32());
        Assert.True(summaries[0].GetProperty("hasUnread").GetBoolean());
        Assert.Equal(9, summaries[1].GetProperty("sellerId").GetInt32());
        Assert.Equal(302, summaries[1].GetProperty("conversationId").GetInt32());
    }

    private static SupportChatBuyerController CreateController(FreshFarmOrderingDBContext db, int userId)
    {
        return new SupportChatBuyerController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(userId)
            }
        };
    }

    private static DefaultHttpContext BuildHttpContext(int userId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
        ], "Test"));
        return context;
    }

    private static FreshFarmOrderingDBContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FreshFarmOrderingDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FreshFarmOrderingDBContext(options);
    }
}
