using Gruuber.SharedKernel.Messaging.Pipeline;
using Gruuber.SharedKernel.Results;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel.DataAnnotations;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Decorator / MediatR Pipeline Behavior pattern.
/// Covers: LoggingBehavior (pass-through), ValidationBehavior (annotation validation),
/// and ErrorHandlingBehavior (maps exceptions to ApplicationResult failures).
/// </summary>
[TestClass]
public class PipelineBehaviorTests
{
    // ── Minimal test request / response types ─────────────────────────────────

    private record TestRequest(string Value) : IRequest<ApplicationResult<string>>;

    private record ValidatedRequest(
        [property: Required][property: MinLength(3)] string Value)
        : IRequest<ApplicationResult<string>>;

    // ── LoggingBehavior ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoggingBehavior_PassesResponseThrough_WhenNoException()
    {
        // Arrange
        var logger    = NullLogger<LoggingBehavior<TestRequest, ApplicationResult<string>>>.Instance;
        var behavior  = new LoggingBehavior<TestRequest, ApplicationResult<string>>(logger);
        var expected  = ApplicationResult<string>.Success("hello");

        // Act
        var result = await behavior.Handle(
            new TestRequest("x"),
            () => Task.FromResult(expected),
            CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [TestMethod]
    public async Task LoggingBehavior_RethrowsExceptions()
    {
        // Arrange
        var logger   = NullLogger<LoggingBehavior<TestRequest, ApplicationResult<string>>>.Instance;
        var behavior = new LoggingBehavior<TestRequest, ApplicationResult<string>>(logger);

        // Act / Assert
        var act = async () =>
            await behavior.Handle(
                new TestRequest("x"),
                () => throw new InvalidOperationException("boom"),
                CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── ValidationBehavior ────────────────────────────────────────────────────

    [TestMethod]
    public async Task ValidationBehavior_ValidRequest_PassesThrough()
    {
        // Arrange
        var behavior = new ValidationBehavior<ValidatedRequest, ApplicationResult<string>>();
        var expected = ApplicationResult<string>.Success("ok");

        // Act
        var result = await behavior.Handle(
            new ValidatedRequest("abc"), // 3 chars — passes MinLength(3)
            () => Task.FromResult(expected),
            CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [TestMethod]
    public async Task ValidationBehavior_InvalidRequest_ReturnsValidationFailure()
    {
        // Arrange
        var behavior = new ValidationBehavior<ValidatedRequest, ApplicationResult<string>>();

        // Act — "ab" has 2 chars, violates MinLength(3)
        var result = await behavior.Handle(
            new ValidatedRequest("ab"),
            () => Task.FromResult(ApplicationResult<string>.Success("should not reach")),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.StatusCode.Should().Be(400);
    }

    [TestMethod]
    public async Task ValidationBehavior_NullRequiredField_ReturnsValidationFailure()
    {
        // Arrange
        var behavior = new ValidationBehavior<ValidatedRequest, ApplicationResult<string>>();

        // Act — null value violates [Required]
        var result = await behavior.Handle(
            new ValidatedRequest(null!),
            () => Task.FromResult(ApplicationResult<string>.Success("unreachable")),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    // ── ErrorHandlingBehavior ─────────────────────────────────────────────────

    [TestMethod]
    public async Task ErrorHandlingBehavior_NoException_PassesResponseThrough()
    {
        // Arrange
        var logger    = NullLogger<ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>>.Instance;
        var behavior  = new ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>(logger);
        var expected  = ApplicationResult<string>.Success("data");

        // Act
        var result = await behavior.Handle(
            new TestRequest("x"),
            () => Task.FromResult(expected),
            CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [TestMethod]
    public async Task ErrorHandlingBehavior_UnhandledException_ReturnsHttp500Failure()
    {
        // Arrange
        var logger   = NullLogger<ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>>.Instance;
        var behavior = new ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>(logger);

        // Act — next() throws
        var result = await behavior.Handle(
            new TestRequest("x"),
            () => throw new Exception("db down"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.ErrorCode.Should().Be("INTERNAL_ERROR");
    }

    [TestMethod]
    public async Task ErrorHandlingBehavior_OperationCanceled_Rethrows()
    {
        // Arrange
        var logger   = NullLogger<ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>>.Instance;
        var behavior = new ErrorHandlingBehavior<TestRequest, ApplicationResult<string>>(logger);

        // Act / Assert — cancellation propagates
        var act = async () =>
            await behavior.Handle(
                new TestRequest("x"),
                () => throw new OperationCanceledException(),
                CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Behavior Chaining ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task ChainedBehaviors_ValidationThenLogging_ProcessRequestCorrectly()
    {
        // Arrange — simulate the pipeline: Validation → Logging → handler
        var validationBehavior = new ValidationBehavior<ValidatedRequest, ApplicationResult<string>>();
        var loggingBehavior    = new LoggingBehavior<ValidatedRequest, ApplicationResult<string>>(
            NullLogger<LoggingBehavior<ValidatedRequest, ApplicationResult<string>>>.Instance);

        RequestHandlerDelegate<ApplicationResult<string>> handler = () =>
            Task.FromResult(ApplicationResult<string>.Success("result"));

        // Act — logging wraps validation; validation wraps handler
        var result = await loggingBehavior.Handle(
            new ValidatedRequest("valid"),
            () => validationBehavior.Handle(new ValidatedRequest("valid"), handler, CancellationToken.None),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be("result");
    }
}
