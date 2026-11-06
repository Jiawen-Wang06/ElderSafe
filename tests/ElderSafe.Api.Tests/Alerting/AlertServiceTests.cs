using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Detection;
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

        _service = new AlertService(
            _context,
            new ThresholdFallDetector(new FallDetectorOptions()),
            new EscalationPolicy(new EscalationOptions { ResponseTimeout = Timeout }),
            _dispatch,
            _clock,
            new AlertServiceOptions(),
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
        AddReadings(resident.Id, FallWindow());

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
        AddReadings(resident.Id, SteadyWindow());

        var alert = await _service.EvaluateFallAsync(resident.Id);

        Assert.Null(alert);
        Assert.Empty(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-02 does not raise a second alert while one is still open")]
    [Trait("Requirement", "REQ-FALL-02")]
    public async Task Does_not_raise_a_second_alert_while_one_is_still_open()
    {
        var resident = AddResident();
        AddReadings(resident.Id, FallWindow());

        Assert.NotNull(await _service.EvaluateFallAsync(resident.Id));
        Assert.Null(await _service.EvaluateFallAsync(resident.Id));
        Assert.Single(_context.Alerts);
    }

    [Fact(DisplayName = "REQ-SEC-02 records who acknowledged an alert and when")]
    [Trait("Requirement", "REQ-SEC-02")]
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

    private Resident AddResident(bool emergencyCallEnabled = false)
    {
        var resident = new Resident
        {
            Id = Guid.NewGuid(),
            DisplayName = "Resident A",
            EmergencyCallEnabled = emergencyCallEnabled
        };

        _context.Residents.Add(resident);
        _context.SaveChanges();

        return resident;
    }

    private Caregiver AddCaregiver(Guid residentId, int order)
    {
        var caregiver = new Caregiver
        {
            Id = Guid.NewGuid(),
            UserName = $"{Guid.NewGuid():N}@example.invalid",
            DisplayName = $"Caregiver {order}"
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

    private void AddReadings(Guid residentId, IEnumerable<(double Seconds, double MagnitudeG)> shape)
    {
        var now = _clock.GetUtcNow();

        foreach (var (seconds, magnitude) in shape)
        {
            _context.Readings.Add(new Reading
            {
                Id = Guid.NewGuid(),
                ResidentId = residentId,
                At = now.AddSeconds(seconds - 3.9),
                AccelerationX = 0,
                AccelerationY = 0,
                AccelerationZ = magnitude
            });
        }

        _context.SaveChanges();
    }

    private static List<(double Seconds, double MagnitudeG)> FallWindow()
    {
        var points = new List<(double, double)>();
        var t = 0.0;

        void Hold(double seconds, double magnitude)
        {
            var count = (int)Math.Round(seconds / 0.05);

            for (var i = 0; i < count; i++)
            {
                points.Add((t, magnitude));
                t += 0.05;
            }
        }

        Hold(0.5, 1.0);
        Hold(0.3, 0.4);
        points.Add((t, 3.0));
        t += 0.05;
        Hold(2.6, 1.0);

        return points;
    }

    private List<IncomingReading> IncomingFallWindow()
    {
        var now = _clock.GetUtcNow();

        return FallWindow()
            .Select(p => new IncomingReading(
                now.AddSeconds(p.Seconds - 3.9), 0, 0, p.MagnitudeG, null, null, null))
            .ToList();
    }

    private static List<(double Seconds, double MagnitudeG)> SteadyWindow()
    {
        var points = new List<(double, double)>();

        for (var i = 0; i < 78; i++)
        {
            points.Add((i * 0.05, 1.3));
        }

        return points;
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
