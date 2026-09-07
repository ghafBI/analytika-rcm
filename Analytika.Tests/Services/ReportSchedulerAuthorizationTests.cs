using System.Security.Claims;
using Analytika.Controllers;
using Analytika.Models;
using Analytika.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Analytika.Tests.Services;

public class ReportSchedulerAuthorizationTests
{
    [Fact]
    public async Task Missing_identity_user_cannot_download_existing_report()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var users = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(),
            null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(user => user.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ApplicationUser?)null);
        var service = new Mock<IReportService>();
        service.Setup(reports => reports.GetReportByIdAsync(42)).ReturnsAsync(new ReportRequest
            { Id = 42, BranchId = 1, FilePath = "/reports/private.xlsx" });
        var controller = new ReportSchedulerController(context, service.Object, users.Object, null!)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        Assert.IsType<NotFoundResult>(await controller.Download(42));
    }
}
