using System.Collections;
using System.Reflection;
using Analytika.Services;
using FluentAssertions;
using Xunit;

namespace Analytika.Tests.Services;

public class RemittanceReportRegressionTests
{
    [Fact]
    public void Ra_parser_sums_activity_amounts_and_uses_header_payment_reference()
    {
        const string xml = """
            <Remittance.Advice><Header><TransactionDate>01/08/2026 10:00</TransactionDate><PaymentReference>PAY-1</PaymentReference></Header>
              <Claim><ID>C-1</ID><Activity><Net>100.10</Net><PaymentAmount>80.00</PaymentAmount></Activity>
                <Activity><Net>25.20</Net><PaymentAmount>0</PaymentAmount><DenialCode>D1</DenialCode></Activity></Claim>
            </Remittance.Advice>
            """;

        var entry = Parse(xml).Single();

        Property<decimal>(entry, "ReceivedAmt").Should().Be(125.30m);
        Property<decimal>(entry, "ApprovedAmt").Should().Be(80.00m);
        Property<string>(entry, "PaymentRef").Should().Be("PAY-1");
        Property<string>(entry, "DenialCode").Should().Be("D1");
    }

    [Fact]
    public void Ra_parser_keeps_zero_paid_denial_as_rejected_edge_case()
    {
        const string xml = """
            <Remittance.Advice><Claim><ClaimID>C-2</ClaimID>
              <Activity><Net>50</Net><PaymentAmount>0</PaymentAmount></Activity>
              <Denial><Code>D2</Code><Description>Rejected service</Description></Denial>
            </Claim></Remittance.Advice>
            """;

        var entry = Parse(xml).Single();

        Property<string>(entry, "Status").Should().Be("Rejected");
        Property<string>(entry, "DenialCode").Should().Be("D2");
        Property<string>(entry, "DenialDescription").Should().Contain("Rejected service");
    }

    private static List<object> Parse(string xml)
    {
        var method = typeof(ReportService).GetMethod("ParseRaXml", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((IEnumerable)method.Invoke(null, [xml, "ra.xml", "01/08/2026 10:00"])!).Cast<object>().ToList();
    }

    private static T Property<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(value)!;
}
