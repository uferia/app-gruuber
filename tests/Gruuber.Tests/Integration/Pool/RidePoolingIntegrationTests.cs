using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Integration.Pool;

[TestCategory("Integration")]
[TestClass]
public class RidePoolingIntegrationTests
{
    [TestMethod, Ignore("Requires Testcontainers: Postgres + Redis + Kafka")]
    public async Task HappyPath_TwoRiders_PoolMatch_BothComplete()
    {
        // Arrange: two riders request pool rides in same region
        // Act: PoolMatcherService runs and matches them
        // Assert: both rides are PoolMatched; fare is discounted; outbox events emitted
        await Task.CompletedTask;
    }

    [TestMethod, Ignore("Requires Testcontainers: Postgres + Redis")]
    public async Task TimeoutFlow_NoMatch_RideExpires_TransitionsToCancel()
    {
        // Arrange: one rider requests pool ride; no second rider within timeout
        // Act: PoolTimeoutWorker sweeps after MatchTimeoutSecs
        // Assert: ride status = Cancelled; ride_pool_timeout event in outbox; idempotent on re-sweep
        await Task.CompletedTask;
    }

    [TestMethod, Ignore("Requires Testcontainers: Postgres concurrent connections")]
    public async Task RaceCondition_TwoMatchers_OnlyOneSucceeds_NoDuplicateMatch()
    {
        // Arrange: two pool rides eligible for matching
        // Act: two PoolMatcherService instances race to match
        // Assert: exactly one match succeeds (Lua ZREM is atomic); other is a no-op
        await Task.CompletedTask;
    }

    [TestMethod, Ignore("Requires Testcontainers: Postgres")]
    public async Task PrivacyGuard_RideViewDoesNotExposePoolPartnerPII()
    {
        // Arrange: pool ride matched with a partner
        // Act: query ride_views for either rider
        // Assert: partner PII (name, phone) is not present in ride_view read model
        await Task.CompletedTask;
    }

}
