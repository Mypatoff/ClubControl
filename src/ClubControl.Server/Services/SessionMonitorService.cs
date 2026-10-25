using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Ticks the two time-driven billing rules that have no admin action behind them (SPEC section 5):
/// a prepay session ending automatically when its time runs out, and a postpay session's cost
/// rising until it reaches the tariff maximum ("Limit reached").
/// </summary>
public class SessionMonitorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    // A tick runs every 2s; logging every single failure while something stays broken would
    // flood the log forever. Log the first occurrence right away, then at most this often while
    // it keeps failing, plus one more line when it eventually recovers.
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionMonitorService> _logger;

    private int _consecutiveFailures;
    private DateTime _lastLoggedFailureUtc = DateTime.MinValue;

    public SessionMonitorService(IServiceScopeFactory scopeFactory, ILogger<SessionMonitorService> logger)
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
                await TickOnceAsync();

                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation(
                        "Session monitor recovered after {FailureCount} consecutive failed ticks.", _consecutiveFailures);
                    _consecutiveFailures = 0;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _consecutiveFailures++;
                var now = DateTime.UtcNow;

                if (_consecutiveFailures == 1 || now - _lastLoggedFailureUtc >= FailureLogInterval)
                {
                    _logger.LogError(ex,
                        "Session monitor tick failed ({FailureCount} consecutive failures so far); will keep retrying.",
                        _consecutiveFailures);
                    _lastLoggedFailureUtc = now;
                }

                // A missed tick should never take the whole server down; the next tick tries again.
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task TickOnceAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        await sessionService.EndExpiredPrepaySessionsAsync();
        await sessionService.TickPostpayCostsAsync();
    }
}
