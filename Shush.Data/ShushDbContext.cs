using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Shush.Data;

public sealed class ShushDbContext(DbContextOptions<ShushDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeVersion> RecipeVersions => Set<RecipeVersion>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Variable> Variables => Set<Variable>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunMachine> RunMachines => Set<RunMachine>();
    public DbSet<RunStep> RunSteps => Set<RunStep>();
    public DbSet<MetaEntry> Meta => Set<MetaEntry>();

    /// <summary>Opens (and migrates) the database file at <paramref name="path"/>.</summary>
    public static DbContextOptions<ShushDbContext> OptionsFor(string path) =>
        new DbContextOptionsBuilder<ShushDbContext>().UseSqlite($"Data Source={path}").Options;

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no date type: times are stored as text and read back with no kind. Everything here is UTC.
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Recipe>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Versions).WithOne(x => x.Recipe).HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RecipeVersion>(e =>
        {
            e.HasIndex(x => new { x.RecipeId, x.Number }).IsUnique();
            e.Property(x => x.Source).HasConversion<string>();
        });

        model.Entity<Group>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Members).WithOne(x => x.Group).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<GroupMember>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.MachineName });
            e.Property(x => x.MachineName).UseCollation("NOCASE");
        });

        model.Entity<Machine>(e =>
        {
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).UseCollation("NOCASE");
        });

        model.Entity<Variable>(e =>
        {
            e.Property(x => x.Scope).HasConversion<string>();
            e.Property(x => x.MachineName).UseCollation("NOCASE");
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Scope, x.GroupId, x.MachineName, x.Name }).IsUnique();
        });

        model.Entity<Deployment>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Run>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Deployment).WithMany().HasForeignKey(x => x.DeploymentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RecipeVersion).WithMany().HasForeignKey(x => x.RecipeVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Machines).WithOne(x => x.Run).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.StartedAt);
        });

        model.Entity<RunMachine>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.MachineName).UseCollation("NOCASE");
            e.HasMany(x => x.Steps).WithOne(x => x.RunMachine).HasForeignKey(x => x.RunMachineId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MachineName, x.Status });
        });

        model.Entity<RunStep>(e => e.Property(x => x.Status).HasConversion<string>());

        model.Entity<MetaEntry>(e => e.HasKey(x => x.Key));
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime(),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the context without the web app.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ShushDbContext>
{
    public ShushDbContext CreateDbContext(string[] args) => new(ShushDbContext.OptionsFor("design-time.db"));
}
