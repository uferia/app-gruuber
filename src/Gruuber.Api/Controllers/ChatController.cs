using Gruuber.Chat.Application.Queries;
using Gruuber.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gruuber.Api.Controllers;

[ApiController]
[Route("v1/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ChatQueryHandler _queryHandler;
    private readonly ICurrentUserContext _currentUser;

    public ChatController(ChatQueryHandler queryHandler, ICurrentUserContext currentUser)
    {
        _queryHandler = queryHandler;
        _currentUser = currentUser;
    }

    [HttpGet("threads")]
    public async Task<IActionResult> GetThreads(
        [FromQuery] Guid? context_id = null,
        CancellationToken ct = default)
    {
        var threads = await _queryHandler.GetThreadsAsync(_currentUser.UserId, context_id, ct);
        return Ok(threads);
    }

    [HttpGet("threads/{threadId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid threadId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var threads = await _queryHandler.GetThreadsAsync(_currentUser.UserId, null, ct);
        var hasAccess = threads.Any(t => t.ThreadId == threadId);
        if (!hasAccess) return Forbid();

        var messages = await _queryHandler.GetMessagesAsync(threadId, page, limit, ct);
        return Ok(messages);
    }

    [HttpGet("quick-replies")]
    public async Task<IActionResult> GetQuickReplies(
        [FromQuery] string role,
        [FromQuery] string locale = "en",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            return BadRequest(new { error = "role is required" });

        var replies = await _queryHandler.GetQuickRepliesAsync(role, locale, ct);
        return Ok(replies);
    }
}
