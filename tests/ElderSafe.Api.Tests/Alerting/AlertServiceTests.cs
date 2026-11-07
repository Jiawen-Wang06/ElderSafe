using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Detection;
using ElderSafe.Api.Security;
using ElderSafe.Api.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ElderSafe.Api.Tests.Alerting;

public class AlertServiceTests : IDisposable
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly SqliteConnection _connection;
    private readonly ElderSafeDbContext _context;
    private readonly FakeTimeProvider _clock;
    private readonly RecordingDispatch _dispatch;
    private readonly RecordingNotifier _notifier;
    private readonly AlertService _service;

    public AlertServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ElderSafeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ElderSafeDbContext(options);
        _context.Database.EnsureCreated();

        _clock = new FakeTimeProvider(Origin);
        _dispatch = new RecordingDispatch();
        _notifier = new RecordingNotifier();

        _service = new AlertService(
            _context,
            new ThresholdFallDetector(new FallDetectorOptions()),
            new EscalationPolicy(new EscalationOptions { ResponseTimeout = Timeout }),
            _dispatch,
            _clock,
            new AlertServiceOptions(),
            new ResidentAccess(_context),
            _notifier,
            NullLogger<AlertService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact(DisplayName = "REQ-FALL-02 raises exactly one alert for readings containing a fall")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Raises_exactly_one_alert_for_readings_containing_a_fall()
    {
        var resident = AddResident();
        AddReadings(resident.Id, Scenario(ScenarioLibrary.Fall));

        var alert = await _service.EvaluateFallAsync(resident.Id);

        Assert.NotNull(alert);
        Assert.Equal(AlertKind.Fall, alert!.Kind);
        Assert.Equal(0, alert.EscalationLevel);
        Assert.Single(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-02 ingesting readings that contain a fall raises an alert")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Ingesting_readings_that_contain_a_fall_raises_an_alert()
    {
        var resident = AddResident();
        var ingestion = new ReadingIngestionService(_context, _service);

        var outcome = await ingestion.IngestAsync(resident.Id, IncomingFallWindow());

        Assert.True(outcome.ResidentFound);
        Assert.NotNull(outcome.RaisedAlertId);
        Assert.Single(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-02 ingesting readings for an unknown resident stores nothing")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Ingesting_readings_for_an_unknown_resident_stores_nothing()
    {
        var ingestion = new ReadingIngestionService(_context, _service);

        var outcome = await ingestion.IngestAsync(Guid.NewGuid(), IncomingFallWindow());

        Assert.False(outcome.ResidentFound);
        Assert.Empty(_context.Readings);
        Assert.Empty(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-02 raises no alert for ordinary movement")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Raises_no_alert_for_ordinary_movement()
    {
        var resident = AddResident();
        AddReadings(resident.Id, Scenario(ScenarioLibrary.OrdinaryMovement));

        var alert = await _service.EvaluateFallAsync(resident.Id);

        Assert.Null(alert);
        Assert.Empty(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-02 does not raise a second alert while one is still open")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Does_not_raise_a_second_alert_while_one_is_still_open()
    {
        var resident = AddResident();
        AddReadings(resident.Id, Scenario(ScenarioLibrary.Fall));

        Assert.NotNull(await _service.EvaluateFallAsync(resident.Id));
        Assert.Null(await _service.EvaluateFallAsync(resident.Id));
        Assert.Single(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-SEC-02 records who acknowledged an alert and when")]
    [Trait("Requirement", "REQ-SEC-02")]
    [Trait("Requirement", "REQ-FALL-07")]
    public async Task Records_who_acknowledged_an_alert_and_when()
    {
        var resident = AddResident();
        var caregiver = AddCaregiver(resident.Id, order: 0);
        var alert = AddOpenAlert(resident.Id);

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(await _service.AcknowledgeAsync(alert.Id, caregiver.Id));

        _context.ChangeTracker.Clear();

        var stored = _context.Alerts.Single();

        Assert.Equal(Origin.AddSeconds(30), stored.AcknowledgedAt);
        Assert.Equal(caregiver.Id, stored.AcknowledgedByCaregiverId);
        Assert.Contains(_context.AuditEntries, e => e.Action == "AcknowledgedAlert");
    }

    [Fact(DisplayName = "REQ-FALL-03 does not escalate an acknowledged alert")]
    [Trait("Requirement", "REQ-FALL-03")]
    [Trait("Requirement", "REQ-FALL-07")]
    public async Task Does_not_escalate_an_acknowledged_alert()
    {
        var resident = AddResident();
        var caregiver = AddCaregiver(resident.Id, order: 0);
        AddCaregiver(resident.Id, order: 1);
        var alert = AddOpenAlert(resident.Id);

        await _service.AcknowledgeAsync(alert.Id, caregiver.Id);

        _clock.Advance(Timeout + TimeSpan.FromMinutes(5));

        await _service.ApplyEscalationAsync();

        _context.ChangeTracker.Clear();

        Assert.Equal(0, _context.Alerts.Single().EscalationLevel);
    }

    [Fact(DisplayName = "REQ-FALL-03 escalates an unacknowledged alert after the timeout")]
    [Trait("Requirement", "REQ-FALL-03")]
    public async Task Escalates_an_unacknowledged_alert_after_the_timeout()
    {
        var resident = AddResident();
        AddCaregiver(resident.Id, order: 0);
        AddCaregiver(resident.Id, order: 1);
        AddOpenAlert(resident.Id);

        await _service.ApplyEscalationAsync();
        _context.ChangeTracker.Clear();
        Assert.Equal(0, _context.Alerts.Single().EscalationLevel);

        _clock.Advance(Timeout);

        await _service.ApplyEscalationAsync();
        _context.ChangeTracker.Clear();

        var escalated = _context.Alerts.Single();

        Assert.Equal(1, escalated.EscalationLevel);
        Assert.Equal(Origin + Timeout, escalated.LastNotifiedAt);
    }

    [Fact(DisplayName = "REQ-FALL-06 places exactly one emergency call across repeated sweeps")]
    [Trait("Requirement", "REQ-FALL-06")]
    public async Task Places_exactly_one_emergency_call_across_repeated_sweeps()
    {
        var resident = AddResident(emergencyCallEnabled: true);
        AddCaregiver(resident.Id, order: 0);
        AddOpenAlert(resident.Id);

        _clock.Advance(Timeout);

        await _service.ApplyEscalationAsync();
        _clock.Advance(Timeout);
        await _service.ApplyEscalationAsync();

        _context.ChangeTracker.Clear();

        Assert.Single(_dispatch.Calls);
        Assert.NotNull(_context.Alerts.Single().DispatchedAt);
    }

    [Fact(DisplayName = "REQ-CFG-06 the resident's configured contact number reaches only the dispatch simulation")]
    [Trait("Requirement", "REQ-CFG-06")]
    public async Task The_residents_configured_contact_number_reaches_only_the_dispatch_simulation()
    {
        var resident = AddResident(emergencyCallEnabled: true, emergencyContactPhoneNumber: "555-0100");
        AddCaregiver(resident.Id, order: 0);
        AddOpenAlert(resident.Id);

        _clock.Advance(Timeout);
        await _service.ApplyEscalationAsync();

        // IEmergencyDispatch is the one boundary any contact number can
        // cross, and RecordingDispatch here stands in for its only real
        // implementation, LoggingEmergencyDispatch, which only logs. Seeing
        // the number arrive at this boundary and nowhere else is as much as
        // a test can verify that it never reaches a real dial path.
        Assert.Single(_dispatch.Calls);
        Assert.Equal("555-0100", _dispatch.Calls.Single().ContactPhoneNumber);
    }

    [Fact(DisplayName = "REQ-CFG-06 an unconfigured contact number reaches the dispatch simulation as null")]
    [Trait("Requirement", "REQ-CFG-06")]
    public async Task An_unconfigured_contact_number_reaches_the_dispatch_simulation_as_null()
    {
        var resident = AddResident(emergencyCallEnabled: true);
        AddCaregiver(resident.Id, order: 0);
        AddOpenAlert(resident.Id);

        _clock.Advance(Timeout);
        await _service.ApplyEscalationAsync();

        Assert.Single(_dispatch.Calls);
        Assert.Null(_dispatch.Calls.Single().ContactPhoneNumber);
    }

    [Fact(DisplayName = "REQ-FALL-05 places no emergency call when the setting is off")]
    [Trait("Requirement", "REQ-FALL-05")]
    public async Task Places_no_emergency_call_when_the_setting_is_off()
    {
        var resident = AddResident(emergencyCallEnabled: false);
        AddCaregiver(resident.Id, order: 0);
        AddOpenAlert(resident.Id);

        _clock.Advance(Timeout + TimeSpan.FromHours(1));

        await _service.ApplyEscalationAsync();

        Assert.Empty(_dispatch.Calls);
    }

    [Fact(DisplayName = "REQ-FALL-06 retries on the next sweep when the call could not be placed")]
    [Trait("Requirement", "REQ-FALL-06")]
    public async Task Retries_on_the_next_sweep_when_the_call_could_not_be_placed()
    {
        var resident = AddResident(emergencyCallEnabled: true);
        AddCaregiver(resident.Id, order: 0);
        AddOpenAlert(resident.Id);

        _dispatch.Succeeds = false;
        _clock.Advance(Timeout);
        await _service.ApplyEscalationAsync();

        _context.ChangeTracker.Clear();
        Assert.Null(_context.Alerts.Single().DispatchedAt);

        _dispatch.Succeeds = true;
        _clock.Advance(Timeout);
        await _service.ApplyEscalationAsync();

        _context.ChangeTracker.Clear();
        Assert.NotNull(_context.Alerts.Single().DispatchedAt);
        Assert.Equal(2, _dispatch.Calls.Count);
    }

    [Fact(DisplayName = "REQ-SEC-01 refuses to acknowledge an alert for a resident the caregiver is not assigned to")]
    [Trait("Requirement", "REQ-SEC-01")]
    [Trait("Requirement", "REQ-FALL-07")]
    public async Task Refuses_to_acknowledge_an_alert_for_a_resident_the_caregiver_is_not_assigned_to()
    {
        var resident = AddResident();
        var otherResident = AddResident("Resident B");
        var caregiver = AddCaregiver(otherResident.Id, order: 0);
        var alert = AddOpenAlert(resident.Id);

        Assert.False(await _service.AcknowledgeAsync(alert.Id, caregiver.Id));

        _context.ChangeTracker.Clear();

        Assert.Null(_context.Alerts.Single(a => a.Id == alert.Id).AcknowledgedAt);
    }

    [Fact(DisplayName = "REQ-FALL-07 a second acknowledgement does not overwrite the first")]
    [Trait("Requirement", "REQ-FALL-07")]
    public async Task A_second_acknowledgement_does_not_overwrite_the_first()
    {
        var resident = AddResident();
        var firstCaregiver = AddCaregiver(resident.Id, order: 0);
        var secondCaregiver = AddCaregiver(resident.Id, order: 1);
        var alert = AddOpenAlert(resident.Id);

        Assert.True(await _service.AcknowledgeAsync(alert.Id, firstCaregiver.Id));

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(await _service.AcknowledgeAsync(alert.Id, secondCaregiver.Id));

        _context.ChangeTracker.Clear();

        var stored = _context.Alerts.Single();

        Assert.Equal(Origin, stored.AcknowledgedAt);
        Assert.Equal(firstCaregiver.Id, stored.AcknowledgedByCaregiverId);
    }

    [Fact(DisplayName = "REQ-FALL-02 publishes a new alert to connected caregivers")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Publishes_a_new_alert_to_connected_caregivers()
    {
        var resident = AddResident();
        AddReadings(resident.Id, Scenario(ScenarioLibrary.Fall));

        await _service.EvaluateFallAsync(resident.Id);

        var published = Assert.Single(_notifier.Raised);

        Assert.Equal(resident.Id, published.ResidentId);
        Assert.Equal("Fall", published.Kind);
    }

    [Fact(DisplayName = "REQ-FALL-02 publishes an acknowledgement so other caregivers see it was handled")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Publishes_an_acknowledgement_so_other_caregivers_see_it_was_handled()
    {
        var resident = AddResident();
        var caregiver = AddCaregiver(resident.Id, order: 0);
        var alert = AddOpenAlert(resident.Id);

        await _service.AcknowledgeAsync(alert.Id, caregiver.Id);

        var published = Assert.Single(_notifier.Updated);

        Assert.Equal(alert.Id, published.Id);
        Assert.NotNull(published.AcknowledgedAt);
    }

    [Fact(DisplayName = "REQ-FALL-02 publishes an escalation")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Publishes_an_escalation()
    {
        var resident = AddResident();
        AddCaregiver(resident.Id, order: 0);
        AddCaregiver(resident.Id, order: 1);
        AddOpenAlert(resident.Id);

        _clock.Advance(Timeout);

        await _service.ApplyEscalationAsync();

        var published = Assert.Single(_notifier.Updated);

        Assert.Equal(1, published.EscalationLevel);
    }

    [Fact(DisplayName = "REQ-FALL-02 stores the alert even when it cannot be published")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Stores_the_alert_even_when_it_cannot_be_published()
    {
        var resident = AddResident();
        AddReadings(resident.Id, Scenario(ScenarioLibrary.Fall));

        _notifier.Throws = true;

        var alert = await _service.EvaluateFallAsync(resident.Id);

        Assert.NotNull(alert);
        Assert.Single(_context.Alerts);
    }

    private Resident AddResident(bool emergencyCallEnabled = false, string? emergencyContactPhoneNumber = null)
    {
        return AddResident("Resident A", emergencyCallEnabled, emergencyContactPhoneNumber);
    }

    private Resident AddResident(
        string displayName,
        bool emergencyCallEnabled = false,
        string? emergencyContactPhoneNumber = null)
    {
        var resident = new Resident
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            EmergencyCallEnabled = emergencyCallEnabled,
            EmergencyContactPhoneNumber = emergencyContactPhoneNumber
        };

        _context.Residents.Add(resident);
        _context.SaveChanges();

        return resident;
    }

    private UserAccount AddCaregiver(Guid residentId, int order)
    {
        var caregiver = new UserAccount
        {
            Id = Guid.NewGuid(),
            UserName = $"{Guid.NewGuid():N}@example.invalid",
            DisplayName = $"Caregiver {order}",
            AccountType = AccountType.Caregiver
        };

        _context.Users.Add(caregiver);
        _context.ResidentCaregivers.Add(new ResidentCaregiver
        {
            ResidentId = residentId,
            CaregiverId = caregiver.Id,
            Order = order
        });

        _context.SaveChanges();

        return caregiver;
    }

    private Alert AddOpenAlert(Guid residentId)
    {
        var now = _clock.GetUtcNow();

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = AlertKind.Fall,
            RaisedAt = now,
            LastNotifiedAt = now,
            EscalationLevel = 0
        };

        _context.Alerts.Add(alert);
        _context.SaveChanges();

        return alert;
    }

    private void AddReadings(Guid residentId, IReadOnlyList<IncomingReading> readings)
    {
        foreach (var reading in readings)
        {
            _context.Readings.Add(new Reading
            {
                Id = Guid.NewGuid(),
                ResidentId = residentId,
                At = reading.At,
                AccelerationX = reading.AccelerationX,
                AccelerationY = reading.AccelerationY,
                AccelerationZ = reading.AccelerationZ
            });
        }

        _context.SaveChanges();
    }

    private IReadOnlyList<IncomingReading> Scenario(string name)
    {
        ScenarioLibrary.TryBuild(name, _clock.GetUtcNow(), out var readings);

        return readings;
    }

    private List<IncomingReading> IncomingFallWindow()
    {
        ScenarioLibrary.TryBuild(ScenarioLibrary.Fall, _clock.GetUtcNow(), out var readings);

        return readings.ToList();
    }

    private sealed class RecordingNotifier : IAlertNotifier
    {
        public List<AlertView> Raised { get; } = new();

        public List<AlertView> Updated { get; } = new();

        public List<ResidentConfigurationChange> ConfigurationChanges { get; } = new();

        public bool Throws { get; set; }

        public Task AlertRaisedAsync(AlertView alert, CancellationToken cancellationToken = default)
        {
            if (Throws)
            {
                throw new InvalidOperationException("the hub is unreachable");
            }

            Raised.Add(alert);

            return Task.CompletedTask;
        }

        public Task AlertUpdatedAsync(AlertView alert, CancellationToken cancellationToken = default)
        {
            if (Throws)
            {
                throw new InvalidOperationException("the hub is unreachable");
            }

            Updated.Add(alert);

            return Task.CompletedTask;
        }

        public Task ConfigurationChangedAsync(
            ResidentConfigurationChange change,
            CancellationToken cancellationToken = default)
        {
            ConfigurationChanges.Add(change);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatch : IEmergencyDispatch
    {
        public List<EmergencyCallRequest> Calls { get; } = new();

        public bool Succeeds { get; set; } = true;

        public Task<bool> PlaceCallAsync(
            EmergencyCallRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(request);

            return Task.FromResult(Succeeds);
        }
    }
}
