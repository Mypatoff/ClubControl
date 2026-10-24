using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubControl.Server.Services;

/// <summary>
/// Ticks the two time-driven billing rules that have no admin action behind them (SPEC section 5):
/// a prepay session ending automatically when its time runs out, and a postpay session's cost
/// rising until it reaches the tariff maximum ("Limit reached").
/// </summary>
public class SessionMonitorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;

    public SessionMonitorService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await TickOnceAsync();
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
