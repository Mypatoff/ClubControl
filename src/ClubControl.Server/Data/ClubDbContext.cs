using ClubControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

public class ClubDbContext : DbContext
{
    public ClubDbContext(DbContextOptions<ClubDbContext> options) : base(options)
    {
    }

    public DbSet<Pc> Pcs => Set<Pc>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionSegment> SessionSegments => Set<SessionSegment>();
    public DbSet<SessionExtra> SessionExtras => Set<SessionExtra>();
    public DbSet<EventLogEntry> EventLogEntries => Set<EventLogEntry>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pc>()
            .HasIndex(p => p.MachineGuid)
            .IsUnique();

        modelBuilder.Entity<Tariff>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<Session>()
            .HasMany(s => s.Segments)
            .WithOne(seg => seg.Session)
            .HasForeignKey(seg => seg.SessionId);

        modelBuilder.Entity<Session>()
            .HasMany(s => s.Extras)
            .WithOne(x => x.Session)
            .HasForeignKey(x => x.SessionId);

        modelBuilder.Entity<Session>()
            .HasOne(s => s.Pc)
            .WithMany()
            .HasForeignKey(s => s.PcId);

        modelBuilder.Entity<SessionSegment>()
            .HasOne(seg => seg.Pc)
            .WithMany()
            .HasForeignKey(seg => seg.PcId);
    }
}
