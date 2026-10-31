using System.ComponentModel.DataAnnotations;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The life of an Account export (DA-248): pending, then ready or failed, then expired.</summary>
public sealed class AccountExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = AccountExportOptions.MaxLifetime;

    [Fact]
    public void A_requested_export_is_pending_with_no_archive()
    {
        var export = Requested();

        Assert.Equal(AccountExportState.Pending, export.StateAt(Now));
        Assert.Null(export.Content);
        Assert.Null(export.ExpiresAt);
    }

    [Fact]
    public void Completing_keeps_the_archive_its_size_and_when_it_expires()
    {
        var export = Requested();

        Assert.True(export.Complete([1, 2, 3], Lifetime, Now));

        Assert.Equal(AccountExportState.Ready, export.StateAt(Now));
        Assert.Equal(3, export.SizeBytes);
        Assert.Equal(Now, export.CompletedAt);
        Assert.Equal(Now + Lifetime, export.ExpiresAt);
    }

    [Fact]
    public void An_export_over_the_size_cap_fails_naming_the_data_that_passed_it_and_keeps_no_archive()
    {
        var export = Requested();

        Assert.True(export.FailOverSizeCap(AccountExportData.ChangeSets, Now));

        Assert.Equal(AccountExportState.Failed, export.StateAt(Now));
        Assert.Equal(AccountExportFailure.SizeCap, export.Failure);
        Assert.Equal(AccountExportData.ChangeSets, export.OversizedData);
        Assert.Null(export.Content);
    }

    [Fact]
    public void Only_a_pending_export_completes_or_fails()
    {
        var ready = Requested();
        ready.Complete([1], Lifetime, Now);
        var failed = Requested();
        failed.FailOverSizeCap(AccountExportData.Nodes, Now);

        Assert.False(ready.Complete([2], Lifetime, Now));
        Assert.False(ready.FailOverSizeCap(AccountExportData.Nodes, Now));
        Assert.False(failed.Complete([2], Lifetime, Now));
        Assert.Equal(1, ready.SizeBytes);
        Assert.Equal(AccountExportFailure.SizeCap, failed.Failure);
    }

    [Fact]
    public void A_ready_export_expires_only_once_its_lifetime_is_over_and_then_drops_the_archive()
    {
        var export = Requested();
        export.Complete([1, 2], Lifetime, Now);

        Assert.False(export.Expire(Now + Lifetime - TimeSpan.FromSeconds(1)));
        Assert.NotNull(export.Content);

        Assert.True(export.Expire(Now + Lifetime));
        Assert.Equal(AccountExportState.Expired, export.StateAt(Now + Lifetime));
        Assert.Null(export.Content);
    }

    [Fact]
    public void A_ready_export_past_its_lifetime_already_reads_as_expired_before_the_expiry_runs()
    {
        var export = Requested();
        export.Complete([1], Lifetime, Now);

        Assert.Equal(AccountExportState.Expired, export.StateAt(Now + Lifetime));
        Assert.Equal(AccountExportState.Ready, export.StateAt(Now + Lifetime - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Only_a_ready_export_expires()
    {
        var pending = Requested();
        var failed = Requested();
        failed.FailOverSizeCap(AccountExportData.Nodes, Now);

        Assert.False(pending.Expire(Now + Lifetime));
        Assert.False(failed.Expire(Now + Lifetime));
    }

    [Fact]
    public void The_default_options_cap_the_archive_at_50_mb_and_keep_it_24_hours()
    {
        var options = new AccountExportOptions();

        Assert.Equal((50L * 1024 * 1024, TimeSpan.FromHours(24)), (options.MaxArchiveBytes, options.Lifetime));
        Assert.True(IsValid(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void A_read_batch_size_outside_1_to_10000_is_refused_at_startup(int readBatchSize)
    {
        var options = new AccountExportOptions { ReadBatchSize = readBatchSize };

        Assert.False(IsValid(options));
    }

    [Fact]
    public void The_size_budget_accepts_batches_up_to_the_cap_exactly()
    {
        var budget = new AccountExportSizeBudget(maxBytes: 100);

        Assert.True(budget.Charge(AccountExportData.Nodes, 60));
        Assert.True(budget.Charge(AccountExportData.Relations, 40));

        Assert.Equal(100, budget.UsedBytes);
        Assert.False(budget.IsExceeded);
        Assert.Null(budget.OversizedData);
    }

    [Fact]
    public void The_size_budget_names_the_data_whose_batch_passed_the_cap_by_one_byte()
    {
        var budget = new AccountExportSizeBudget(maxBytes: 100);
        budget.Charge(AccountExportData.Nodes, 100);

        Assert.False(budget.Charge(AccountExportData.ChangeSets, 1));

        Assert.True(budget.IsExceeded);
        Assert.Equal(AccountExportData.ChangeSets, budget.OversizedData);
    }

    [Fact]
    public void Once_past_the_cap_the_size_budget_keeps_the_first_data_that_passed_it()
    {
        var budget = new AccountExportSizeBudget(maxBytes: 10);
        budget.Charge(AccountExportData.Tags, 11);

        Assert.False(budget.Charge(AccountExportData.AgentReads, 0));

        Assert.Equal(AccountExportData.Tags, budget.OversizedData);
    }

    [Theory]
    [InlineData(0, 24 * 60)]
    [InlineData(1024, 24 * 60 + 1)]
    [InlineData(1024, 0)]
    public void A_cap_below_one_byte_or_a_lifetime_outside_zero_to_24_hours_is_refused_at_startup(long maxArchiveBytes, int lifetimeMinutes)
    {
        var options = new AccountExportOptions { MaxArchiveBytes = maxArchiveBytes, Lifetime = TimeSpan.FromMinutes(lifetimeMinutes) };

        Assert.False(IsValid(options));
    }

    private static AccountExport Requested() =>
        AccountExport.Request(Guid.CreateVersion7(Now), Guid.CreateVersion7(Now), Guid.CreateVersion7(Now), Now);

    private static bool IsValid(AccountExportOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(options, context, results, validateAllProperties: true)
            && !options.Validate(context).Any();
    }
}
