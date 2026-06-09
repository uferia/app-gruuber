namespace Gruuber.SharedKernel.Domain;

/// <summary>
/// Specification / Filter-Criteria pattern — encapsulates a predicate as a named, composable object.
/// Concrete specs live in each module's Application layer.
/// </summary>
public interface ISpecification<T>
{
    bool IsSatisfiedBy(T candidate);
}

/// <summary>Composes two specs with logical AND.</summary>
public sealed class AndSpecification<T>(ISpecification<T> left, ISpecification<T> right) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) =>
        left.IsSatisfiedBy(candidate) && right.IsSatisfiedBy(candidate);
}

/// <summary>Composes two specs with logical OR.</summary>
public sealed class OrSpecification<T>(ISpecification<T> left, ISpecification<T> right) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) =>
        left.IsSatisfiedBy(candidate) || right.IsSatisfiedBy(candidate);
}

/// <summary>Negates a spec.</summary>
public sealed class NotSpecification<T>(ISpecification<T> inner) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) => !inner.IsSatisfiedBy(candidate);
}

/// <summary>Fluent extension methods for spec composition.</summary>
public static class SpecificationExtensions
{
    public static ISpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right) =>
        new AndSpecification<T>(left, right);

    public static ISpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right) =>
        new OrSpecification<T>(left, right);

    public static ISpecification<T> Not<T>(this ISpecification<T> inner) =>
        new NotSpecification<T>(inner);
}
