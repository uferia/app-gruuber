using Gruuber.SharedKernel.Results;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Static Factory Method pattern on Result&lt;T&gt; and ApplicationResult&lt;T&gt;.
/// Verifies Ok/Fail aliases and all factory branches behave correctly.
/// </summary>
[TestClass]
public class ResultStaticFactoryTests
{
    // ── Result<T>.Ok ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Ok_ReturnsSuccessResult_WithCorrectValue()
    {
        // Arrange / Act
        var result = Result<int>.Ok(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [TestMethod]
    public void Ok_WithReferenceType_ReturnsPopulatedValue()
    {
        // Arrange
        var expected = new { Name = "Rider" };

        // Act
        var result = Result<object>.Ok(expected);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
    }

    // ── Result<T>.Fail ────────────────────────────────────────────────────────

    [TestMethod]
    public void Fail_ReturnsFailureResult_WithErrorDetails()
    {
        // Arrange / Act — use a reference type so Value is null on failure
        var result = Result<string>.Fail("NOT_FOUND", "Resource was not found.");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
        result.ErrorMessage.Should().Be("Resource was not found.");
        result.Value.Should().BeNull();
    }

    [TestMethod]
    public void Fail_IsEquivalentTo_Failure()
    {
        // Arrange
        var viaAlias   = Result<string>.Fail("ERR", "msg");
        var viaVerbose = Result<string>.Failure("ERR", "msg");

        // Assert — both factory methods produce the same observable state
        viaVerbose.IsSuccess.Should().Be(viaAlias.IsSuccess);
        viaVerbose.ErrorCode.Should().Be(viaAlias.ErrorCode);
        viaVerbose.ErrorMessage.Should().Be(viaAlias.ErrorMessage);
    }

    // ── ApplicationResult<T> ──────────────────────────────────────────────────

    [TestMethod]
    public void ApplicationResult_Success_ReturnsHttp200()
    {
        // Act
        var result = ApplicationResult<string>.Success("hello");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().Be("hello");
    }

    [TestMethod]
    public void ApplicationResult_Accepted_ReturnsHttp202()
    {
        // Act
        var result = ApplicationResult<Guid>.Accepted(Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
    }

    [TestMethod]
    public void ApplicationResult_Failure_ReturnsHttp400ByDefault()
    {
        // Act
        var result = ApplicationResult<string>.Failure("INVALID", "bad request");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("INVALID");
    }

    [TestMethod]
    public void ApplicationResult_Conflict_ReturnsHttp409WithResourceConflictedCode()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var result = ApplicationResult<string>.Conflict(entityId, currentVersion: 5);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("RESOURCE_CONFLICTED");
        result.ErrorMessage!.Contains("5").Should().BeTrue();
    }

    [TestMethod]
    public void ApplicationResult_CustomStatusCode_IsPreserved()
    {
        // Act
        var result = ApplicationResult<string>.Failure("SERVER_ERR", "oops", 500);

        // Assert
        result.StatusCode.Should().Be(500);
    }
}
