using FluentAssertions;
using Gruuber.Restaurants.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RestaurantDomainTests
{
    private static Restaurant NewRestaurant() =>
        Restaurant.Create(Guid.NewGuid(), "Kanto Grill", "Filipino BBQ", "Filipino", "123 Mabini St", 14.5995, 120.9842, 1);

    [TestMethod]
    public void Create_StartsPendingClosedVersion1()
    {
        var r = NewRestaurant();

        r.ApprovalStatus.Should().Be(RestaurantApprovalStatus.Pending);
        r.IsOpen.Should().BeFalse();
        r.Version.Should().Be(1);
        r.RegionId.Should().Be(1);
        r.Name.Should().Be("Kanto Grill");
    }

    [TestMethod]
    public void Approve_SetsStatusTimestampAndBumpsVersion()
    {
        var r = NewRestaurant();

        r.Approve();

        r.ApprovalStatus.Should().Be(RestaurantApprovalStatus.Approved);
        r.ApprovedAt.Should().NotBeNull();
        r.RejectionReason.Should().BeNull();
        r.Version.Should().Be(2);
    }

    [TestMethod]
    public void Reject_SetsReasonClearsApprovedAtAndBumpsVersion()
    {
        var r = NewRestaurant();
        r.Approve();

        r.Reject("Incomplete documents");

        r.ApprovalStatus.Should().Be(RestaurantApprovalStatus.Rejected);
        r.RejectionReason.Should().Be("Incomplete documents");
        r.ApprovedAt.Should().BeNull();
        r.Version.Should().Be(3);
    }

    [TestMethod]
    public void SetOpen_TogglesAndBumpsVersion()
    {
        var r = NewRestaurant();

        r.SetOpen(true);

        r.IsOpen.Should().BeTrue();
        r.Version.Should().Be(2);
    }

    [TestMethod]
    public void UpdateProfile_ReplacesFieldsAndBumpsVersion()
    {
        var r = NewRestaurant();

        r.UpdateProfile("Kanto Grill 2", "New desc", "BBQ", "456 Rizal Ave", 14.6, 121.0);

        r.Name.Should().Be("Kanto Grill 2");
        r.CuisineType.Should().Be("BBQ");
        r.Address.Should().Be("456 Rizal Ave");
        r.Version.Should().Be(2);
    }

    [TestMethod]
    public void MenuItem_Create_DefaultsAvailableVersion1()
    {
        var item = MenuItem.Create(Guid.NewGuid(), "Pork BBQ", "3 sticks", "Grill", 120.00m, "PHP", 1);

        item.IsAvailable.Should().BeTrue();
        item.Version.Should().Be(1);
        item.Price.Should().Be(120.00m);
        item.Currency.Should().Be("PHP");
    }

    [TestMethod]
    public void MenuItem_Update_ReplacesFieldsAndBumpsVersion()
    {
        var item = MenuItem.Create(Guid.NewGuid(), "Pork BBQ", "3 sticks", "Grill", 120.00m, "PHP", 1);

        item.Update("Pork BBQ Large", "5 sticks", "Grill", 180.00m, false);

        item.Name.Should().Be("Pork BBQ Large");
        item.Price.Should().Be(180.00m);
        item.IsAvailable.Should().BeFalse();
        item.Version.Should().Be(2);
    }
}
