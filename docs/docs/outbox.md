# Outbox

{== Outbox? Who? What? ==} {>> Should we describe it? <<}


## Quick Start

The quick start example uses [EF Core](https://docs.microsoft.com/en-us/ef/core/) and Postgres to persist domain models and outbox messages.

1. Add outbox producer configuration
2. Setup persistence
    1. Configure EF Core
    2. Configure DbContext
    3. Prepare Outbox schema
    4. Implement the Outbox repository
    5. Implement Unit of Work
3. Use the Outbox

### Add outbox producer configuration

Add Kafka producer configuration and outgoing messages:

```csharp
public class Startup
{
    ...

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // configure messaging: producer
        services.AddOutbox(options =>
        {
            // configuration settings
            options.WithBootstrapServers("http://localhost:9092");

            // register outgoing messages (includes outbox messages)
            options.Register<Test>("test-topic", "test-event", @event => @event.AggregateId);

            // include outbox (polling publisher)
            options.WithOutboxMessageRepository<OutboxMessageRepository>();
            options.WithUnitOfWorkFactory<OutboxUnitOfWorkFactory>();
        });

        ...
    }
}
```

### Setup persistence

#### Configure EF Core

To enable persistence, configure EF Core:

```csharp
public class Startup
{
    ...

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        ...
        // configure DbContext
        services
            .AddEntityFrameworkNpgsql()
            .AddDbContext<SampleDbContext>(options => options.UseNpgsql(connectionString));

    }
}
```

#### Configure DbContext

Add a `DbContext` and configure the `OutboxMessage` model:

```csharp
    public class SampleDbContext : DbContext
    {
        public SampleDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ...

            modelBuilder.Entity<OutboxMessage>(cfg =>
            {
                cfg.ToTable("OutboxMessage", "outbox");
                cfg.HasKey(x => x.MessageId);
                cfg.Property(x => x.MessageId);
                cfg.Property(x => x.CorrelationId);
                cfg.Property(x => x.Topic);
                cfg.Property(x => x.Key);
                cfg.Property(x => x.Type);
                cfg.Property(x => x.Format);
                cfg.Property(x => x.Data);
                cfg.Property(x => x.OccurredOnUtc);
                cfg.Property(x => x.ProcessedUtc);
            });
        }
```

#### Prepare Outbox schema

Run the following `Postgresql`:

```sql
CREATE SCHEMA IF NOT EXISTS outbox;

CREATE TABLE outbox."OutboxMessage" (
    "MessageId" uuid NOT NULL,
    "CorrelationId" varchar(255) NOT NULL,
    "Topic" varchar(255) NOT NULL,
    "Key" varchar(255) NOT NULL,
    "Type" varchar(255) NOT NULL,
    "Format" varchar(255) NOT NULL,
    "Data" text NOT NULL,
    "OccurredOnUtc" timestamp NOT NULL,
    "ProcessedUtc" timestamp NULL,

    CONSTRAINT domainevent_pk PRIMARY KEY ("MessageId")
);

CREATE INDEX domainevent_processedutc_idx ON outbox."OutboxMessage" ("ProcessedUtc" NULLS FIRST);

CREATE INDEX domainevent_occurredonutc_idx ON outbox."OutboxMessage" ("OccurredOnUtc" ASC);

```

#### Implement the Outbox repository

Implement the `IOutboxMessageRepository`:

```csharp
public class OutboxMessageRepository : IOutboxMessageRepository
{
    private readonly SampleDbContext _dbContext;

    public OutboxMessageRepository(SampleDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(IEnumerable<OutboxMessage> domainEvents)
    {
        await _dbContext.OutboxMessages.AddRangeAsync(domainEvents);
    }
}
```

#### Implement Unit of Work

To use the polling publisher implement the `IOutboxUnitOfWorkFactory` interface:

```csharp
public class OutboxUnitOfWorkFactory : IOutboxUnitOfWorkFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public OutboxUnitOfWorkFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public IOutboxUnitOfWork Begin()
    {
        var serviceScope = _serviceScopeFactory.CreateScope();
        var dbContext = serviceScope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var transaction = dbContext.Database.BeginTransaction();

        return new OutboxUnitOfWork(serviceScope, dbContext, transaction);
    }

    private class OutboxUnitOfWork : IOutboxUnitOfWork
    {
        private readonly IServiceScope _serviceScope;
        private readonly SampleDbContext _dbContext;
        private readonly IDbContextTransaction _transaction;

        public OutboxUnitOfWork(IServiceScope serviceScope, SampleDbContext dbContext, IDbContextTransaction transaction)
        {
            _serviceScope = serviceScope;
            _dbContext = dbContext;
            _transaction = transaction;
        }

        public async Task<ICollection<OutboxMessage>> GetAllUnpublishedMessages(CancellationToken stoppingToken)
        {
            return await _dbContext
                .OutboxMessages
                .Where(x => x.ProcessedUtc == null)
                .ToListAsync(stoppingToken);
        }

        public async Task Commit(CancellationToken stoppingToken)
        {
            await _dbContext.SaveChangesAsync(stoppingToken);

            _transaction.Commit();
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _dbContext?.Dispose();
            _serviceScope?.Dispose();
        }
    }
}
```

### Use the Outbox

Take a dependency on `Outbox` and call the `Enqueue` method:

```csharp
public class Service
{
    private readonly IOutbox _outbox;

    public TestCommandHandler(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public async Task Handle(TestCommand command)
    {
        ...
        IOutboxNotifier notifier = await _outbox.Enqueue(new[] {new Test {AggregateId = "aggregate-id"}});
        ...
    }
}
```

On the returned `IOutboxNotifier` call `Notify` ... {>>more info on notification here<<}

