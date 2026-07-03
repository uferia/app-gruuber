using Gruuber.Orders.Application.Specifications;
using Gruuber.Rides.Application.Specifications;
using Gruuber.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Specification / Filter-Criteria pattern.
/// Covers: ISpecification&lt;T&gt; composites (And, Or, Not) and domain-specific
/// specs for driver matching and order eligibility.
/// </summary>
[TestClass]
public class SpecificationTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Generic Composite Combinators
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class AlwaysTrue<T> : ISpecification<T>
    {
        public bool IsSatisfiedBy(T candidate) => true;
    }

    private sealed class AlwaysFalse<T> : ISpecification<T>
    {
        public bool IsSatisfiedBy(T candidate) => false;
    }

    [TestMethod]
    public void AndSpecification_TrueAndTrue_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysTrue<string>().And(new AlwaysTrue<string>());

        // Assert
        spec.IsSatisfiedBy("x").Should().BeTrue();
    }

    [TestMethod]
    public void AndSpecification_TrueAndFalse_ReturnsFalse()
    {
        var spec = new AlwaysTrue<string>().And(new AlwaysFalse<string>());
        spec.IsSatisfiedBy("x").Should().BeFalse();
    }

    [TestMethod]
    public void OrSpecification_FalseOrTrue_ReturnsTrue()
    {
        var spec = new AlwaysFalse<string>().Or(new AlwaysTrue<string>());
        spec.IsSatisfiedBy("x").Should().BeTrue();
    }

    [TestMethod]
    public void OrSpecification_FalseOrFalse_ReturnsFalse()
    {
        var spec = new AlwaysFalse<string>().Or(new AlwaysFalse<string>());
        spec.IsSatisfiedBy("x").Should().BeFalse();
    }

    [TestMethod]
    public void NotSpecification_NegatesTrue()
    {
        var spec = new AlwaysTrue<int>().Not();
        spec.IsSatisfiedBy(0).Should().BeFalse();
    }

    [TestMethod]
    public void NotSpecification_NegatesFalse()
    {
        var spec = new AlwaysFalse<int>().Not();
        spec.IsSatisfiedBy(0).Should().BeTrue();
    }

    [TestMethod]
    public void ChainedComposites_AndOrNot_EvaluateCorrectly()
    {
        // (True AND NOT False) OR False  →  (True AND True) OR False  →  True OR False  →  True
        var spec = new AlwaysTrue<string>()
            .And(new AlwaysFalse<string>().Not())
            .Or(new AlwaysFalse<string>());

        spec.IsSatisfiedBy("any").Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Driver Match Specifications
    // ══════════════════════════════════════════════════════════════════════════

    private static DriverCandidateContext MakeDriver(
        bool available = true,
        double distKm = 2.0,
        double rating = 4.5,
        int regionId  = 1) =>
        new(Guid.NewGuid(), Score: 0.8, distKm, rating, available, regionId);

    [TestMethod]
    public void DriverAvailableSpec_EligibleWhenAvailable()
    {
        // Arrange
        var spec = new DriverAvailableSpecification();

        // Assert
        spec.IsSatisfiedBy(MakeDriver(available: true)).Should().BeTrue();
        spec.IsSatisfiedBy(MakeDriver(available: false)).Should().BeFalse();
    }

    [TestMethod]
    public void DriverWithinRadiusSpec_RejectsDriverBeyondRadius()
    {
        // Arrange
        var spec = new DriverWithinRadiusSpecification(maxDistanceKm: 3.0);

        // Assert
        spec.IsSatisfiedBy(MakeDriver(distKm: 2.9)).Should().BeTrue();
        spec.IsSatisfiedBy(MakeDriver(distKm: 3.1)).Should().BeFalse();
    }

    [TestMethod]
    public void DriverWithinRadiusSpec_ExactBoundaryIsIncluded()
    {
        // Arrange — exactly at boundary
        var spec = new DriverWithinRadiusSpecification(maxDistanceKm: 5.0);

        // Assert
        spec.IsSatisfiedBy(MakeDriver(distKm: 5.0)).Should().BeTrue();
    }

    [TestMethod]
    public void DriverMinRatingSpec_RejectsLowRating()
    {
        // Arrange
        var spec = new DriverMinRatingSpecification(minRating: 4.0);

        // Assert
        spec.IsSatisfiedBy(MakeDriver(rating: 4.0)).Should().BeTrue();
        spec.IsSatisfiedBy(MakeDriver(rating: 3.9)).Should().BeFalse();
    }

    [TestMethod]
    public void DriverMatchEligibilitySpec_AllConditionsMet_IsEligible()
    {
        // Arrange — well within all thresholds
        var spec      = new DriverMatchEligibilitySpecification(maxDistanceKm: 5, minRating: 3.5);
        var candidate = MakeDriver(available: true, distKm: 2.0, rating: 4.5);

        // Assert
        spec.IsSatisfiedBy(candidate).Should().BeTrue();
    }

    [TestMethod]
    public void DriverMatchEligibilitySpec_UnavailableDriver_IsIneligible()
    {
        // Arrange
        var spec      = new DriverMatchEligibilitySpecification();
        var candidate = MakeDriver(available: false, distKm: 1.0, rating: 5.0);

        // Assert
        spec.IsSatisfiedBy(candidate).Should().BeFalse();
    }

    [TestMethod]
    public void DriverMatchEligibilitySpec_TooFar_IsIneligible()
    {
        // Arrange
        var spec      = new DriverMatchEligibilitySpecification(maxDistanceKm: 3.0);
        var candidate = MakeDriver(available: true, distKm: 4.0, rating: 5.0);

        // Assert
        spec.IsSatisfiedBy(candidate).Should().BeFalse();
    }

    [TestMethod]
    public void DriverMatchEligibilitySpec_LowRating_IsIneligible()
    {
        // Arrange
        var spec      = new DriverMatchEligibilitySpecification(minRating: 4.0);
        var candidate = MakeDriver(available: true, distKm: 1.0, rating: 3.0);

        // Assert
        spec.IsSatisfiedBy(candidate).Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Order Eligibility Specifications
    // ══════════════════════════════════════════════════════════════════════════

    private static OrderEligibilityContext MakeOrder(
        bool restaurantOpen = true,
        int itemCount       = 2,
        decimal total       = 20m,
        int regionId        = 1) =>
        new(Guid.NewGuid(), restaurantOpen, itemCount, total, regionId);

    [TestMethod]
    public void RestaurantOpenSpec_ClosedRestaurant_Fails()
    {
        // Arrange
        var spec = new RestaurantOpenSpecification();

        // Assert
        spec.IsSatisfiedBy(MakeOrder(restaurantOpen: false)).Should().BeFalse();
        spec.IsSatisfiedBy(MakeOrder(restaurantOpen: true)).Should().BeTrue();
    }

    [TestMethod]
    public void OrderHasItemsSpec_EmptyBasket_Fails()
    {
        // Arrange
        var spec = new OrderHasItemsSpecification();

        // Assert
        spec.IsSatisfiedBy(MakeOrder(itemCount: 0)).Should().BeFalse();
        spec.IsSatisfiedBy(MakeOrder(itemCount: 1)).Should().BeTrue();
    }

    [TestMethod]
    public void MinimumOrderAmountSpec_BelowMinimum_Fails()
    {
        // Arrange
        var spec = new MinimumOrderAmountSpecification(minimumAmount: 10m);

        // Assert
        spec.IsSatisfiedBy(MakeOrder(total: 9.99m)).Should().BeFalse();
        spec.IsSatisfiedBy(MakeOrder(total: 10.00m)).Should().BeTrue();
    }

    [TestMethod]
    public void OrderEligibilitySpec_AllConditionsMet_IsEligible()
    {
        // Arrange
        var spec = new OrderEligibilitySpecification(minimumOrderAmount: 5m);
        var ctx  = MakeOrder(restaurantOpen: true, itemCount: 3, total: 25m);

        // Assert
        spec.IsSatisfiedBy(ctx).Should().BeTrue();
    }

    [TestMethod]
    public void OrderEligibilitySpec_ClosedRestaurant_Fails()
    {
        // Arrange
        var spec = new OrderEligibilitySpecification();
        var ctx  = MakeOrder(restaurantOpen: false);

        // Assert
        spec.IsSatisfiedBy(ctx).Should().BeFalse();
    }

    [TestMethod]
    public void OrderEligibilitySpec_ZeroItems_Fails()
    {
        // Arrange
        var spec = new OrderEligibilitySpecification();
        var ctx  = MakeOrder(itemCount: 0, total: 0m);

        // Assert
        spec.IsSatisfiedBy(ctx).Should().BeFalse();
    }

    [TestMethod]
    public void OrderEligibilitySpec_TotalBelowMinimum_Fails()
    {
        // Arrange
        var spec = new OrderEligibilitySpecification(minimumOrderAmount: 15m);
        var ctx  = MakeOrder(restaurantOpen: true, itemCount: 1, total: 5m);

        // Assert
        spec.IsSatisfiedBy(ctx).Should().BeFalse();
    }
}
