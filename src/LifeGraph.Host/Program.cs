using LifeGraph.Accounts;
using LifeGraph.Agents;
using LifeGraph.Changes;
using LifeGraph.Collections;
using LifeGraph.Graph;
using LifeGraph.Host.Observability;
using LifeGraph.Host.Operations;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.Resources;
using LifeGraph.Semantic;
using LifeGraph.Sharing;

var builder = WebApplication.CreateBuilder(args);

builder.AddLifeGraphObservability();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddLifeGraphPersistence(builder.Configuration);
builder.Services.AddLifeGraphHealthChecks();

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

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapLifeGraphHealthChecks();

app.Run();
