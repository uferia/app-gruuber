using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Application.Commands;

public class ApproveDriverHandler
{
    private readonly AuthDbContext _db;

    public ApproveDriverHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<ApproveDriverResponse>> HandleAsync(
        ApproveDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.Id == command.DriverProfileId, cancellationToken);
        if (profile is null)
            return ApplicationResult<ApproveDriverResponse>.Failure("NOT_FOUND", "Driver profile not found.", 404);

        if (profile.Version != command.ExpectedVersion)
            return ApplicationResult<ApproveDriverResponse>.Failure(
                "RESOURCE_CONFLICTED", "Driver profile was modified by another request.", 409);

        profile.Approve();
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<ApproveDriverResponse>.Success(
            new ApproveDriverResponse(profile.Id, profile.ApprovalStatus.ToString(), profile.ApprovedAt!.Value));
    }
}
