using LifeGraph.Infrastructure.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Infrastructure.Jobs;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> job)
    {
        job.ToTable("jobs", table =>
        {
            table.HasCheckConstraint("ck_jobs_status", "status IN ('dead', 'done', 'pending', 'running')");
            table.HasCheckConstraint("ck_jobs_attempts", "attempts >= 0");
            table.HasCheckConstraint("ck_jobs_running_has_lease", "(status = 'running') = (lease_until IS NOT NULL)");
        });
        job.HasKey(entity => entity.Id).HasName("pk_jobs");
        job.Property(entity => entity.Id).ValueGeneratedNever();
        job.HasOne<Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_jobs_account_id");
        job.Property(entity => entity.Kind).HasMaxLength(Job.KindMaxLength);
        job.Property(entity => entity.Payload).HasColumnType("jsonb");
        job.Property(entity => entity.Status)
            .HasConversion(status => status.ToString().ToLowerInvariant(), name => Enum.Parse<JobStatus>(name, true))
            .HasMaxLength(16);
        job.Property(entity => entity.LastError).HasMaxLength(Job.ErrorMaxLength);
        job.Property<uint>("RowVersion").IsRowVersion();

        // What the claim scans: due pending jobs, and running ones whose lease may have expired.
        job.HasIndex(entity => entity.RunAfter).HasFilter("status = 'pending'").HasDatabaseName("ix_jobs_pending_run_after");
        job.HasIndex(entity => entity.LeaseUntil).HasFilter("status = 'running'").HasDatabaseName("ix_jobs_running_lease_until");
        job.HasIndex(entity => new { entity.AccountId, entity.Kind }).HasDatabaseName("ix_jobs_account_id_kind");
    }
}
