using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Alerting;

/// <summary>One reading as it arrives from a device.</summary>
public sealed record IncomingReading(
    DateTimeOffset At,
    double AccelerationX,
    double AccelerationY,
    double AccelerationZ,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters);

/// <summary>What happened to a batch of readings.</summary>
/// <param name="ResidentFound">False when no such resident exists.</param>
/// <param name="Accepted">How many readings were stored.</param>
/// <param name="RaisedAlertId">The alert raised by this batch, if any.</param>
public sealed record IngestionOutcome(
    bool ResidentFound,
    int Accepted,
    Guid? RaisedAlertId)
{
    public static IngestionOutcome ResidentNotFound()
    {
        return new IngestionOutcome(false, 0, null);
    }
}

/// <summary>
/// Stores incoming readings and has them evaluated for a fall.
/// </summary>
/// <remarks>
/// Ingestion and evaluation belong together: storing readings without
/// evaluating them would leave a fall sitting in the database with nobody
/// told, which is RISK-01 arriving by the back door. Keeping both inside one
/// service also means the controller cannot perform half of the operation.
/// </remarks>
public sealed class ReadingIngestionService
{
    private readonly ElderSafeDbContext _context;
    private readonly AlertService _alertService;

    public ReadingIngestionService(ElderSafeDbContext context, AlertService alertService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _alertService = alertService ?? throw new ArgumentNullException(nameof(alertService));
    }

    public async Task<IngestionOutcome> IngestAsync(
        Guid residentId,
        IReadOnlyList<IncomingReading> readings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var residentExists = await _context.Residents
            .AnyAsync(r => r.Id == residentId, cancellationToken);

        if (!residentExists)
        {
            return IngestionOutcome.ResidentNotFound();
        }

        foreach (var reading in readings)
        {
            _context.Readings.Add(new Reading
            {
                Id = Guid.NewGuid(),
                ResidentId = residentId,
                At = reading.At,
                AccelerationX = reading.AccelerationX,
                AccelerationY = reading.AccelerationY,
                AccelerationZ = reading.AccelerationZ,
                Latitude = reading.Latitude,
                Longitude = reading.Longitude,
                AccuracyMeters = reading.AccuracyMeters
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var alert = await _alertService.EvaluateFallAsync(residentId, cancellationToken);

        return new IngestionOutcome(true, readings.Count, alert?.Id);
    }
}
