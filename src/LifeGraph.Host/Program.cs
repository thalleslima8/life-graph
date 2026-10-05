using System.Reflection;
using LifeGraph.Accounts;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Agents;
using LifeGraph.Changes;
using LifeGraph.Collections;
using LifeGraph.Graph;
using LifeGraph.Host.Observability;
using LifeGraph.Host.OpenApi;
using LifeGraph.Host.Operations;
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

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddOperationTransformer<RetryAfterHeaderTransformer>());
builder.Services.AddLifeGraphPersistence(builder.Configuration);
builder.Services.AddLifeGraphHealthChecks();

// Authentication by default (API-080): every endpoint needs a signed-in principal unless
// it opts out with AllowAnonymous.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services
    .AddAccountsModule()
    .AddGraphModule()
    .AddChangesModule()
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

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapLifeGraphHealthChecks();
app.MapAccountsEndpoints();

app.Run();
return 0;
