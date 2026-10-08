using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LifeGraph.Accounts;
using LifeGraph.Accounts.Csrf;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LifeGraph.IntegrationTests.Http;

/// <summary>
/// The error contract of every response (DA-101 to DA-105): Limaj's V3 Problem Details,
/// a catalogued <c>code</c> on every error, and a generic 500 even in Development.
/// </summary>
public sealed class ErrorContractTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string ThrowingPath = "/test/throws";
    private const string BadRequestPath = "/test/bad-request";
    private const string ExceptionSecret = "node title: my private diary";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task An_unhandled_exception_in_development_is_a_generic_500_with_a_code()
    {
        // The public profile exposes the app as Development (DA-027/028, DA-102).
        await using var development = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpoint>());
        });
        using var spa = new SpaClient(development.CreateClient());
        (await spa.LoginAsync(Email, TestAccounts.Password)).EnsureSuccessStatusCode();

        var response = await spa.GetAsync(ThrowingPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(ExceptionSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(CommonErrors.Unexpected.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(CommonErrors.UnexpectedMessage, problem.RootElement.GetProperty("detail").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    // GEN-030: since .NET 10 the middleware no longer logs an exception an IExceptionHandler
    // handled, so the handler logs it: a bug at Error, an unreadable request (the client's
    // mistake) at Information (GEN-041). Only the exception's type, never its message (GEN-043).
    [Theory]
    [InlineData(ThrowingPath, HttpStatusCode.InternalServerError, LogLevel.Error, nameof(InvalidOperationException))]
    [InlineData(BadRequestPath, HttpStatusCode.BadRequest, LogLevel.Information, null)]
    public async Task An_exception_the_handler_writes_is_logged_at_its_level(string path, HttpStatusCode status, LogLevel level, string? exceptionType)
    {
        await using var logged = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpoint>());
        });
        using var spa = new SpaClient(logged.CreateClient());
        (await spa.LoginAsync(Email, TestAccounts.Password)).EnsureSuccessStatusCode();

        var response = await spa.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        var handled = logged.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Category == "LifeGraph.Http.LifeGraphExceptionHandler")
            .ToList();
        var entry = Assert.Single(handled);
        Assert.Equal(level, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(exceptionType, entry.GetStructuredStateValue("ExceptionType"));
        Assert.DoesNotContain(ExceptionSecret, entry.Message, StringComparison.Ordinal);
    }

    // Without a session the routing rejections (405, 415, unknown route) are 401 too (DA-109).
    public static TheoryData<bool, string, string, string?, string?, HttpStatusCode> ErrorsWithoutAnEndpointResult => new()
    {
        // signed in, method, path, body, content type, expected status
        { false, "GET", "/api/sessions/current", null, null, HttpStatusCode.Unauthorized },
        { false, "GET", "/api/no-such-route", null, null, HttpStatusCode.Unauthorized },
        { false, "POST", "/api/sessions", "{ not json", "application/json", HttpStatusCode.BadRequest },
        { true, "GET", "/api/no-such-route", null, null, HttpStatusCode.NotFound },
        { true, "POST", "/api/sessions", "email=a", "text/plain", HttpStatusCode.UnsupportedMediaType },
        { true, "PUT", "/api/sessions", "{}", "application/json", HttpStatusCode.MethodNotAllowed },
    };

    [Theory]
    [MemberData(nameof(ErrorsWithoutAnEndpointResult))]
    public async Task Every_error_response_carries_a_catalogued_code(
        bool signedIn,
        string method,
        string path,
        string? body,
        string? contentType,
        HttpStatusCode expectedStatus)
    {
        using var spa = signedIn ? await TestAccounts.SignedInAsync(_factory, Email) : new SpaClient(_factory.CreateClient());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(CsrfProtection.HeaderName, await spa.GetCsrfTokenAsync());
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType!);
        }

        var response = await spa.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        var code = await ProblemCode.ReadAsync(response);
        Assert.True(Catalog.TryGet(code!, out var entry), $"'{code}' is not in the catalog.");
        Assert.Equal((int)expectedStatus, entry.Status);
    }

    // The SPA reads only the code, never title or detail (DA-103), so neither is pinned here.
    [Fact]
    public async Task A_failed_result_is_written_in_the_v3_format()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var response = await spa.LoginAsync(Email, "not the password at all");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AccountsErrors.InvalidCredentials.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public void Every_declared_error_code_is_in_the_catalog()
    {
        var declared = typeof(Program).Assembly.GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith("LifeGraph.", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Append(typeof(Program).Assembly)
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(ErrorCode))
            .Select(field => (Field: $"{field.DeclaringType!.Name}.{field.Name}", Code: (ErrorCode)field.GetValue(null)!))
            .ToList();

        Assert.Contains(declared, entry => entry.Code == AccountsErrors.InvalidCredentials);
        Assert.All(declared, entry => Assert.True(
            Catalog.TryGet(entry.Code.Code, out var catalogued) && ReferenceEquals(catalogued, entry.Code),
            $"{entry.Field} ('{entry.Code.Code}') is not registered in the catalog."));
    }

    [Fact]
    public async Task The_openapi_document_publishes_the_catalog()
    {
        var document = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "openapi", "lifegraph.json"),
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(document);

        var published = json.RootElement.GetProperty("x-error-codes").EnumerateArray()
            .Select(entry => entry.GetProperty("code").GetString()!)
            .ToHashSet();

        Assert.Equal(Catalog.All.Select(entry => entry.Code).ToHashSet(), published);
    }

    private ErrorCatalog Catalog => _factory.Services.GetRequiredService<ErrorCatalog>();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LifeGraph.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("LifeGraph.sln not found above the test output.");
    }

    /// <summary>
    /// Appended after the app's own pipeline, so the exception goes through the real exception
    /// handler (a branch ahead of it would meet the developer exception page instead).
    /// </summary>
    private sealed class ThrowingEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowingPath, branch => branch.Run(_ => throw new InvalidOperationException(ExceptionSecret)));
            app.Map(BadRequestPath, branch => branch.Run(_ => throw new BadHttpRequestException(ExceptionSecret)));
        };
    }
}
