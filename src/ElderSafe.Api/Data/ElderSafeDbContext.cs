using ElderSafe.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ElderSafe.Api.Data;

/// <summary>
/// The application's only database context: Identity's tables and the domain
/// tables share one context and one migration history (ADR-006).
/// </summary>
public class ElderSafeDbContext
    : IdentityDbContext<Caregiver, IdentityRole<Guid>, Guid>
{
    public ElderSafeDbContext(DbContextOptions<ElderSafeDbContext> options)
        : base(options)
    {
    }

    public DbSet<Resident> Residents => Set<Resident>();

    public DbSet<ResidentCaregiver> ResidentCaregivers => Set<ResidentCaregiver>();

    public DbSet<Reading> Readings => Set<Reading>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // Applies to nullable DateTimeOffset properties as well.
        builder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ResidentCaregiver>(entity =>
        {
            entity.HasKey(rc => new { rc.ResidentId, rc.CaregiverId });

            entity.HasOne(rc => rc.Resident)
                .WithMany(r => r.Caregivers)
                .HasForeignKey(rc => rc.ResidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(rc => rc.Caregiver)
                .WithMany(c => c.Assignments)
                .HasForeignKey(rc => rc.CaregiverId)
                .OnDelete(DeleteBehavior.Cascade);

            // One position per resident, so two caregivers cannot both be
            // "first" and leave the escalation order ambiguous (RISK-07).
            entity.HasIndex(rc => new { rc.ResidentId, rc.Order }).IsUnique();
        });

        builder.Entity<Alert>(entity =>
        {
            // Stored as text so the data stays readable and reordering the
            // enum cannot silently change the meaning of existing rows.
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(a => a.Resident)
                .WithMany()
                .HasForeignKey(a => a.ResidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.AcknowledgedByCaregiver)
                .WithMany()
                .HasForeignKey(a => a.AcknowledgedByCaregiverId)
                .OnDelete(DeleteBehavior.SetNull);

            // The dashboard asks for a resident's open alerts, newest first.
            entity.HasIndex(a => new { a.ResidentId, a.RaisedAt });
        });

        builder.Entity<Reading>(entity =>
        {
            entity.HasOne(r => r.Resident)
                .WithMany()
                .HasForeignKey(r => r.ResidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // The detector asks for one resident's most recent few seconds.
            entity.HasIndex(r => new { r.ResidentId, r.At });
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.Property(a => a.Action).HasMaxLength(64).IsRequired();
            entity.HasIndex(a => a.At);
        });

        builder.Entity<Resident>(entity =>
        {
            entity.Property(r => r.DisplayName).HasMaxLength(200).IsRequired();
        });

        builder.Entity<Caregiver>(entity =>
        {
            entity.Property(c => c.DisplayName).HasMaxLength(200).IsRequired();
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditLogIsAppendOnly();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardAuditLogIsAppendOnly();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Refuses to save a change that would alter or remove an existing audit
    /// entry (REQ-SEC-02).
    /// </summary>
    /// <remarks>
    /// Enforced here rather than by leaving update and delete out of the
    /// application's own code paths. An audit log that any future endpoint,
    /// migration or test fixture could quietly edit is not evidence of
    /// anything, and "we simply never wrote that code" stops being true the
    /// first time someone does.
    /// </remarks>
    private void GuardAuditLogIsAppendOnly()
    {
        foreach (EntityEntry<AuditEntry> entry in ChangeTracker.Entries<AuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"The audit log is append-only; attempted to {entry.State.ToString().ToLowerInvariant()} " +
                    $"audit entry {entry.Entity.Id}.");
            }
        }
    }
}
