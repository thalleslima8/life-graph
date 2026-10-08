using System.Reflection;
using LifeGraph.Accounts;
using LifeGraph.Accounts.ClientMetadata;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Agents;
using LifeGraph.Collections;
using LifeGraph.Graph;
using LifeGraph.Graph.Http;
using LifeGraph.Host.Observability;
using LifeGraph.Host.OpenApi;
using LifeGraph.Host.Operations;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.Resources;
using LifeGraph.Semantic;
using LifeGraph.Sharing;
using Microsoft.AspNetCore.Authorization;

// `accounts ...` runs the owner's account command instead of the web server (DA-095).
var isAccountsCommand = AccountsCommandLine.IsInvocation(args);
var builder = WebApplication.CreateBuilder(isAccountsCommand ? [] : args);

if (BuildTimeDocumentGeneration.IsRunningUnder(Assembly.GetEntryAssembly()))
{
    builder.Configuration.AddInMemoryCollection(BuildTimeDocumentGeneration.PlaceholderSettings);
}

builder.AddLifeGraphObservability();

builder.Services.AddLifeGraphHttpErrors();
builder.Services.AddLifeGraphHttpJson();
builder.Services.AddLifeGraphApiRateLimiting();
builder.Services.AddOpenApi(options => options
    .AddOperationTransformer<RetryAfterHeaderTransformer>()
    .AddDocumentTransformer<ErrorCatalogDocumentTransformer>());

// The owner's CLI connects as the provisioning role, the web process never does (DA-107).
builder.Services.AddLifeGraphPersistence(builder.Configuration, AccountsCommandLine.ConnectionStringNameFor(args));
builder.Services.AddLifeGraphHealthChecks();
builder.Services.AddLifeGraphPublicExposure();
builder.Services.AddLifeGraphJobs(builder.Configuration);

// Authentication by default (API-080): every endpoint needs a signed-in principal unless
// it opts out with AllowAnonymous.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services
    .AddAccountsModule()
    .AddGraphModule()
    .AddAgentsModule()
    .AddResourcesModule()
    .AddSemanticModule()
    .AddSharingModule()
    .AddCollectionsModule();

var app = builder.Build();

if (isAccountsCommand)
{
    return await AccountsCommandLine.RunAsync(app.Services, args, Console.Out, CancellationToken.None);
}

// First: the tunnel's forwarded scheme and client, and nothing beyond MCP and OAuth exposed (DA-028).
app.UseLifeGraphPublicExposure();

// Method, path, status and duration of each request; never the query string (DA-123).
app.UseHttpLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Before authentication: the OpenIddict server answers the issuer's routes there, so their
// budgets and the CIMD client registration must come first (DA-028, DA-029). Both read the
// form, so the body cap of those routes comes before them (API-082, DA-116).
app.UseIssuerRequestBodyLimit();
app.UseOAuthRateLimits();
app.UseClientMetadataDocuments();

app.UseAuthentication();
app.UseAuthorization();

// After authorization: no session is 401 before any budget or size check (DA-109, DA-116).
app.UseRequestBodyLimit();
app.UseRateLimiter();

app.MapLifeGraphHealthChecks();
app.MapAccountsEndpoints();
app.MapGraphEndpoints();
app.MapAgentsEndpoints();

app.Run();
return 0;
