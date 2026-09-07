using Analytika.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analytika.Tests.Services;

public class EmailDeliveryTests
{
    [Fact]
    public async Task Missing_smtp_must_not_report_success()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var sut = new EmailService(new ConfigurationBuilder().Build(), NullLogger<EmailService>.Instance, services);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SendReportAsync("test@example.invalid", "fixture", "ClaimSummary", "missing.xlsx"));
        Assert.Contains("SMTP host", error.Message);
    }

    [Fact]
    public async Task Missing_attachment_must_not_send_an_empty_email()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.invalid"
        }).Build();
        var sut = new EmailService(config, NullLogger<EmailService>.Instance, services);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            sut.SendReportAsync("test@example.invalid", "fixture", "ClaimSummary", "nonexistent-fixture.xlsx"));
    }
}
