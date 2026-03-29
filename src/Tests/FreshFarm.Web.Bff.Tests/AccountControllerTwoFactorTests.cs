using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FreshFarm.Web.Bff.Controllers;
using FreshFarm.Web.Bff.Dtos;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FreshFarm.Web.Bff.Tests;

public sealed class AccountControllerTwoFactorTests
{
    [Fact]
    public async Task OrderHistory_PopulatesAccountNotificationSummaryForSharedNav()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = false,
                        status = "pending",
                        statusLabel = "Đang chờ duyệt",
                        reviewStatus = "pending",
                        reviewStatusLabel = "Đang chờ duyệt"
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        page = 1,
                        pageSize = 1,
                        total = 1,
                        totalPages = 1,
                        stats = new
                        {
                            totalNotifications = 3,
                            unreadNotifications = 2,
                            pushNotifications = 0,
                            recent24hNotifications = 1
                        },
                        filters = new
                        {
                            q = "",
                            type = "",
                            isRead = (bool?)null,
                            typeOptions = Array.Empty<string>()
                        },
                        notifications = Array.Empty<object>()
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/my")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[]
                    {
                        new
                        {
                            orderId = 101,
                            status = "pending",
                            statusLabel = "Chờ xử lý",
                            orderDate = DateTime.UtcNow,
                            totalAmount = 125000m
                        }
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        }, provideDefaultNotificationNavSummary: false);

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.OrderHistory();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<OrderHistoryItemDto>>(view.Model);
        Assert.Single(model);
        var navSummary = Assert.IsType<AccountNotificationNavSummaryDto>(controller.ViewData["AccountNotificationSummary"]);
        Assert.Equal(2, navSummary.UnreadCount);
    }

    [Fact]
    public async Task OrderDetail_PopulatesAccountNotificationSummaryForSharedNav()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = true,
                        status = "approved",
                        statusLabel = "Đã là người bán",
                        reviewStatus = "approved",
                        reviewStatusLabel = "Đã duyệt"
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        page = 1,
                        pageSize = 1,
                        total = 1,
                        totalPages = 1,
                        stats = new
                        {
                            totalNotifications = 1,
                            unreadNotifications = 1,
                            pushNotifications = 0,
                            recent24hNotifications = 1
                        },
                        filters = new
                        {
                            q = "",
                            type = "",
                            isRead = (bool?)null,
                            typeOptions = Array.Empty<string>()
                        },
                        notifications = Array.Empty<object>()
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/101")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        orderId = 101,
                        status = "pending",
                        statusLabel = "Chờ xử lý",
                        totalAmount = 125000m,
                        items = Array.Empty<object>(),
                        shippings = Array.Empty<object>(),
                        payments = Array.Empty<object>()
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        }, provideDefaultNotificationNavSummary: false);

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.OrderDetail(101);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<OrderDetailDto>(view.Model);
        var navSummary = Assert.IsType<AccountNotificationNavSummaryDto>(controller.ViewData["AccountNotificationSummary"]);
        Assert.Equal(1, navSummary.UnreadCount);
    }

    [Fact]
    public async Task Notifications_ReturnsViewModelFromOrderingCustomerNotifications()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = true,
                        status = "approved",
                        statusLabel = "Đã là người bán",
                        reviewStatus = "approved",
                        reviewStatusLabel = "Đã duyệt"
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        page = 1,
                        pageSize = 12,
                        total = 1,
                        totalPages = 1,
                        stats = new
                        {
                            totalNotifications = 2,
                            unreadNotifications = 1,
                            pushNotifications = 0,
                            recent24hNotifications = 2
                        },
                        filters = new
                        {
                            q = "",
                            type = "seller_review_update",
                            isRead = (bool?)null,
                            typeOptions = new[] { "seller_review_update", "order_status" }
                        },
                        notifications = new object[]
                        {
                            new
                            {
                                notificationId = 35,
                                userId = 47,
                                orderId = 0,
                                notificationType = "seller_review_update",
                                title = "Hồ sơ seller đã được duyệt",
                                message = "Tài khoản của bạn đã có quyền người bán.",
                                isRead = false,
                                isPushNotification = false,
                                createdAt = DateTime.UtcNow,
                                readAt = (DateTime?)null
                            }
                        }
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.Notifications();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AccountNotificationsPageViewModel>(view.Model);
        Assert.True(model.SellerApplication.IsSellerApproved);
        Assert.Single(model.Notifications);
        Assert.Equal(1, model.UnreadNotifications);
        Assert.Equal("seller_review_update", model.Type);
        Assert.Equal("Hồ sơ seller đã được duyệt", model.Notifications[0].Title);
    }

    [Fact]
    public async Task MarkNotificationAsRead_PostsToOrderingAndRedirectsBackToFilters()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me/35/mark-read")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        message = "Đã đánh dấu đã đọc."
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.MarkNotificationAsRead(35, q: "seller", type: "seller_review_update", isRead: false, page: 2);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Notifications), redirect.ActionName);
        Assert.Equal("Đã đánh dấu đã đọc.", controller.TempData["SuccessMessage"]);
        Assert.Contains(handler.Requests, x => x.RequestUri?.AbsolutePath == "/api/orders/notifications/me/35/mark-read");
        Assert.Equal("seller", redirect.RouteValues?["q"]);
        Assert.Equal("seller_review_update", redirect.RouteValues?["type"]);
        Assert.Equal(false, redirect.RouteValues?["isRead"]);
        Assert.Equal(2, redirect.RouteValues?["page"]);
    }

    [Fact]
    public async Task MarkAllNotificationsAsRead_PostsCurrentFilterScopeToOrdering()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me/mark-all-read")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true,
                        message = "Đã đánh dấu đã đọc 2 thông báo."
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.MarkAllNotificationsAsRead(q: "seller", type: "seller_review_update", isRead: false, page: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Notifications), redirect.ActionName);
        Assert.Equal("Đã đánh dấu đã đọc 2 thông báo.", controller.TempData["SuccessMessage"]);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/orders/notifications/me/mark-all-read", request.RequestUri?.AbsolutePath);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"q\":\"seller\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"seller_review_update\"", body, StringComparison.Ordinal);
        Assert.Contains("\"isRead\":false", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenNotification_MarksReadAndRedirectsToLocalTarget()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me/41/mark-read")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.OpenNotification(41, "/account/orders/166");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/account/orders/166", redirect.Url);
        Assert.Contains(handler.Requests, x => x.RequestUri?.AbsolutePath == "/api/orders/notifications/me/41/mark-read");
    }

    [Fact]
    public async Task OpenNotification_FallsBackToNotifications_WhenTargetIsUnsafe()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/orders/notifications/me/41/mark-read")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        success = true
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });

        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.OpenNotification(41, "https://evil.example/phishing");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/account/notifications", redirect.Url);
        Assert.Contains(handler.Requests, x => x.RequestUri?.AbsolutePath == "/api/orders/notifications/me/41/mark-read");
    }

    [Fact]
    public async Task BecomeSeller_ReturnsViewWithRecaptchaError_WhenVerificationFails()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = false,
                        status = "pending",
                        statusLabel = "Đang chờ duyệt",
                        reviewStatus = "pending",
                        reviewStatusLabel = "Đang chờ duyệt",
                        kyc = new
                        {
                            hasKycProfile = true,
                            hasIdentityDocuments = true,
                            hasBusinessLicense = true,
                            citizenIdFrontUrl = "/uploads/front.jpg",
                            citizenIdBackUrl = "/uploads/back.jpg",
                            businessLicenseUrl = "/uploads/license.pdf"
                        }
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var recaptchaService = new FakeGoogleRecaptchaService
        {
            Result = new GoogleRecaptchaVerificationResult
            {
                Success = false,
                ErrorMessage = "Xác minh reCAPTCHA không hợp lệ."
            }
        };
        var controller = CreateController(
            handler,
            googleRecaptchaOptions: new GoogleRecaptchaOptions
            {
                SiteKey = "site-key",
                SecretKey = "secret-key",
                SellerApplicationAction = "become_seller"
            },
            googleRecaptchaService: recaptchaService);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.BecomeSeller(new BecomeSellerPageViewModel
        {
            Form = new BecomeSellerRequestDto
            {
                StoreName = "Fresh Farm",
                StoreAddress = "1 Nguyen Trai",
                StoreEmail = "shop@example.com",
                StorePhone = "0912345678",
                LegalFullName = "Nguyen Van A",
                IdentityNumber = "123456789012",
                IdentityIssuedDate = new DateTime(2020, 1, 1),
                IdentityIssuedPlace = "HCM",
                RecaptchaToken = "bad-token"
            }
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            controller.ModelState,
            entry => string.Equals(entry.Key, "RecaptchaToken", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.Key, "Form.RecaptchaToken", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Requests, x => x.RequestUri?.AbsolutePath == "/auth/seller-application/me");
        Assert.DoesNotContain(handler.Requests, x => x.RequestUri?.AbsolutePath == "/auth/seller-application");
        Assert.Equal("bad-token", ((BecomeSellerPageViewModel)view.Model!).Form.RecaptchaToken);
    }

    [Fact]
    public async Task BecomeSeller_PostsApplication_WhenRecaptchaSucceeds()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = false,
                        status = "pending",
                        statusLabel = "Đang chờ duyệt",
                        reviewStatus = "pending",
                        reviewStatusLabel = "Đang chờ duyệt",
                        kyc = new
                        {
                            hasKycProfile = true,
                            hasIdentityDocuments = true,
                            hasBusinessLicense = true,
                            citizenIdFrontUrl = "/uploads/front.jpg",
                            citizenIdBackUrl = "/uploads/back.jpg",
                            businessLicenseUrl = "/uploads/license.pdf"
                        }
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/seller-application")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var recaptchaService = new FakeGoogleRecaptchaService
        {
            Result = new GoogleRecaptchaVerificationResult
            {
                Success = true,
                Action = "become_seller",
                Score = 0.9m
            }
        };
        var controller = CreateController(
            handler,
            googleRecaptchaOptions: new GoogleRecaptchaOptions
            {
                SiteKey = "site-key",
                SecretKey = "secret-key",
                SellerApplicationAction = "become_seller"
            },
            googleRecaptchaService: recaptchaService);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.BecomeSeller(new BecomeSellerPageViewModel
        {
            Form = new BecomeSellerRequestDto
            {
                StoreName = "Fresh Farm",
                StoreAddress = "1 Nguyen Trai",
                StoreEmail = "shop@example.com",
                StorePhone = "0912345678",
                LegalFullName = "Nguyen Van A",
                IdentityNumber = "123456789012",
                IdentityIssuedDate = new DateTime(2020, 1, 1),
                IdentityIssuedPlace = "HCM",
                RecaptchaToken = "good-token"
            }
        });

        Assert.True(
            result is RedirectToActionResult,
            string.Join(" | ", controller.ModelState.SelectMany(x => x.Value?.Errors.Select(e => $"{x.Key}:{e.ErrorMessage}") ?? [])));
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.BecomeSeller), redirect.ActionName);
        Assert.Equal("Đã gửi hồ sơ đăng ký người bán kèm giấy tờ pháp lý. Bạn có thể theo dõi trạng thái ngay trên trang này.", controller.TempData["SuccessMessage"]);
        Assert.Equal("good-token", recaptchaService.LastToken);
        Assert.Equal("become_seller", recaptchaService.LastExpectedAction);
        Assert.Contains(handler.Requests, x => x.RequestUri?.AbsolutePath == "/auth/seller-application");
    }

    [Fact]
    public async Task BecomeSeller_ReturnsView_WhenRequiredIdentityUploadsMissing()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = false,
                        isSellerApproved = false,
                        status = "not_applied",
                        statusLabel = "Chưa đăng ký",
                        reviewStatus = "not_applied",
                        reviewStatusLabel = "Chưa có hồ sơ",
                        kyc = new { }
                    })
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var controller = CreateController(handler);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.BecomeSeller(new BecomeSellerPageViewModel
        {
            Form = new BecomeSellerRequestDto
            {
                StoreName = "Fresh Farm",
                StoreAddress = "1 Nguyen Trai",
                StoreEmail = "shop@example.com",
                StorePhone = "0912345678",
                LegalFullName = "Nguyen Van A",
                IdentityNumber = "123456789012",
                IdentityIssuedDate = new DateTime(2020, 1, 1),
                IdentityIssuedPlace = "HCM"
            }
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState, x => x.Key == "CitizenIdFrontFile");
        Assert.Contains(controller.ModelState, x => x.Key == "CitizenIdBackFile");
        Assert.DoesNotContain(handler.Requests, x => x.RequestUri?.AbsolutePath == "/auth/seller-application");
    }

    [Fact]
    public async Task BecomeSeller_DeletesNewUploads_WhenIdentityReturnsError()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/auth/seller-application/me")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        hasApplication = true,
                        isSellerApproved = false,
                        status = "pending",
                        statusLabel = "Đang chờ duyệt",
                        reviewStatus = "pending",
                        reviewStatusLabel = "Đang chờ duyệt",
                        kyc = new
                        {
                            hasKycProfile = true,
                            hasIdentityDocuments = true,
                            hasBusinessLicense = false,
                            citizenIdFrontUrl = "/uploads/front-old.jpg",
                            citizenIdBackUrl = "/uploads/back-old.jpg"
                        }
                    })
                };
            }

            if (request.RequestUri?.AbsolutePath == "/auth/seller-application")
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"message\":\"Hồ sơ seller chưa hợp lệ.\"}", Encoding.UTF8, "application/json")
                };
            }

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var storage = new FakeSellerKycStorageService();
        var controller = CreateController(handler, sellerKycStorageService: storage);
        controller.HttpContext.Session.SetString("ACCESS_TOKEN", "buyer-token");

        var result = await controller.BecomeSeller(new BecomeSellerPageViewModel
        {
            CitizenIdFrontFile = CreateFormFile("front-new.jpg", "front-bytes"),
            Form = new BecomeSellerRequestDto
            {
                StoreName = "Fresh Farm",
                StoreAddress = "1 Nguyen Trai",
                StoreEmail = "shop@example.com",
                StorePhone = "0912345678",
                LegalFullName = "Nguyen Van A",
                IdentityNumber = "123456789012",
                IdentityIssuedDate = new DateTime(2020, 1, 1),
                IdentityIssuedPlace = "HCM",
                CitizenIdBackUrl = "/uploads/back-old.jpg"
            }
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState, x => x.Value?.Errors.Any(e => e.ErrorMessage.Contains("Hồ sơ seller chưa hợp lệ.", StringComparison.Ordinal)) == true);
        Assert.Contains(storage.SavedPaths, x => x.Contains("cccd-front", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(storage.SavedPaths, storage.DeletedPaths);
        Assert.IsType<BecomeSellerPageViewModel>(view.Model);
    }

    [Fact]
    public async Task SignIn_RedirectsToTwoFactor_WhenIdentityRequiresTwoFactor()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AuthResponseDto
                {
                    RequiresTwoFactor = true,
                    RequiresTwoFactorSetup = true,
                    TwoFactorTicket = "ticket-123",
                    ManualEntryKey = "ABCD-EFGH",
                    OtpAuthUri = "otpauth://totp/FreshFarm:test@example.com?secret=ABCDEFGH&issuer=FreshFarm",
                    AuthenticatorIssuer = "FreshFarm",
                    AuthenticatorAccountName = "test@example.com",
                    ChallengeMessage = "Thiết lập ứng dụng xác thực trước khi tiếp tục."
                })
            });
        var controller = CreateController(handler);

        var result = await controller.SignIn(new LoginRequestDto
        {
            Identifier = "test@example.com",
            Password = "Password123"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.TwoFactor), redirect.ActionName);

        var challenge = ReadChallenge(controller.HttpContext.Session);
        Assert.NotNull(challenge);
        Assert.Equal("ticket-123", challenge!.Ticket);
        Assert.True(challenge.RequiresSetup);
        Assert.Equal("test@example.com", challenge.AuthenticatorAccountName);
    }

    [Fact]
    public async Task TwoFactor_Post_SignsInAndClearsChallenge_WhenVerifySucceeds()
    {
        var token = CreateAccessToken("15", "buyer01", "buyer01@example.com", ["Customer", "Seller"]);
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AuthResponseDto
                {
                    AccessToken = token,
                    ExpiredAtUtc = DateTime.UtcNow.AddHours(2)
                })
            });
        var controller = CreateController(handler);
        SaveChallenge(controller.HttpContext.Session, new TwoFactorChallengeStateDto
        {
            Ticket = "ticket-456",
            ReturnUrl = null,
            AuthenticatorAccountName = "buyer01@example.com"
        });

        var result = await controller.TwoFactor(new AccountTwoFactorViewModel
        {
            Code = "123456"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
        Assert.Null(ReadChallenge(controller.HttpContext.Session));
        Assert.Equal(token, controller.HttpContext.Session.GetString("ACCESS_TOKEN"));

        var authService = GetAuthenticationService(controller.HttpContext);
        Assert.True(authService.SignInCalled);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authService.LastScheme);

        Assert.Single(handler.Requests);
        Assert.Equal("https://identity.test/auth/login/2fa", handler.Requests[0].RequestUri?.ToString());
    }

    private static AccountController CreateController(
        RecordingHttpMessageHandler handler,
        GoogleRecaptchaOptions? googleRecaptchaOptions = null,
        FakeGoogleRecaptchaService? googleRecaptchaService = null,
        FakeSellerKycStorageService? sellerKycStorageService = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://identity.test")
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new SessionFeature
        {
            Session = new TestSession()
        });

        var authService = new TestAuthenticationService();
        var urlHelper = new TestUrlHelper();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(authService);
        services.AddSingleton<IUrlHelperFactory>(new TestUrlHelperFactory(urlHelper));
        httpContext.RequestServices = services.BuildServiceProvider();

        var controller = new AccountController(
            new StaticHttpClientFactory(client),
            new FakeGhnSandboxService(),
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions()),
            Microsoft.Extensions.Options.Options.Create(googleRecaptchaOptions ?? new GoogleRecaptchaOptions()),
            googleRecaptchaService ?? new FakeGoogleRecaptchaService(),
            new FakeSignUpCaptchaService(),
            sellerKycStorageService ?? new FakeSellerKycStorageService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        controller.Url = urlHelper;
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    private static TestAuthenticationService GetAuthenticationService(HttpContext httpContext)
    {
        return Assert.IsType<TestAuthenticationService>(httpContext.RequestServices.GetRequiredService<IAuthenticationService>());
    }

    private static TwoFactorChallengeStateDto? ReadChallenge(ISession session)
    {
        var json = session.GetString("ACCOUNT_2FA_CHALLENGE");
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<TwoFactorChallengeStateDto>(json);
    }

    private static void SaveChallenge(ISession session, TwoFactorChallengeStateDto challenge)
    {
        session.SetString("ACCOUNT_2FA_CHALLENGE", JsonSerializer.Serialize(challenge));
    }

    private static string CreateAccessToken(string userId, string userName, string email, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new("username", userName),
            new(JwtRegisteredClaimNames.Email, email)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var jwt = new JwtSecurityToken(
            issuer: "FreshFarm.Tests",
            audience: "FreshFarm.Tests",
            claims: claims);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static IFormFile CreateFormFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        bool provideDefaultNotificationNavSummary = true) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            if (provideDefaultNotificationNavSummary &&
                request.RequestUri?.AbsolutePath == "/api/orders/notifications/me" &&
                string.Equals(request.RequestUri.Query, "?page=1&pageSize=1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        page = 1,
                        pageSize = 1,
                        total = 0,
                        totalPages = 1,
                        stats = new
                        {
                            totalNotifications = 0,
                            unreadNotifications = 0,
                            pushNotifications = 0,
                            recent24hNotifications = 0
                        },
                        filters = new
                        {
                            q = "",
                            type = "",
                            isRead = (bool?)null,
                            typeOptions = Array.Empty<string>()
                        },
                        notifications = Array.Empty<object>()
                    })
                });
            }

            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                clone.Content = new StringContent(body, Encoding.UTF8);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _store.Keys;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool IsAvailable => true;

        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;

        public bool TryGetValue(string key, out byte[] value)
        {
            if (_store.TryGetValue(key, out var stored))
            {
                value = stored;
                return true;
            }

            value = null!;
            return false;
        }
    }

    private sealed class TestAuthenticationService : IAuthenticationService
    {
        public bool SignInCalled { get; private set; }
        public string? LastScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignInCalled = true;
            LastScheme = scheme;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    private sealed class TestUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext => new();

        public string? Action(UrlActionContext actionContext) => null;

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => !string.IsNullOrWhiteSpace(url) && url.StartsWith("/", StringComparison.Ordinal);

        public string? Link(string? routeName, object? values) => null;

        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class TestUrlHelperFactory(IUrlHelper urlHelper) : IUrlHelperFactory
    {
        public IUrlHelper GetUrlHelper(ActionContext context) => urlHelper;
    }

    private sealed class FakeGoogleRecaptchaService : IGoogleRecaptchaService
    {
        public bool IsConfigured => true;

        public string? LastToken { get; private set; }

        public string? LastExpectedAction { get; private set; }

        public GoogleRecaptchaVerificationResult Result { get; set; } = new()
        {
            Success = true,
            Score = 1m
        };

        public Task<GoogleRecaptchaVerificationResult> VerifyAsync(string token, string expectedAction, string? remoteIp, CancellationToken cancellationToken = default)
        {
            LastToken = token;
            LastExpectedAction = expectedAction;
            return Task.FromResult(new GoogleRecaptchaVerificationResult
            {
                Success = Result.Success,
                ErrorMessage = Result.ErrorMessage,
                Score = Result.Score,
                Action = string.IsNullOrWhiteSpace(Result.Action) ? expectedAction : Result.Action
            });
        }
    }

    private sealed class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        private Dictionary<string, object> _values = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _values = new Dictionary<string, object>(values);
        }
    }

    private sealed class FakeSignUpCaptchaService : ISignUpCaptchaService
    {
        public string GenerateCode(int length = 5) => "ABCDE";
        public string BuildSvg(string captchaCode) => "<svg></svg>";
        public bool Matches(string? expectedCode, string? submittedCode) => true;
    }

    private sealed class FakeSellerKycStorageService : ISellerKycStorageService
    {
        public List<string> SavedPaths { get; } = new();

        public List<string> DeletedPaths { get; } = new();

        public (bool isValid, string errorMessage) Validate(IFormFile file, bool allowPdf = false) => (true, string.Empty);

        public string Save(IFormFile file, string prefix, bool allowPdf = false)
        {
            var path = $"/uploads/{prefix}-{file.FileName}";
            SavedPaths.Add(path);
            return path;
        }

        public void Delete(string? requestPath)
        {
            if (!string.IsNullOrWhiteSpace(requestPath))
            {
                DeletedPaths.Add(requestPath);
            }
        }
    }

    private sealed class FakeGhnSandboxService : IGhnSandboxService
    {
        public bool IsConfigured => false;
        public int? ShopId => null;
        public Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
