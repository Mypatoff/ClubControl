using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClubControl.Server.Data;

/// <summary>
/// Lets `dotnet ef migrations add` create a ClubDbContext at design time. There is no
/// ASP.NET Core Program.cs/Startup for the EF tools to discover (this is a WPF app that builds
/// its own WebApplication at runtime - see App.xaml.cs), so this factory is the standard way to
/// point the tools at the right connection string without running the whole app.
/// </summary>
public class ClubDbContextFactory : IDesignTimeDbContextFactory<ClubDbContext>
{
    public ClubDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClubDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time.db");
        return new ClubDbContext(optionsBuilder.Options);
    }
}
