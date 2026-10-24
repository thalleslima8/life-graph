using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.UnitTests.Errors;

public sealed class ErrorCodeTests
{
    [Theory]
    [InlineData("validation_failed")]
    [InlineData("graph.node_not_found")]
    [InlineData("accounts.invalid_credentials")]
    public void Snake_case_codes_with_an_optional_module_prefix_are_accepted(string code) =>
        Assert.Equal(code, ErrorCode.Validation(code).Code);

    [Theory]
    [InlineData("EmailTooLong")]
    [InlineData("graph.nodes.not_found")]
    [InlineData("graph-node")]
    [InlineData("")]
    [InlineData(".not_found")]
    public void Codes_outside_the_convention_are_refused(string code) =>
        Assert.Throws<ArgumentException>(() => ErrorCode.Validation(code));

    [Fact]
    public void Details_are_public_only_when_the_code_declares_it()
    {
        var conflict = ErrorCode.Conflict("graph.node_version_conflict");
        var withDetails = conflict.WithPublicDetails();

        Assert.False(conflict.HasPublicDetails);
        Assert.True(withDetails.HasPublicDetails);
        Assert.Equal((conflict.Code, conflict.Status, conflict.Type), (withDetails.Code, withDetails.Status, withDetails.Type));
    }

    [Fact]
    public void A_business_rule_is_a_validation_error_answered_as_422()
    {
        var rule = ErrorCode.BusinessRule("graph.type_in_use");

        Assert.Equal(ErrorType.Validation, rule.Type);
        Assert.Equal(422, rule.Status);
        Assert.Equal(ErrorRecovery.FixInput, rule.Recovery);
    }

    [Fact]
    public void The_error_carries_the_code_type_details_and_wait()
    {
        var details = new Dictionary<string, string[]> { ["title"] = ["Required."] };

        var error = ErrorCode.TooManyRequests("accounts.too_many_attempts")
            .ToError("Wait.", details, TimeSpan.FromSeconds(30));

        Assert.Equal("accounts.too_many_attempts", error.Code);
        Assert.Equal(ErrorType.TooManyRequests, error.Type);
        Assert.Equal("Wait.", error.Message);
        Assert.Same(details, error.Details);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public void The_catalog_refuses_two_declarations_of_one_code()
    {
        var first = ErrorCode.NotFound("graph.node_not_found");
        var second = ErrorCode.NotFound("graph.node_not_found");

        Assert.Throws<InvalidOperationException>(() => new ErrorCatalog([first, second]));
    }

    [Fact]
    public void The_catalog_accepts_the_same_declaration_registered_twice()
    {
        var catalog = new ErrorCatalog([.. CommonErrors.All, .. CommonErrors.All]);

        Assert.Equal(CommonErrors.All.Count, catalog.All.Count);
        Assert.True(catalog.TryGet("unexpected_error", out var entry));
        Assert.Same(CommonErrors.Unexpected, entry);
    }
}
