using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace LifeGraph.IntegrationTests.Jobs;

/// <summary>The job queue (DA-113): RLS on the table, the claim function across Accounts, and the runner.</summary>
public sealed class JobQueueTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string TestKind = "test.recorded";
    private const string FailingKind = "test.failing";

    private readonly RecordingHandler _recording = new();
    private LifeGraphApiFactory _baseFactory = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _accountA;
    private Guid _accountB;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _baseFactory = new LifeGraphApiFactory(database);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_recording);
                services.AddScoped<IJobHandler, RecordingJobHandler>();
                services.AddScoped<IJobHandler, FailingJobHandler>();
            });
        });
        _graph = new GraphWriteHarness(database, _baseFactory);
        _accountA = await _graph.CreateAccountAsync();
        _accountB = await _graph.CreateAccountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    [Fact]
    public async Task The_application_role_sees_only_the_jobs_of_the_account_in_context()
    {
        await InsertJobAsync(_accountA);
        await InsertJobAsync(_accountB);

        Assert.Equal(1, await InAccountAsync(_accountA, db => db.Set<Job>().CountAsync(TestContext.Current.CancellationToken)));
        Assert.Equal(_accountA, await InAccountAsync(_accountA, db => db.Set<Job>().Select(job => job.AccountId).SingleAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_job_for_another_account_than_the_one_in_context_is_refused()
    {
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => InAccountAsync(_accountA, async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Add(Job.Schedule(_accountB, TestKind, "{}", now, now));
            return await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ((PostgresException)refused.InnerException!).SqlState);
        Assert.Equal(0, await _graph.CountAsync("jobs"));
    }

    [Fact]
    public async Task The_claim_takes_due_jobs_of_every_account_and_leaves_the_rest()
    {
        var ofA = await InsertJobAsync(_accountA);
        var ofB = await InsertJobAsync(_accountB);
        await InsertJobAsync(_accountA, runAfter: "now() + interval '1 day'");

        var claimed = await Runner.ClaimAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal([(ofA, _accountA), (ofB, _accountB)], claimed.Select(job => (job.JobId, job.AccountId)).Order());
        Assert.Equal(2, await _graph.ScalarAsync<long>("SELECT count(*) FROM jobs WHERE status = 'running' AND lease_until > now() AND attempts = 1"));
        Assert.Empty(await Runner.ClaimAsync(10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_workers_never_claim_the_same_job()
    {
        for (var index = 0; index < 30; index++)
        {
            await InsertJobAsync(index % 2 == 0 ? _accountA : _accountB);
        }

        var claims = await Task.WhenAll(
            Runner.ClaimAsync(20, TestContext.Current.CancellationToken),
            Runner.ClaimAsync(20, TestContext.Current.CancellationToken));

        var all = claims.SelectMany(claim => claim.Select(job => job.JobId)).ToList();
        Assert.Equal(30, all.Count);
        Assert.Equal(30, all.Distinct().Count());
    }

    [Fact]
    public async Task An_expired_lease_is_claimed_again_until_its_attempts_are_spent()
    {
        var retried = await InsertJobAsync(_accountA, status: "running", attempts: 1, leaseUntil: "now() - interval '1 second'");
        var spent = await InsertJobAsync(_accountA, status: "running", attempts: Job.MaxAttempts, leaseUntil: "now() - interval '1 second'");
        await InsertJobAsync(_accountA, status: "running", attempts: 1, leaseUntil: "now() + interval '1 minute'");

        var claimed = await Runner.ClaimAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal([retried], claimed.Select(job => job.JobId));
        Assert.Equal(2, await AttemptsAsync(retried));
        Assert.Equal("dead", await StatusAsync(spent));
        Assert.Equal(JobRunner.LeaseExpiredError, await _graph.ScalarAsync<string>(
            "SELECT last_error FROM jobs WHERE id = @id", new NpgsqlParameter("id", spent)));

        // BE-036: the job the claim sent to dead reaches the log like any other dead job.
        var dead = Assert.Single(_factory.Services.GetFakeLogCollector().GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.Equal(spent.ToString(), dead.GetStructuredStateValue("JobId"));
        Assert.Equal(JobRunner.LeaseExpiredError, dead.GetStructuredStateValue("ErrorType"));
    }

    // The attempts and the lease come from Job; the cap lives in the function, tied here to MaxClaimBatch.
    [Fact]
    public async Task The_claim_leases_for_the_job_lease_and_takes_at_most_the_batch_cap()
    {
        await InsertJobAsync(_accountA, status: "running", attempts: Job.MaxAttempts - 1, leaseUntil: "now() - interval '1 second'");
        for (var index = 0; index < JobRunner.MaxClaimBatch; index++)
        {
            await InsertJobAsync(_accountA);
        }

        var claimed = await Runner.ClaimAsync(JobRunner.MaxClaimBatch * 2, TestContext.Current.CancellationToken);

        Assert.Equal(JobRunner.MaxClaimBatch, claimed.Count);
        Assert.Equal(Job.MaxAttempts, await _graph.ScalarAsync<int>("SELECT max(attempts) FROM jobs WHERE status = 'running'"));
        Assert.Equal(Job.Lease.TotalSeconds, await _graph.ScalarAsync<double>(
            "SELECT extract(epoch FROM lease_until - updated_at)::float8 FROM jobs WHERE status = 'running' LIMIT 1"));
        Assert.Single(await Runner.ClaimAsync(JobRunner.MaxClaimBatch, TestContext.Current.CancellationToken));
    }

    // The function runs as its owner across Accounts: it clamps what its caller passes.
    [Fact]
    public async Task The_claim_clamps_the_attempts_and_the_lease_it_is_given()
    {
        var fresh = await InsertJobAsync(_accountA);
        var expired = await InsertJobAsync(_accountA, status: "running", attempts: 1, leaseUntil: "now() - interval '1 second'");

        await database.ExecuteAsMigratorAsync(
            "SELECT count(*) FROM app.claim_due_jobs(10, 0, interval '10 days')",
            TestContext.Current.CancellationToken);

        Assert.Equal("running", await StatusAsync(fresh));
        Assert.Equal(TimeSpan.FromHours(1).TotalSeconds, await _graph.ScalarAsync<double>(
            "SELECT extract(epoch FROM lease_until - updated_at)::float8 FROM jobs WHERE id = @id", new NpgsqlParameter("id", fresh)));
        Assert.Equal("dead", await StatusAsync(expired));
    }

    [Fact]
    public async Task The_claim_returns_no_payload_runs_as_its_owner_and_only_the_application_role_may_call_it()
    {
        Assert.Equal(
            "TABLE(job_id uuid, account_id uuid, kind character varying, status character varying)",
            await _graph.ScalarAsync<string>("SELECT pg_get_function_result('app.claim_due_jobs(integer, integer, interval)'::regprocedure)"));
        Assert.True(await _graph.ScalarAsync<bool>("SELECT prosecdef FROM pg_proc WHERE oid = 'app.claim_due_jobs(integer, integer, interval)'::regprocedure"));
        Assert.True(await _graph.ScalarAsync<bool>(
            "SELECT 'search_path=pg_catalog, pg_temp' = ANY (proconfig) FROM pg_proc WHERE oid = 'app.claim_due_jobs(integer, integer, interval)'::regprocedure"));
        Assert.True(await _graph.ScalarAsync<bool>($"SELECT has_function_privilege('{DatabaseRoles.Application}', 'app.claim_due_jobs(integer, integer, interval)', 'EXECUTE')"));
        Assert.False(await _graph.ScalarAsync<bool>($"SELECT has_function_privilege('{DatabaseRoles.Provisioner}', 'app.claim_due_jobs(integer, integer, interval)', 'EXECUTE')"));
        Assert.False(await _graph.ScalarAsync<bool>("SELECT has_function_privilege('public', 'app.claim_due_jobs(integer, integer, interval)', 'EXECUTE')"));
    }

    [Fact]
    public async Task A_job_runs_in_a_transaction_of_its_own_account_and_is_marked_done_there()
    {
        var ofA = await InsertJobAsync(_accountA, kind: TestKind);
        var ofB = await InsertJobAsync(_accountB, kind: TestKind);

        Assert.Equal(2, await Runner.RunDueAsync(TestContext.Current.CancellationToken));

        Assert.Equal([(_accountA, _accountA, 1), (_accountB, _accountB, 1)], _recording.Runs.Order());
        Assert.Equal("done", await StatusAsync(ofA));
        Assert.Equal("done", await StatusAsync(ofB));
    }

    // BE-031, BE-036: retried with backoff, then dead, kept for inspection with only the error type.
    [Fact]
    public async Task A_failing_job_is_retried_later_and_goes_dead_when_its_attempts_are_spent()
    {
        var failing = await InsertJobAsync(_accountA, kind: FailingKind);

        await Runner.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal("pending", await StatusAsync(failing));
        Assert.Equal(nameof(TimeoutException), await _graph.ScalarAsync<string>("SELECT last_error FROM jobs WHERE id = @id", new NpgsqlParameter("id", failing)));
        Assert.True(await _graph.ScalarAsync<bool>("SELECT run_after > now() FROM jobs WHERE id = @id", new NpgsqlParameter("id", failing)));

        await database.ExecuteAsMigratorAsync(
            "UPDATE jobs SET attempts = @attempts, run_after = now() WHERE id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("attempts", Job.MaxAttempts - 1),
            new NpgsqlParameter("id", failing));
        await Runner.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal("dead", await StatusAsync(failing));
        Assert.Equal(Job.MaxAttempts, await AttemptsAsync(failing));
    }

    [Fact]
    public async Task A_job_of_an_unknown_kind_goes_dead()
    {
        var unknown = await InsertJobAsync(_accountA, kind: "nobody.handles");

        await Runner.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal("dead", await StatusAsync(unknown));
        Assert.Equal(JobRunner.UnknownKindError, await _graph.ScalarAsync<string>("SELECT last_error FROM jobs WHERE id = @id", new NpgsqlParameter("id", unknown)));
    }

    [Fact]
    public async Task The_in_process_worker_runs_due_jobs_on_its_own()
    {
        var job = await InsertJobAsync(_accountA, kind: "nobody.handles");
        await using var withWorker = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{JobsOptions.SectionName}:{nameof(JobsOptions.RunWorker)}"] = "true",
            [$"{JobsOptions.SectionName}:{nameof(JobsOptions.PollInterval)}"] = "00:00:00.200",
        });

        _ = withWorker.Services;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (await StatusAsync(job) != "dead" && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal("dead", await StatusAsync(job));
    }

    private JobRunner Runner => _factory.Services.GetRequiredService<JobRunner>();

    private async Task<T> InAccountAsync<T>(Guid accountId, Func<LifeGraphDbContext, Task<T>> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        return await db.InAccountTransactionAsync(_ => work(db), TestContext.Current.CancellationToken);
    }

    private async Task<Guid> InsertJobAsync(
        Guid accountId,
        string kind = TestKind,
        string status = "pending",
        int attempts = 0,
        string runAfter = "now() - interval '1 second'",
        string leaseUntil = "NULL")
    {
        var id = Guid.CreateVersion7();
        await database.ExecuteAsMigratorAsync(
            $$"""
            INSERT INTO jobs (id, account_id, kind, payload, status, run_after, lease_until, attempts, created_at, updated_at)
            VALUES (@id, @account_id, @kind, '{}'::jsonb, @status, {{runAfter}}, {{leaseUntil}}, @attempts, now(), now())
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", id),
            new NpgsqlParameter("account_id", accountId),
            new NpgsqlParameter("kind", kind),
            new NpgsqlParameter("status", status),
            new NpgsqlParameter("attempts", attempts));
        return id;
    }

    private Task<string> StatusAsync(Guid jobId) =>
        _graph.ScalarAsync<string>("SELECT status FROM jobs WHERE id = @id", new NpgsqlParameter("id", jobId));

    private Task<int> AttemptsAsync(Guid jobId) =>
        _graph.ScalarAsync<int>("SELECT attempts FROM jobs WHERE id = @id", new NpgsqlParameter("id", jobId));

    /// <summary>What each run saw: the Account in context, the job's Account and how many jobs RLS showed it.</summary>
    internal sealed class RecordingHandler
    {
        private readonly List<(Guid InContext, Guid Visible, int VisibleJobs)> _runs = [];

        public IReadOnlyList<(Guid InContext, Guid Visible, int VisibleJobs)> Runs
        {
            get
            {
                lock (_runs)
                {
                    return [.. _runs];
                }
            }
        }

        public void Add((Guid, Guid, int) run)
        {
            lock (_runs)
            {
                _runs.Add(run);
            }
        }
    }

    private sealed class RecordingJobHandler(RecordingHandler recording, IAccountContext accountContext, LifeGraphDbContext db) : IJobHandler
    {
        public string Kind => TestKind;

        public async Task RunAsync(string payload, CancellationToken cancellationToken)
        {
            var visible = await db.Set<Job>().Select(job => job.AccountId).ToListAsync(cancellationToken);
            recording.Add((accountContext.AccountId!.Value, visible.Distinct().Single(), visible.Count));
        }
    }

    private sealed class FailingJobHandler : IJobHandler
    {
        public string Kind => FailingKind;

        public Task RunAsync(string payload, CancellationToken cancellationToken) => throw new TimeoutException("Not this time.");
    }
}
