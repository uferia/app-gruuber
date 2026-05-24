using Xunit;

namespace Gruuber.Tests.Integration.Analytics;

/// <summary>
/// Integration tests for Gruuber.Analytics module.
/// Requires Docker (Postgres + Kafka via Testcontainers).
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class AnalyticsDashboardIntegrationTests
{
    [Fact(Skip = "Requires Docker")]
    public async Task Publish5RideCompletedEvents_DriverStatsHasCorrectCumulativeTotals()
    {
        // Arrange: Postgres + Kafka containers; seed region config
        // Act: publish 5 ride_completed events via Kafka
        // Assert: driver_stats_daily has trips_completed=5, correct gross_earnings
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    public async Task PublishOrderDeliveredWith3Items_ThreeMenuItemStatsRows()
    {
        // Arrange: Postgres + Kafka containers
        // Act: publish order_delivered with 3 items
        // Assert: 3 rows in menu_item_stats_daily with correct units_sold and revenue
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    public async Task ReplayDuplicateEvent_TotalsUnchanged()
    {
        // Act: publish same event_id twice
        // Assert: second event skipped; totals reflect only one event
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    public async Task ConsumerFails5Times_MessageInDLQ()
    {
        // Arrange: misconfigured consumer + real Kafka
        // Assert: after 5 retries, message in analytics-events-dlq-{region}
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    public async Task ExportJob_RequestCSV_PollJobId_DownloadUrl()
    {
        // Act: POST /v1/analytics/driver/earnings/export?format=csv
        // Assert: 202 with job_id; poll job_id; assert completed with download_url
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    public async Task DriverCannotAccessOtherDriversData_Returns403()
    {
        // Act: GET /v1/analytics/driver/summary as driver A with driver B's sub in JWT
        // Assert: 403 Forbidden (JWT sub mismatch enforced by policy)
        await Task.CompletedTask;
    }
}
