using Gruuber.SharedKernel.Results;
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
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(42, result.Value);
        Assert.IsNull(result.ErrorCode);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Ok_WithReferenceType_ReturnsPopulatedValue()
    {
        // Arrange
        var expected = new { Name = "Rider" };

        // Act
        var result = Result<object>.Ok(expected);

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(expected, result.Value);
    }

    // ── Result<T>.Fail ────────────────────────────────────────────────────────

    [TestMethod]
    public void Fail_ReturnsFailureResult_WithErrorDetails()
    {
        // Arrange / Act — use a reference type so Value is null on failure
        var result = Result<string>.Fail("NOT_FOUND", "Resource was not found.");

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("NOT_FOUND", result.ErrorCode);
        Assert.AreEqual("Resource was not found.", result.ErrorMessage);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void Fail_IsEquivalentTo_Failure()
    {
        // Arrange
        var viaAlias   = Result<string>.Fail("ERR", "msg");
        var viaVerbose = Result<string>.Failure("ERR", "msg");

        // Assert — both factory methods produce the same observable state
        Assert.AreEqual(viaAlias.IsSuccess, viaVerbose.IsSuccess);
        Assert.AreEqual(viaAlias.ErrorCode, viaVerbose.ErrorCode);
        Assert.AreEqual(viaAlias.ErrorMessage, viaVerbose.ErrorMessage);
    }

    // ── ApplicationResult<T> ──────────────────────────────────────────────────

    [TestMethod]
    public void ApplicationResult_Success_ReturnsHttp200()
    {
        // Act
        var result = ApplicationResult<string>.Success("hello");

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(200, result.StatusCode);
        Assert.AreEqual("hello", result.Data);
    }

    [TestMethod]
    public void ApplicationResult_Accepted_ReturnsHttp202()
    {
        // Act
        var result = ApplicationResult<Guid>.Accepted(Guid.Empty);

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(202, result.StatusCode);
    }

    [TestMethod]
    public void ApplicationResult_Failure_ReturnsHttp400ByDefault()
    {
        // Act
        var result = ApplicationResult<string>.Failure("INVALID", "bad request");

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual("INVALID", result.ErrorCode);
    }

    [TestMethod]
    public void ApplicationResult_Conflict_ReturnsHttp409WithResourceConflictedCode()
    {
        // Arrange
        var entityId = Guid.NewGuid();

        // Act
        var result = ApplicationResult<string>.Conflict(entityId, currentVersion: 5);

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual("RESOURCE_CONFLICTED", result.ErrorCode);
        Assert.IsTrue(result.ErrorMessage!.Contains("5"));
    }

    [TestMethod]
    public void ApplicationResult_CustomStatusCode_IsPreserved()
    {
        // Act
        var result = ApplicationResult<string>.Failure("SERVER_ERR", "oops", 500);

        // Assert
        Assert.AreEqual(500, result.StatusCode);
    }
}
