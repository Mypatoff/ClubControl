using System.Security.Claims;
using ClubControl.Server.Data;
using ClubControl.Server.Hubs;
using ClubControl.Server.Services;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace ClubControl.Server.Tests;

/// <summary>A <see cref="ClubHub"/> that can be called directly in a test: a connection id, and a caller that swallows what is sent to it.</summary>
public static class HubFakes
{
    public static ClubHub Hub(ClubDbContext db, SessionService sessions, HeartbeatTracker heartbeats) =>
        new(db, sessions, new ClientUpdateTracker(), new AuthRejectionTracker(), heartbeats)
        {
            Context = new FakeCallerContext(),
            Clients = new FakeClients(),
        };


    private sealed class FakeCallerContext : HubCallerContext
    {
        public override string ConnectionId { get; } = Guid.NewGuid().ToString();
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class SwallowingProxy : ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default) => Task.FromResult(default(T)!);
    }

    private sealed class FakeClients : IHubCallerClients
    {
        private static readonly SwallowingProxy Proxy = new();

        public ISingleClientProxy Caller => Proxy;
        public ISingleClientProxy Client(string connectionId) => Proxy;
        IClientProxy IHubCallerClients<IClientProxy>.Caller => Proxy;
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Proxy;
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy Others => Proxy;
        public IClientProxy OthersInGroup(string groupName) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }
}
