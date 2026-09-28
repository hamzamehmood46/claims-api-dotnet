using ClaimsApi.Application;
using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsApi.Infrastructure;

public class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options), IClaimsDbContext
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Claim> Claims => Set<Claim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
            e.Property(p => p.LastName).HasMaxLength(100).IsRequired();
            e.Property(p => p.InsuranceId).HasMaxLength(64).IsRequired();
            e.HasIndex(p => p.LastName);
            e.HasIndex(p => p.InsuranceId);
        });

        modelBuilder.Entity<Claim>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.DecisionReason).HasMaxLength(500);
            e.Ignore(c => c.Total);
            e.HasIndex(c => c.Status);
            e.HasIndex(c => c.PatientId);
            e.HasOne<Patient>().WithMany().HasForeignKey(c => c.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Lines).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClaimLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.ProcedureCode).HasMaxLength(16).IsRequired();
            e.Property(l => l.Description).HasMaxLength(200);
            e.Property(l => l.Amount).HasPrecision(18, 2);
        });
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddClaimsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Claims") ?? "Data Source=claims.db";
        services.AddDbContext<ClaimsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IClaimsDbContext>(sp => sp.GetRequiredService<ClaimsDbContext>());
        services.AddScoped<PatientService>();
        services.AddScoped<ClaimService>();
        return services;
    }
}
