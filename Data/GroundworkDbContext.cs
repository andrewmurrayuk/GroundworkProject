using Microsoft.EntityFrameworkCore;

namespace Groundwork.Data;

public class GroundworkDbContext : DbContext
{
    public GroundworkDbContext(DbContextOptions<GroundworkDbContext> options)
        : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Brief> Briefs => Set<Brief>();
    public DbSet<SeedList> SeedLists => Set<SeedList>();
    public DbSet<Paper> Papers => Set<Paper>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunSource> RunSources => Set<RunSource>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Project>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.HasMany(p => p.Briefs).WithOne().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.SeedLists).WithOne().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Papers).WithOne().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Runs).WithOne().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Run>(e =>
        {
            e.Property(r => r.Status).HasMaxLength(40);
            e.HasMany(r => r.Sources).WithOne().HasForeignKey(s => s.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Report).WithOne().HasForeignKey<Report>(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.ProjectId, r.CreatedUtc });
        });

        b.Entity<RunSource>(e =>
        {
            e.Property(s => s.Stage).HasMaxLength(40);
            e.Property(s => s.Origin).HasMaxLength(40);
            e.HasIndex(s => new { s.RunId, s.ClientId });
        });
    }
}
