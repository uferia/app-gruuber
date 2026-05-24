using Gruuber.SharedKernel.Results;

namespace Gruuber.Auth.Application;

public interface ICommandHandler<TCommand, TResult>
{
    Task<ApplicationResult<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
