namespace ElderSafe.Api.Alerting;

/// <summary>
/// Re-examines open alerts on a timer and applies the escalation policy.
/// </summary>
/// <remarks>
/// <para>
/// A single worker, deliberately. Two would each read the same open alert and
/// could both act on it. The stored <c>DispatchedAt</c> guards the emergency
/// call itself, but a second notification is still noise, and nothing here
/// yet coordinates between instances.
/// </para>
/// <para>
/// In-process scheduling also means escalation stops when the process does. A
/// real deployment needs durable scheduling, which is recorded as a known
/// limitation in the architecture document rather than pretended away.
/// </para>
/// </remarks>
public sealed class EscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly AlertServiceOptions _options;
    private readonly ILogger<EscalationWorker> _logger;

    public EscalationWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        AlertServiceOptions options,
        ILogger<EscalationWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.EscalationSweepInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var alertService = scope.ServiceProvider.GetRequiredService<AlertService>();

                await alertService.ApplyEscalationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad sweep must not take the worker down: if it exits,
                // nothing escalates again until the process restarts, and
                // every open alert sits unattended (RISK-06).
                _logger.LogError(exception, "Escalation sweep failed");
            }
        }
    }
}
