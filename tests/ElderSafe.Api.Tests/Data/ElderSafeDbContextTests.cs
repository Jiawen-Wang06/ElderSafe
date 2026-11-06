using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Tests.Data;

public class ElderSafeDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ElderSafeDbContext _context;

    public ElderSafeDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ElderSafeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ElderSafeDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private Resident AddResident(string name = "Resident A")
    {
        var resident = new Resident { Id = Guid.NewGuid(), DisplayName = name };
        _context.Residents.Add(resident);
        _context.SaveChanges();
        return resident;
    }

    private Caregiver AddCaregiver(string name)
    {
        var caregiver = new Caregiver
        {
            Id = Guid.NewGuid(),
            UserName = $"{Guid.NewGuid():N}@example.invalid",
            DisplayName = name
        };

        _context.Users.Add(caregiver);
        _context.SaveChanges();
        return caregiver;
    }

    [Fact]
    public void Stores_a_timestamp_written_with_a_local_offset_as_utc()
    {
        var resident = AddResident();
        var raisedAt = new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.FromHours(-5));

        _context.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = resident.Id,
            Kind = AlertKind.Fall,
            RaisedAt = raisedAt,
            LastNotifiedAt = raisedAt
        });

        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var stored = _context.Alerts.Single();

        Assert.Equal(TimeSpan.Zero, stored.RaisedAt.Offset);
        Assert.Equal(raisedAt.UtcDateTime, stored.RaisedAt.UtcDateTime);
    }

    [Fact]
    public void Orders_alerts_correctly_when_they_were_written_with_mixed_offsets()
    {
        var resident = AddResident();

        var earlier = new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.FromHours(-5));
        var later = new DateTimeOffset(2026, 11, 5, 22, 30, 0, TimeSpan.Zero);

        _context.Alerts.AddRange(
            new Alert
            {
                Id = Guid.NewGuid(),
                ResidentId = resident.Id,
                Kind = AlertKind.Fall,
                RaisedAt = later,
                LastNotifiedAt = later
            },
            new Alert
            {
                Id = Guid.NewGuid(),
                ResidentId = resident.Id,
                Kind = AlertKind.Fall,
                RaisedAt = earlier,
                LastNotifiedAt = earlier
            });

        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var ordered = _context.Alerts.OrderBy(a => a.RaisedAt).ToList();

        Assert.Equal(earlier.UtcDateTime, ordered[0].RaisedAt.UtcDateTime);
        Assert.Equal(later.UtcDateTime, ordered[1].RaisedAt.UtcDateTime);
    }

    [Fact]
    public void Filters_alerts_by_timestamp_in_the_database()
    {
        var resident = AddResident();
        var cutoff = new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);

        _context.Alerts.AddRange(
            new Alert
            {
                Id = Guid.NewGuid(),
                ResidentId = resident.Id,
                Kind = AlertKind.Fall,
                RaisedAt = cutoff.AddMinutes(-10),
                LastNotifiedAt = cutoff.AddMinutes(-10)
            },
            new Alert
            {
                Id = Guid.NewGuid(),
                ResidentId = resident.Id,
                Kind = AlertKind.Fall,
                RaisedAt = cutoff.AddMinutes(10),
                LastNotifiedAt = cutoff.AddMinutes(10)
            });

        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var recent = _context.Alerts.Where(a => a.RaisedAt > cutoff).ToList();

        Assert.Single(recent);
        Assert.Equal(cutoff.AddMinutes(10).UtcDateTime, recent[0].RaisedAt.UtcDateTime);
    }

    [Fact]
    public void Round_trips_an_alert_with_its_dispatch_timestamp()
    {
        var resident = AddResident();
        var raisedAt = new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);
        var dispatchedAt = raisedAt.AddMinutes(4);
        var id = Guid.NewGuid();

        _context.Alerts.Add(new Alert
        {
            Id = id,
            ResidentId = resident.Id,
            Kind = AlertKind.Fall,
            RaisedAt = raisedAt,
            LastNotifiedAt = raisedAt,
            EscalationLevel = 2,
            DispatchedAt = dispatchedAt
        });

        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var stored = _context.Alerts.Single(a => a.Id == id);

        Assert.Equal(AlertKind.Fall, stored.Kind);
        Assert.Equal(2, stored.EscalationLevel);
        Assert.NotNull(stored.DispatchedAt);
        Assert.Equal(dispatchedAt.UtcDateTime, stored.DispatchedAt!.Value.UtcDateTime);
    }

    [Fact]
    public void Returns_a_residents_caregivers_in_escalation_order()
    {
        var resident = AddResident();
        var first = AddCaregiver("First");
        var second = AddCaregiver("Second");
        var third = AddCaregiver("Third");

        _context.ResidentCaregivers.AddRange(
            new ResidentCaregiver { ResidentId = resident.Id, CaregiverId = third.Id, Order = 2 },
            new ResidentCaregiver { ResidentId = resident.Id, CaregiverId = first.Id, Order = 0 },
            new ResidentCaregiver { ResidentId = resident.Id, CaregiverId = second.Id, Order = 1 });

        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var names = _context.ResidentCaregivers
            .Where(rc => rc.ResidentId == resident.Id)
            .OrderBy(rc => rc.Order)
            .Select(rc => rc.Caregiver.DisplayName)
            .ToList();

        Assert.Equal(new[] { "First", "Second", "Third" }, names);
    }

    [Fact]
    public void Refuses_to_modify_an_existing_audit_entry()
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Action = "ViewedResident"
        };

        _context.AuditEntries.Add(entry);
        _context.SaveChanges();

        entry.Action = "SomethingElse";

        var exception = Assert.Throws<InvalidOperationException>(() => _context.SaveChanges());

        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_to_delete_an_existing_audit_entry()
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Action = "AcknowledgedAlert"
        };

        _context.AuditEntries.Add(entry);
        _context.SaveChanges();

        _context.AuditEntries.Remove(entry);

        Assert.Throws<InvalidOperationException>(() => _context.SaveChanges());
    }

    [Fact]
    public void Allows_a_new_audit_entry_to_be_appended()
    {
        _context.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Action = "ViewedResident"
        });

        _context.SaveChanges();

        _context.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Action = "AcknowledgedAlert"
        });

        _context.SaveChanges();

        Assert.Equal(2, _context.AuditEntries.Count());
    }

    [Fact]
    public async Task Seeds_demo_data_only_when_it_is_enabled()
    {
        var disabled = new DemoDataSeeder(_context, new DemoDataOptions { Enabled = false });

        Assert.False(await disabled.SeedAsync());
        Assert.Empty(_context.Residents);

        var enabled = new DemoDataSeeder(_context, new DemoDataOptions { Enabled = true });

        Assert.True(await enabled.SeedAsync());
        Assert.NotEmpty(_context.Residents);
    }

    [Fact]
    public async Task Does_not_seed_demo_data_twice()
    {
        var seeder = new DemoDataSeeder(_context, new DemoDataOptions { Enabled = true });

        Assert.True(await seeder.SeedAsync());

        var count = _context.Residents.Count();

        Assert.False(await seeder.SeedAsync());
        Assert.Equal(count, _context.Residents.Count());
    }
}
