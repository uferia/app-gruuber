using Gruuber.Analytics.Application;
using Gruuber.Analytics.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly DriverDashboardQueryHandler _driverHandler;
    private readonly RestaurantDashboardQueryHandler _restaurantHandler;
    private readonly AdminDashboardQueryHandler _adminHandler;
    private readonly ExportJobService _exportService;
    private readonly ICurrentUserContext _currentUser;

    public AnalyticsController(
        DriverDashboardQueryHandler driverHandler,
        RestaurantDashboardQueryHandler restaurantHandler,
        AdminDashboardQueryHandler adminHandler,
        ExportJobService exportService,
        ICurrentUserContext currentUser)
    {
        _driverHandler = driverHandler;
        _restaurantHandler = restaurantHandler;
        _adminHandler = adminHandler;
        _exportService = exportService;
        _currentUser = currentUser;
    }

    // ── DRIVER ──────────────────────────────────────────────────────────

    [HttpGet("driver/summary")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverSummary([FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var result = await _driverHandler.GetSummaryAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("driver/trips")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverTrips(
        [FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _driverHandler.GetTripsAsync(_currentUser.UserId, fromDate, toDate, page, limit, ct);
        return Ok(result);
    }

    [HttpGet("driver/earnings/export")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverExport(
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        if (format is not ("csv" or "pdf"))
            return BadRequest(new { error = "format must be 'csv' or 'pdf'" });

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate)
            return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "driver", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("driver/exports/{jobId:guid}")]
    [Authorize(Policy = "driver")]
    public async Task<IActionResult> DriverExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }

    // ── RESTAURANT ──────────────────────────────────────────────────────

    [HttpGet("restaurant/summary")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantSummary([FromQuery] string period = "daily", CancellationToken ct = default)
    {
        var result = await _restaurantHandler.GetSummaryAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("restaurant/menu-performance")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> MenuPerformance([FromQuery] string period = "weekly", CancellationToken ct = default)
    {
        var result = await _restaurantHandler.GetMenuPerformanceAsync(_currentUser.UserId, period, ct);
        return Ok(result);
    }

    [HttpGet("restaurant/revenue/export")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantExport(
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate) return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "restaurant", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("restaurant/exports/{jobId:guid}")]
    [Authorize(Policy = "restaurant")]
    public async Task<IActionResult> RestaurantExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }

    // ── ADMIN ────────────────────────────────────────────────────────────

    [HttpGet("admin/summary")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminSummary(
        [FromQuery] int region_id = 0,
        [FromQuery] string period = "daily",
        CancellationToken ct = default)
    {
        var effectiveRegion = region_id > 0 ? region_id : _currentUser.RegionId;
        var result = await _adminHandler.GetSummaryAsync(effectiveRegion, period, ct);
        return Ok(result);
    }

    [HttpGet("admin/export")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminExport(
        [FromQuery] int region_id = 0,
        [FromQuery] string format = "csv",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-29));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (fromDate > toDate) return BadRequest(new { error = "from must be before to" });

        var jobId = await _exportService.EnqueueAsync(_currentUser.UserId, "admin", format, fromDate, toDate, ct);
        return Accepted(new { job_id = jobId });
    }

    [HttpGet("admin/exports/{jobId:guid}")]
    [Authorize(Policy = "admin")]
    public async Task<IActionResult> AdminExportStatus(Guid jobId, CancellationToken ct)
    {
        var status = await _exportService.GetStatusAsync(jobId, _currentUser.UserId, ct);
        if (status is null) return NotFound();
        return status.Status == "processing" ? Accepted(status) : Ok(status);
    }
}
