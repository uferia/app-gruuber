# In-App Chat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Gruuber.Chat` module — an isolated, microservice-ready real-time chat system for Rider↔Driver and Rider↔Restaurant conversations, connected to ride/order lifecycle events via Kafka, with REST history access and quick-reply templates.

**Architecture:** New `Gruuber.Chat` project with its own `ChatDbContext` (4 tables: `chat_threads`, `chat_participants`, `chat_messages`, `quick_reply_templates`). Intentionally no FK to rides/orders tables — decoupled for future microservice split. `ChatThreadConsumer` (Kafka `BackgroundService`) listens to `ride_matched` and `order_accepted` to auto-create threads. `ChatHub` (separate from `LocationHub`) uses SignalR with Redis backplane, grouped by `chat:{threadId}`. `ChatThreadClosureWorker` (5-min background sweep) marks threads past `closes_at` as `read_only`. `ChatController` serves REST endpoints under `/v1/chat/`. All display names are anonymized role labels, never real identity.

**Tech Stack:** ASP.NET Core 8, EF Core 8, Npgsql, Confluent.Kafka, Microsoft.AspNetCore.SignalR, StackExchange.Redis, xunit, Moq, FluentAssertions

---

## File Map

**New project:**
- `src/Gruuber.Chat/Gruuber.Chat.csproj`
- `src/Gruuber.Chat/ChatModule.cs`
- `src/Gruuber.Chat/Domain/ChatThread.cs`
- `src/Gruuber.Chat/Domain/ChatParticipant.cs`
- `src/Gruuber.Chat/Domain/ChatMessage.cs`
- `src/Gruuber.Chat/Domain/QuickReplyTemplate.cs`
- `src/Gruuber.Chat/Infrastructure/ChatDbContext.cs`
- `src/Gruuber.Chat/Infrastructure/ChatDbContextFactory.cs`
- `src/Gruuber.Chat/Infrastructure/Migrations/` _(generated)_
- `src/Gruuber.Chat/Application/ChatThreadConsumer.cs`
- `src/Gruuber.Chat/Application/ChatThreadClosureWorker.cs`
- `src/Gruuber.Chat/Application/SendMessageHandler.cs`
- `src/Gruuber.Chat/Application/MarkReadHandler.cs`
- `src/Gruuber.Chat/Application/Queries/ChatQueryHandler.cs`
- `src/Gruuber.Chat/Hubs/ChatHub.cs`

**New controller:**
- `src/Gruuber.Api/Controllers/ChatController.cs`

**Modified files:**
- `src/Gruuber.Api/Program.cs` — register `AddChatModule()`, `app.MapHub<ChatHub>("/hubs/chat")`
- `src/Gruuber.Api/Gruuber.Api.csproj` — add project reference
- `Gruuber.slnx` — add new project
- `tests/Gruuber.Tests/Gruuber.Tests.csproj` — add project reference

---

## Task 1: Create Gruuber.Chat project and domain entities

**Files:**
- Create: `src/Gruuber.Chat/Gruuber.Chat.csproj`
- Create: 4 domain entity files

- [ ] **Step 1: Create the csproj**

```xml
<!-- src/Gruuber.Chat/Gruuber.Chat.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Gruuber.SharedKernel\Gruuber.SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Confluent.Kafka" Version="2.3.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Common" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Add to solution:
```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet sln Gruuber.slnx add src/Gruuber.Chat/Gruuber.Chat.csproj
```

- [ ] **Step 2: Create domain entities**

```csharp
// src/Gruuber.Chat/Domain/ChatThread.cs
namespace Gruuber.Chat.Domain;

public class ChatThread
{
    public Guid ThreadId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// context_type: "ride" | "order".
    /// context_id: the RideId or OrderId — stored as a reference only (no FK to rides/orders).
    /// </summary>
    public string ContextType { get; set; } = string.Empty;
    public Guid ContextId { get; set; }
    public int RegionId { get; set; }
    public string Status { get; set; } = "active";         // active | read_only | closed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosesAt { get; set; }                // null = open ended

    public List<ChatParticipant> Participants { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
}
```

```csharp
// src/Gruuber.Chat/Domain/ChatParticipant.cs
namespace Gruuber.Chat.Domain;

public class ChatParticipant
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Anonymized display name — e.g. "Your Driver", "Your Rider", "Restaurant Staff".
    /// Never contains PII.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>rider | driver | restaurant</summary>
    public string Role { get; set; } = string.Empty;

    public ChatThread Thread { get; set; } = null!;
}
```

```csharp
// src/Gruuber.Chat/Domain/ChatMessage.cs
namespace Gruuber.Chat.Domain;

public class ChatMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsQuickReply { get; set; }
    public string DeliveryStatus { get; set; } = "sent";   // sent | delivered | read
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public ChatThread Thread { get; set; } = null!;
}
```

```csharp
// src/Gruuber.Chat/Domain/QuickReplyTemplate.cs
namespace Gruuber.Chat.Domain;

public class QuickReplyTemplate
{
    public int Id { get; set; }

    /// <summary>rider | driver | restaurant</summary>
    public string Role { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Gruuber.Chat/
git commit -m "feat(chat): create Gruuber.Chat project and domain entities"
```

---

## Task 2: ChatDbContext and EF migration

**Files:**
- Create: `src/Gruuber.Chat/Infrastructure/ChatDbContext.cs`
- Create: `src/Gruuber.Chat/Infrastructure/ChatDbContextFactory.cs`

- [ ] **Step 1: Create ChatDbContext**

```csharp
// src/Gruuber.Chat/Infrastructure/ChatDbContext.cs
using Gruuber.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Chat.Infrastructure;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<ChatThread> Threads => Set<ChatThread>();
    public DbSet<ChatParticipant> Participants => Set<ChatParticipant>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<QuickReplyTemplate> QuickReplyTemplates => Set<QuickReplyTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatThread>(e =>
        {
            e.ToTable("chat_threads");
            e.HasKey(x => x.ThreadId);
            e.HasIndex(x => new { x.ContextType, x.ContextId });
            e.HasIndex(x => new { x.Status, x.ClosesAt });
            e.HasMany(x => x.Participants)
                .WithOne(x => x.Thread)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Thread)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatParticipant>(e =>
        {
            e.ToTable("chat_participants");
            e.HasKey(x => new { x.ThreadId, x.UserId });
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(x => x.MessageId);
            e.HasIndex(x => new { x.ThreadId, x.SentAt });
            e.HasIndex(x => x.SenderId);
        });

        modelBuilder.Entity<QuickReplyTemplate>(e =>
        {
            e.ToTable("quick_reply_templates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Role, x.Locale });
        });
    }
}
```

- [ ] **Step 2: Create ChatDbContextFactory**

```csharp
// src/Gruuber.Chat/Infrastructure/ChatDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Chat.Infrastructure;

public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber_chat;Username=postgres;Password=postgres")
            .Options;
        return new ChatDbContext(options);
    }
}
```

- [ ] **Step 3: Add project reference to Gruuber.Api**

In `src/Gruuber.Api/Gruuber.Api.csproj`, add:

```xml
<ProjectReference Include="..\Gruuber.Chat\Gruuber.Chat.csproj" />
```

- [ ] **Step 4: Generate migration**

```bash
cd c:\Projects\app-gruuber.worktrees\copilot-feature-brainstorming-session
dotnet ef migrations add InitialCreate --project src/Gruuber.Chat/Gruuber.Chat.csproj --startup-project src/Gruuber.Api/Gruuber.Api.csproj
```

Expected: migration files generated in `src/Gruuber.Chat/Infrastructure/Migrations/`.

- [ ] **Step 5: Build**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Chat/Infrastructure/ src/Gruuber.Api/Gruuber.Api.csproj
git commit -m "feat(chat): add ChatDbContext, DbContextFactory, and initial EF migration"
```

---

## Task 3: ChatHub (SignalR)

**Files:**
- Create: `src/Gruuber.Chat/Hubs/ChatHub.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Gruuber.Tests/Unit/Chat/ChatHubTests.cs
using Gruuber.Chat.Application;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Hubs;
using Gruuber.Chat.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ChatHubTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task JoinThread_AddsCallerToGroup_AndMarksMessagesDelivered()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var thread = new ChatThread { ThreadId = threadId, ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 };
        thread.Participants.Add(new ChatParticipant { ThreadId = threadId, UserId = userId, DisplayName = "Your Rider", Role = "rider" });
        thread.Messages.Add(new ChatMessage { MessageId = Guid.NewGuid(), ThreadId = threadId, SenderId = Guid.NewGuid(), Body = "Hi", DeliveryStatus = "sent" });
        db.Threads.Add(thread);
        await db.SaveChangesAsync();

        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-1");
        mockContext.Setup(x => x.UserIdentifier).Returns(userId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await hub.JoinThread(threadId);

        mockGroups.Verify(g => g.AddToGroupAsync("conn-1", $"chat:{threadId}", default), Times.Once);

        var updated = await db.Messages.Where(m => m.ThreadId == threadId).ToListAsync();
        Assert.All(updated, m => Assert.Equal("delivered", m.DeliveryStatus));
    }

    [Fact]
    public async Task JoinThread_ThreadNotFound_ThrowsHubException()
    {
        await using var db = CreateInMemoryDb();
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-1");
        mockContext.Setup(x => x.UserIdentifier).Returns(Guid.NewGuid().ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await Assert.ThrowsAsync<HubException>(() => hub.JoinThread(Guid.NewGuid()));
    }

    [Fact]
    public async Task SendMessage_ToReadOnlyThread_ThrowsHubException()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Threads.Add(new ChatThread
        {
            ThreadId = threadId,
            ContextType = "ride",
            ContextId = Guid.NewGuid(),
            RegionId = 1,
            Status = "read_only"  // closed thread
        });
        await db.SaveChangesAsync();

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns(userId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = new Mock<IHubCallerClients>().Object,
            Groups = new Mock<IGroupManager>().Object,
            Context = mockContext.Object
        };

        await Assert.ThrowsAsync<HubException>(() =>
            hub.SendMessage(threadId, "Hello?", isQuickReply: false));
    }

    [Fact]
    public async Task SendMessage_ValidThread_PersistsMessageAndBroadcastsToGroup()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        db.Threads.Add(new ChatThread
        {
            ThreadId = threadId,
            ContextType = "ride",
            ContextId = Guid.NewGuid(),
            RegionId = 1,
            Status = "active"
        });
        db.Participants.Add(new ChatParticipant
        {
            ThreadId = threadId, UserId = senderId,
            DisplayName = "Your Driver", Role = "driver"
        });
        await db.SaveChangesAsync();

        var mockGroupClient = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubCallerClients>();
        mockClients.Setup(c => c.Group($"chat:{threadId}")).Returns(mockGroupClient.Object);
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns(senderId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = new Mock<IGroupManager>().Object,
            Context = mockContext.Object
        };

        await hub.SendMessage(threadId, "On my way!", isQuickReply: false);

        var savedMsg = await db.Messages.SingleAsync(m => m.ThreadId == threadId);
        Assert.Equal("On my way!", savedMsg.Body);
        Assert.Equal(senderId, savedMsg.SenderId);

        mockGroupClient.Verify(
            c => c.SendCoreAsync("MessageReceived", It.IsAny<object[]>(), default),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatHubTests" -v minimal
```

Expected: compile error — `ChatHub` not found.

- [ ] **Step 3: Implement ChatHub**

```csharp
// src/Gruuber.Chat/Hubs/ChatHub.cs
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatDbContext _db;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ChatDbContext db, ILogger<ChatHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Caller joins the SignalR group for this thread.
    /// Marks any unread messages sent to the caller as "delivered".
    /// </summary>
    public async Task JoinThread(Guid threadId)
    {
        var thread = await _db.Threads
            .Include(t => t.Participants)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ThreadId == threadId);

        if (thread is null)
            throw new HubException("Thread not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{threadId}");

        // Mark unread (non-sender) messages as delivered
        var callerId = Guid.Parse(Context.UserIdentifier!);
        var undelivered = thread.Messages
            .Where(m => m.SenderId != callerId && m.DeliveryStatus == "sent")
            .ToList();

        foreach (var msg in undelivered)
            msg.DeliveryStatus = "delivered";

        if (undelivered.Count > 0)
            await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} joined chat thread {ThreadId}", callerId, threadId);
    }

    /// <summary>
    /// Persists a new message and broadcasts to the thread group.
    /// Throws HubException if the thread is read_only or closed.
    /// </summary>
    public async Task SendMessage(Guid threadId, string body, bool isQuickReply)
    {
        var thread = await _db.Threads
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.ThreadId == threadId);

        if (thread is null)
            throw new HubException("Thread not found.");

        if (thread.Status is "read_only" or "closed")
            throw new HubException("This conversation has ended and is no longer accepting messages.");

        var senderId = Guid.Parse(Context.UserIdentifier!);
        var participant = thread.Participants.FirstOrDefault(p => p.UserId == senderId);
        if (participant is null)
            throw new HubException("You are not a participant in this thread.");

        var message = new ChatMessage
        {
            ThreadId = threadId,
            SenderId = senderId,
            Body = body,
            IsQuickReply = isQuickReply,
            DeliveryStatus = "sent",
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Message {MessageId} sent in thread {ThreadId} by {UserId}",
            message.MessageId, threadId, senderId);

        await Clients.Group($"chat:{threadId}").SendCoreAsync("MessageReceived", new object[]
        {
            new
            {
                message.MessageId,
                message.ThreadId,
                SenderDisplayName = participant.DisplayName,
                message.Body,
                message.IsQuickReply,
                message.DeliveryStatus,
                message.SentAt
            }
        });
    }

    /// <summary>
    /// Caller marks messages as read. Notifies sender of read receipt via SignalR.
    /// </summary>
    public async Task MarkRead(Guid threadId, Guid[] messageIds)
    {
        var callerId = Guid.Parse(Context.UserIdentifier!);
        var messages = await _db.Messages
            .Where(m => m.ThreadId == threadId && messageIds.Contains(m.MessageId)
                        && m.SenderId != callerId && m.DeliveryStatus != "read")
            .ToListAsync();

        if (messages.Count == 0) return;

        foreach (var msg in messages)
            msg.DeliveryStatus = "read";

        await _db.SaveChangesAsync();

        // Notify sender(s) of read receipts — group by sender
        foreach (var senderGroup in messages.GroupBy(m => m.SenderId))
        {
            var readIds = senderGroup.Select(m => m.MessageId).ToArray();
            await Clients.Group($"chat:{threadId}").SendCoreAsync("MessageRead", new object[]
            {
                new { ThreadId = threadId, MessageIds = readIds, ReadAt = DateTime.UtcNow }
            });
        }
    }
}
```

- [ ] **Step 4: Add Chat project reference to tests**

In `tests/Gruuber.Tests/Gruuber.Tests.csproj`, add:

```xml
<ProjectReference Include="..\..\src\Gruuber.Chat\Gruuber.Chat.csproj" />
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatHubTests" -v minimal
```

Expected: 4 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Chat/Hubs/ChatHub.cs
git add tests/Gruuber.Tests/Unit/Chat/ChatHubTests.cs
git add tests/Gruuber.Tests/Gruuber.Tests.csproj
git commit -m "feat(chat): implement ChatHub with JoinThread, SendMessage, MarkRead; 4 unit tests"
```

---

## Task 4: ChatThreadConsumer (Kafka → auto-create threads)

**Files:**
- Create: `src/Gruuber.Chat/Application/ChatThreadConsumer.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Gruuber.Tests/Unit/Chat/ChatThreadConsumerTests.cs
using Gruuber.Chat.Application;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ChatThreadConsumerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task ProcessRideMatched_CreatesOneThreadWithTwoParticipants()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var rideId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_matched"",
            ""RideId"": ""{rideId}"",
            ""RiderId"": ""{riderId}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var threads = await db.Threads.Include(t => t.Participants).ToListAsync();
        Assert.Single(threads);

        var thread = threads[0];
        Assert.Equal("ride", thread.ContextType);
        Assert.Equal(rideId, thread.ContextId);
        Assert.Equal(2, thread.Participants.Count);

        var riderParticipant = thread.Participants.FirstOrDefault(p => p.UserId == riderId);
        Assert.NotNull(riderParticipant);
        Assert.Equal("Your Rider", riderParticipant!.DisplayName);
        Assert.Equal("rider", riderParticipant.Role);

        var driverParticipant = thread.Participants.FirstOrDefault(p => p.UserId == driverId);
        Assert.NotNull(driverParticipant);
        Assert.Equal("Your Driver", driverParticipant!.DisplayName);
        Assert.Equal("driver", driverParticipant.Role);
    }

    [Fact]
    public async Task ProcessOrderAccepted_CreatesTwoThreads_RiderDriverAndRiderRestaurant()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var orderId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""order_accepted"",
            ""OrderId"": ""{orderId}"",
            ""RiderId"": ""{riderId}"",
            ""DriverId"": ""{driverId}"",
            ""RestaurantId"": ""{restaurantId}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var threads = await db.Threads.Include(t => t.Participants).ToListAsync();
        Assert.Equal(2, threads.Count);

        // Thread 1: rider ↔ driver
        var riderDriverThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "driver") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        Assert.NotNull(riderDriverThread);

        // Thread 2: rider ↔ restaurant
        var riderRestaurantThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "restaurant") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        Assert.NotNull(riderRestaurantThread);
    }

    [Fact]
    public async Task ProcessRideMatched_IdempotentOnDuplicateEvent_ThreadCreatedOnlyOnce()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var rideId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_matched"",
            ""RideId"": ""{rideId}"",
            ""RiderId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{Guid.NewGuid()}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);
        await processor.ProcessAsync(payload, CancellationToken.None); // duplicate

        Assert.Single(await db.Threads.ToListAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatThreadConsumerTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement ChatThreadConsumer**

```csharp
// src/Gruuber.Chat/Application/ChatThreadConsumer.cs
using System.Text.Json;
using Confluent.Kafka;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Application;

/// <summary>
/// Kafka BackgroundService — listens to ride and order events and creates chat threads.
/// Topics: ride-events-{region} (filters ride_matched), order-events-{region} (filters order_accepted).
/// </summary>
public class ChatThreadConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatThreadConsumer> _logger;

    public ChatThreadConsumer(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<ChatThreadConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var groupId = _configuration["Kafka:ChatGroupId"] ?? "gruuber-chat";
        var regions = _configuration.GetSection("Kafka:RideRegions").Get<int[]>() ?? [1];

        var topics = regions.SelectMany(r => new[]
        {
            $"ride-events-{r}",
            $"order-events-{r}"
        }).ToList();

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topics);
        _logger.LogInformation("ChatThreadConsumer subscribed to: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            var retryCount = 0;
            try
            {
                result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null) continue;

                bool success = false;
                while (retryCount <= 5 && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                        var processor = new ChatEventProcessor(db, _logger);
                        await processor.ProcessAsync(result.Message.Value, stoppingToken);
                        consumer.Commit(result);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "ChatThreadConsumer failed (attempt {Attempt})", retryCount);
                        if (retryCount <= 5)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount - 1)), stoppingToken);
                    }
                }

                if (!success)
                {
                    _logger.LogError("ChatThreadConsumer routing to DLQ after 5 failures on topic {Topic}", result?.Topic);
                    consumer.Commit(result!);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatThreadConsumer unexpected error");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
    }
}

/// <summary>
/// Processes a single chat event payload. Extracted for unit testability.
/// Idempotent: checks for existing thread with same context_type + context_id before creating.
/// </summary>
public class ChatEventProcessor
{
    private readonly ChatDbContext _db;
    private readonly ILogger _logger;
    private static readonly TimeSpan ThreadLifetime = TimeSpan.FromHours(24);

    public ChatEventProcessor(ChatDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessAsync(string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (!root.TryGetProperty("EventName", out var en)) return;

        switch (en.GetString())
        {
            case "ride_matched": await HandleRideMatched(root, ct); break;
            case "order_accepted": await HandleOrderAccepted(root, ct); break;
        }
    }

    private async Task HandleRideMatched(JsonElement root, CancellationToken ct)
    {
        var rideId = root.GetProperty("RideId").GetGuid();
        var riderId = root.GetProperty("RiderId").GetGuid();
        var driverId = root.GetProperty("DriverId").GetGuid();
        var regionId = root.TryGetProperty("RegionId", out var r) ? r.GetInt32() : 1;

        // Idempotency: don't create a duplicate thread for the same ride
        var existing = await _db.Threads
            .AnyAsync(t => t.ContextType == "ride" && t.ContextId == rideId, ct);
        if (existing) return;

        var thread = new ChatThread
        {
            ContextType = "ride",
            ContextId = rideId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = DateTime.UtcNow.Add(ThreadLifetime)
        };
        thread.Participants.AddRange([
            new ChatParticipant { ThreadId = thread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = thread.ThreadId, UserId = driverId, DisplayName = "Your Driver", Role = "driver" }
        ]);

        _db.Threads.Add(thread);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Chat thread {ThreadId} created for ride {RideId}", thread.ThreadId, rideId);
    }

    private async Task HandleOrderAccepted(JsonElement root, CancellationToken ct)
    {
        var orderId = root.GetProperty("OrderId").GetGuid();
        var riderId = root.GetProperty("RiderId").GetGuid();
        var driverId = root.GetProperty("DriverId").GetGuid();
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var regionId = root.TryGetProperty("RegionId", out var r) ? r.GetInt32() : 1;

        // Idempotency: skip if threads already exist for this order
        var existingCount = await _db.Threads
            .CountAsync(t => t.ContextType == "order" && t.ContextId == orderId, ct);
        if (existingCount >= 2) return;

        var closesAt = DateTime.UtcNow.Add(ThreadLifetime);

        // Thread 1: rider ↔ driver
        var riderDriverThread = new ChatThread
        {
            ContextType = "order",
            ContextId = orderId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = closesAt
        };
        riderDriverThread.Participants.AddRange([
            new ChatParticipant { ThreadId = riderDriverThread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = riderDriverThread.ThreadId, UserId = driverId, DisplayName = "Your Driver", Role = "driver" }
        ]);

        // Thread 2: rider ↔ restaurant
        var riderRestaurantThread = new ChatThread
        {
            ContextType = "order",
            ContextId = orderId,
            RegionId = regionId,
            Status = "active",
            ClosesAt = closesAt
        };
        riderRestaurantThread.Participants.AddRange([
            new ChatParticipant { ThreadId = riderRestaurantThread.ThreadId, UserId = riderId, DisplayName = "Your Rider", Role = "rider" },
            new ChatParticipant { ThreadId = riderRestaurantThread.ThreadId, UserId = restaurantId, DisplayName = "Restaurant Staff", Role = "restaurant" }
        ]);

        _db.Threads.AddRange(riderDriverThread, riderRestaurantThread);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Chat threads created for order {OrderId}: {Thread1}, {Thread2}",
            orderId, riderDriverThread.ThreadId, riderRestaurantThread.ThreadId);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatThreadConsumerTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Chat/Application/ChatThreadConsumer.cs
git add tests/Gruuber.Tests/Unit/Chat/ChatThreadConsumerTests.cs
git commit -m "feat(chat): add ChatThreadConsumer and ChatEventProcessor with idempotent thread creation"
```

---

## Task 5: ChatThreadClosureWorker

**Files:**
- Create: `src/Gruuber.Chat/Application/ChatThreadClosureWorker.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/Gruuber.Tests/Unit/Chat/ChatThreadClosureWorkerTests.cs
using Gruuber.Chat.Application;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class ChatThreadClosureWorkerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task SweepExpired_MarksThreadsPassedClosesAtAsReadOnly()
    {
        await using var db = CreateInMemoryDb();

        // One expired thread (closes_at in the past)
        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = DateTime.UtcNow.AddMinutes(-10)  // expired
        });

        // One active thread with future closes_at
        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = DateTime.UtcNow.AddHours(10)     // still open
        });

        // One thread with no closes_at (open-ended — never auto-closed)
        db.Threads.Add(new ChatThread
        {
            ContextType = "order", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = null
        });

        await db.SaveChangesAsync();

        var worker = new ChatThreadClosureService(db, NullLogger<ChatThreadClosureService>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var threads = await db.Threads.OrderBy(t => t.ClosesAt).ToListAsync();
        Assert.Equal("read_only", threads[0].Status);   // expired one is read_only
        Assert.Equal("active", threads[1].Status);      // future one unchanged
        Assert.Equal("active", threads[2].Status);      // open-ended unchanged
    }

    [Fact]
    public async Task SweepExpired_AlreadyReadOnly_NoStateChange()
    {
        await using var db = CreateInMemoryDb();

        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "read_only",   // already closed
            ClosesAt = DateTime.UtcNow.AddMinutes(-60)
        });
        await db.SaveChangesAsync();

        var worker = new ChatThreadClosureService(db, NullLogger<ChatThreadClosureService>.Instance);
        await worker.SweepAsync(CancellationToken.None);

        var thread = await db.Threads.SingleAsync();
        Assert.Equal("read_only", thread.Status);       // no change, no exception
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatThreadClosureWorkerTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement ChatThreadClosureWorker**

```csharp
// src/Gruuber.Chat/Application/ChatThreadClosureWorker.cs
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Application;

/// <summary>
/// Background worker that sweeps every 5 minutes and marks threads past closes_at as read_only.
/// </summary>
public class ChatThreadClosureWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatThreadClosureWorker> _logger;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    public ChatThreadClosureWorker(IServiceScopeFactory scopeFactory, ILogger<ChatThreadClosureWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                var service = new ChatThreadClosureService(db, _logger);
                await service.SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatThreadClosureWorker sweep failed");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }
}

/// <summary>
/// Encapsulates the sweep logic (extracted for unit testability without BackgroundService plumbing).
/// </summary>
public class ChatThreadClosureService
{
    private readonly ChatDbContext _db;
    private readonly ILogger _logger;

    public ChatThreadClosureService(ChatDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Threads
            .Where(t => t.Status == "active" && t.ClosesAt != null && t.ClosesAt <= now)
            .ToListAsync(ct);

        foreach (var thread in expired)
            thread.Status = "read_only";

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("ChatThreadClosureWorker marked {Count} threads as read_only", expired.Count);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatThreadClosureWorkerTests" -v minimal
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Gruuber.Chat/Application/ChatThreadClosureWorker.cs
git add tests/Gruuber.Tests/Unit/Chat/ChatThreadClosureWorkerTests.cs
git commit -m "feat(chat): add ChatThreadClosureWorker that marks expired threads as read_only every 5 min"
```

---

## Task 6: REST handlers and ChatController

**Files:**
- Create: `src/Gruuber.Chat/Application/Queries/ChatQueryHandler.cs`
- Create: `src/Gruuber.Api/Controllers/ChatController.cs`

- [ ] **Step 1: Write failing unit tests**

```csharp
// tests/Gruuber.Tests/Unit/Chat/ChatQueryHandlerTests.cs
using Gruuber.Chat.Application.Queries;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class ChatQueryHandlerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task GetThreadsForUser_ReturnsOnlyThreadsUserIsParticipantIn()
    {
        await using var db = CreateInMemoryDb();
        var userId = Guid.NewGuid();
        var contextId = Guid.NewGuid();

        var thread1 = new ChatThread { ContextType = "ride", ContextId = contextId, RegionId = 1 };
        thread1.Participants.Add(new ChatParticipant { ThreadId = thread1.ThreadId, UserId = userId, DisplayName = "Your Rider", Role = "rider" });

        var thread2 = new ChatThread { ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 };
        // user is NOT a participant in thread2

        db.Threads.AddRange(thread1, thread2);
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetThreadsAsync(userId, contextId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(thread1.ThreadId, result[0].ThreadId);
    }

    [Fact]
    public async Task GetMessages_Paginated_ReturnsOldestFirst()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        db.Threads.Add(new ChatThread { ThreadId = threadId, ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 });
        for (int i = 0; i < 10; i++)
        {
            db.Messages.Add(new ChatMessage
            {
                ThreadId = threadId, SenderId = senderId,
                Body = $"Message {i}",
                SentAt = DateTime.UtcNow.AddMinutes(-10 + i)
            });
        }
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetMessagesAsync(threadId, page: 1, limit: 5, CancellationToken.None);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal("Message 0", result.Items[0].Body);  // oldest first
        Assert.Equal(10, result.Total);
    }

    [Fact]
    public async Task GetQuickReplies_ReturnsOnlyMatchingRoleAndLocale()
    {
        await using var db = CreateInMemoryDb();
        db.QuickReplyTemplates.AddRange(
            new QuickReplyTemplate { Role = "driver", Body = "On my way", Locale = "en", IsActive = true },
            new QuickReplyTemplate { Role = "rider", Body = "Please hurry", Locale = "en", IsActive = true },
            new QuickReplyTemplate { Role = "driver", Body = "Estoy en camino", Locale = "es", IsActive = true }
        );
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetQuickRepliesAsync("driver", "en", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("On my way", result[0].Body);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatQueryHandlerTests" -v minimal
```

Expected: compile error.

- [ ] **Step 3: Implement ChatQueryHandler**

```csharp
// src/Gruuber.Chat/Application/Queries/ChatQueryHandler.cs
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Chat.Application.Queries;

public class ChatQueryHandler
{
    private readonly ChatDbContext _db;
    public ChatQueryHandler(ChatDbContext db) => _db = db;

    /// <summary>
    /// Returns threads for a given user, optionally filtered by context_id (rideId or orderId).
    /// Only returns threads the user is a participant in.
    /// </summary>
    public async Task<List<ThreadSummaryResponse>> GetThreadsAsync(Guid userId, Guid? contextId, CancellationToken ct)
    {
        var query = _db.Threads
            .Include(t => t.Participants)
            .Where(t => t.Participants.Any(p => p.UserId == userId));

        if (contextId.HasValue)
            query = query.Where(t => t.ContextId == contextId.Value);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ThreadSummaryResponse(
                t.ThreadId,
                t.ContextType,
                t.ContextId,
                t.Status,
                t.CreatedAt,
                t.ClosesAt,
                t.Participants
                    .Select(p => new ParticipantInfo(p.UserId, p.DisplayName, p.Role))
                    .ToList()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns messages in a thread, paginated oldest-first.
    /// Caller must verify they are a thread participant before invoking.
    /// </summary>
    public async Task<PagedChatResponse<MessageResponse>> GetMessagesAsync(Guid threadId, int page, int limit, CancellationToken ct)
    {
        var query = _db.Messages
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.SentAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * limit).Take(limit)
            .Select(m => new MessageResponse(m.MessageId, m.ThreadId, m.SenderId, m.Body, m.IsQuickReply, m.DeliveryStatus, m.SentAt))
            .ToListAsync(ct);

        return new PagedChatResponse<MessageResponse>(items, total, page, limit);
    }

    /// <summary>Returns active quick reply templates for a role and locale.</summary>
    public async Task<List<QuickReplyResponse>> GetQuickRepliesAsync(string role, string locale, CancellationToken ct)
    {
        return await _db.QuickReplyTemplates
            .Where(q => q.Role == role && q.Locale == locale && q.IsActive)
            .Select(q => new QuickReplyResponse(q.Id, q.Body))
            .ToListAsync(ct);
    }
}

public record ThreadSummaryResponse(Guid ThreadId, string ContextType, Guid ContextId,
    string Status, DateTime CreatedAt, DateTime? ClosesAt, List<ParticipantInfo> Participants);
public record ParticipantInfo(Guid UserId, string DisplayName, string Role);
public record MessageResponse(Guid MessageId, Guid ThreadId, Guid SenderId, string Body,
    bool IsQuickReply, string DeliveryStatus, DateTime SentAt);
public record QuickReplyResponse(int Id, string Body);
public record PagedChatResponse<T>(List<T> Items, int Total, int Page, int Limit);
```

- [ ] **Step 4: Create ChatController**

```csharp
// src/Gruuber.Api/Controllers/ChatController.cs
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

    /// <summary>
    /// Returns threads the calling user is a participant in.
    /// Optional filter by context_id (ride or order ID).
    /// </summary>
    [HttpGet("threads")]
    public async Task<IActionResult> GetThreads(
        [FromQuery] Guid? context_id = null,
        CancellationToken ct = default)
    {
        var threads = await _queryHandler.GetThreadsAsync(_currentUser.UserId, context_id, ct);
        return Ok(threads);
    }

    /// <summary>
    /// Returns paginated messages for a thread.
    /// Returns 403 if the calling user is not a participant.
    /// </summary>
    [HttpGet("threads/{threadId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid threadId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        // Authorization: only participants can read messages — verified by checking thread membership
        var threads = await _queryHandler.GetThreadsAsync(_currentUser.UserId, null, ct);
        var hasAccess = threads.Any(t => t.ThreadId == threadId);
        if (!hasAccess) return Forbid();

        var messages = await _queryHandler.GetMessagesAsync(threadId, page, limit, ct);
        return Ok(messages);
    }

    /// <summary>
    /// Returns quick reply templates for a role and locale.
    /// Locale defaults to "en".
    /// </summary>
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
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "ChatQueryHandlerTests" -v minimal
```

Expected: 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Gruuber.Chat/Application/Queries/ChatQueryHandler.cs
git add src/Gruuber.Api/Controllers/ChatController.cs
git add tests/Gruuber.Tests/Unit/Chat/ChatQueryHandlerTests.cs
git commit -m "feat(chat): add ChatQueryHandler and ChatController with threads, messages, quick-replies"
```

---

## Task 7: ChatModule and Program.cs wiring

**Files:**
- Create: `src/Gruuber.Chat/ChatModule.cs`
- Modify: `src/Gruuber.Api/Program.cs`

- [ ] **Step 1: Create ChatModule**

```csharp
// src/Gruuber.Chat/ChatModule.cs
using Gruuber.Chat.Application;
using Gruuber.Chat.Application.Queries;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gruuber.Chat;

public static class ChatModule
{
    public static IServiceCollection AddChatModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ChatDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ChatDb")
                ?? configuration.GetConnectionString("Default")));

        services.AddScoped<ChatQueryHandler>();
        services.AddHostedService<ChatThreadConsumer>();
        services.AddHostedService<ChatThreadClosureWorker>();

        return services;
    }
}
```

- [ ] **Step 2: Register in Program.cs**

In `src/Gruuber.Api/Program.cs`:

```csharp
using Gruuber.Chat;
using Gruuber.Chat.Hubs;

// In builder.Services section (after other modules):
builder.Services.AddChatModule(builder.Configuration);

// After app.Build(), in the hub mapping section:
app.MapHub<ChatHub>("/hubs/chat");
```

- [ ] **Step 3: Build and run all unit tests**

```bash
dotnet build Gruuber.slnx -c Release --no-incremental 2>&1 | tail -5
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: Build succeeds; all unit tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Gruuber.Chat/ChatModule.cs src/Gruuber.Api/Program.cs
git commit -m "feat(chat): register ChatModule and ChatHub in Program.cs"
```

---

## Task 8: Integration test stubs

**Files:**
- Create: `tests/Gruuber.Tests/Integration/Chat/ChatIntegrationTests.cs`

- [ ] **Step 1: Create integration test stubs**

```csharp
// tests/Gruuber.Tests/Integration/Chat/ChatIntegrationTests.cs
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
        // Assert: 2 threads — rider↔driver and rider↔restaurant
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
```

- [ ] **Step 2: Run all unit tests to confirm no regressions**

```bash
dotnet test tests/Gruuber.Tests/Gruuber.Tests.csproj --filter "Category!=Integration" -v minimal
```

Expected: all unit tests PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Gruuber.Tests/Integration/Chat/
git commit -m "test(chat): add integration test stubs for thread creation, SignalR, read receipts, and closure"
```

---

## Completion Checklist

- [ ] All REST endpoints under `/v1/chat/`
- [ ] `ChatHub` mapped at `/hubs/chat` (separate from `/hubs/location`)
- [ ] `ChatHub` groups by `chat:{threadId}` (not `ride:{rideId}` or `location`)
- [ ] No FK relationships to `rides` or `orders` tables — context stored as plain `Guid`
- [ ] Display names are always anonymized role labels — never real names or phone numbers
- [ ] `chat_threads.status` transitions: `active → read_only` (by closure worker), never `active → closed` directly
- [ ] `ChatThreadClosureWorker` only affects threads where `closes_at IS NOT NULL AND closes_at <= NOW()`
- [ ] `ChatThreadConsumer` idempotent: `ride_matched` creates at most 1 thread per `RideId`; `order_accepted` creates at most 2 threads per `OrderId`
- [ ] Kafka consumer retries: max 5 attempts with exponential backoff + jitter, then DLQ
- [ ] `CancellationToken` propagated in all async handlers, Kafka loops, and background workers
- [ ] Messages returned oldest-first (ordered by `SentAt ASC`)
- [ ] `GET /v1/chat/threads/{threadId}/messages` returns 403 if user is not a participant
