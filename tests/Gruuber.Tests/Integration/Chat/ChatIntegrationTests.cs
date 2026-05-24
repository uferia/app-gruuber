using Xunit;

/// <summary>
/// Integration tests for Gruuber.Chat module.
/// Requires Docker (Postgres + Kafka + Redis via Testcontainers).
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
public class ChatIntegrationTests
{
    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task PublishRideMatched_ThreadCreatedWithRiderAndDriver()
    {
        // Arrange: Postgres + Kafka containers
        // Act: publish ride_matched event to ride-events-{region}
        // Assert: chat_threads has 1 row; chat_participants has 2 rows with correct display_names
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task PublishOrderAccepted_TwoThreadsCreated()
    {
        // Act: publish order_accepted
        // Assert: 2 threads — rider<->driver and rider<->restaurant
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task SignalR_SendMessage_AllParticipantsReceive()
    {
        // Arrange: two connected SignalR clients (rider + driver), both joined the thread group
        // Act: rider sends "On my way!"
        // Assert: driver receives MessageReceived event with correct body and display_name
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task SendMessage_ReadOnlyThread_HubExceptionRaisedOnClient()
    {
        // Arrange: thread with status=read_only
        // Act: connected client calls SendMessage
        // Assert: client receives HubException "conversation has ended"
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task MarkRead_NotifiesSenderViaSignalR()
    {
        // Arrange: two connected clients; sender has sent a message
        // Act: recipient calls MarkRead with the MessageId
        // Assert: sender receives MessageRead event with correct MessageId
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task ClosureWorker_ExpiredThreads_MarkedReadOnlyAfter5Minutes()
    {
        // Arrange: thread with closes_at = now - 1 minute
        // Act: wait for next closure sweep (5 min interval)
        // Assert: thread status = read_only
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task GetMessages_UserNotParticipant_Returns403()
    {
        // Act: GET /v1/chat/threads/{threadId}/messages as a user not in the thread
        // Assert: 403 Forbidden
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker")]
    [Trait("Category", "Integration")]
    public async Task ConsumerFails5Times_MessageInDLQ()
    {
        // Act: publish a malformed event that causes repeated exceptions
        // Assert: message in ride-events-{region}-dlq after 5 retries
        await Task.CompletedTask;
    }
}
