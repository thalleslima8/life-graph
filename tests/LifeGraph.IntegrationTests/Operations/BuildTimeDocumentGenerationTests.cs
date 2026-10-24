using System.Reflection;
using System.Reflection.Emit;
using LifeGraph.Host.OpenApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace LifeGraph.IntegrationTests.Operations;

public sealed class BuildTimeDocumentGenerationTests
{
    /// <summary>A clean machine: no Development settings, no environment configuration.</summary>
    private const string CleanEnvironment = "Production";

    [Fact]
    public void Only_the_document_tool_counts_as_build_time_generation()
    {
        var tool = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(BuildTimeDocumentGeneration.ToolAssemblyName), AssemblyBuilderAccess.Run);

        Assert.True(BuildTimeDocumentGeneration.IsRunningUnder(tool));
        Assert.False(BuildTimeDocumentGeneration.IsRunningUnder(typeof(Program).Assembly));
        Assert.False(BuildTimeDocumentGeneration.IsRunningUnder(Assembly.GetEntryAssembly()));
        Assert.False(BuildTimeDocumentGeneration.IsRunningUnder(null));
    }

    [Fact]
    public async Task Placeholder_settings_let_the_host_start_on_a_clean_machine()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(CleanEnvironment);
            foreach (var (key, value) in BuildTimeDocumentGeneration.PlaceholderSettings)
            {
                builder.UseSetting(key, value);
            }
        });

        // Resolving the services starts the host, which runs the startup validation.
        Assert.NotNull(factory.Services);
    }

    [Fact]
    public async Task Without_configuration_the_host_still_refuses_to_start()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment(CleanEnvironment));

        var startup = Assert.ThrowsAny<Exception>(() => factory.Services);

        var failures = startup is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [startup];
        Assert.All(failures, failure => Assert.IsType<OptionsValidationException>(failure));
    }
}
