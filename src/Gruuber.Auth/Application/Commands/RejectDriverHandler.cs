using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Application.Commands;

public class RejectDriverHandler
{
    private readonly AuthDbContext _db;

    public RejectDriverHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RejectDriverResponse>> HandleAsync(
        RejectDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.Id == command.DriverProfileId, cancellationToken);
        if (profile is null)
            return ApplicationResult<RejectDriverResponse>.Failure("NOT_FOUND", "Driver profile not found.", 404);

        if (profile.Version != command.ExpectedVersion)
            return ApplicationResult<RejectDriverResponse>.Failure(
                "RESOURCE_CONFLICTED", "Driver profile was modified by another request.", 409);

        profile.Reject(command.Reason);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<RejectDriverResponse>.Success(
            new RejectDriverResponse(profile.Id, profile.ApprovalStatus.ToString()));
    }
}
