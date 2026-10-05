using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Http;

public sealed class LifeGraphErrorHttpMapperTests
{
    private static readonly ErrorCode Rule = ErrorCode.BusinessRule("graph.type_in_use");

    private readonly LifeGraphErrorHttpMapper _mapper = new(
        Options.Create(new LimajHttpErrorOptions
        {
            Format = LimajProblemDetailsFormat.V3,
            IncludeExceptionDetails = false,
            IncludeDetailsOutsideValidation = false,
        }),
        new ErrorCatalog([.. CommonErrors.All, Rule]));

    [Fact]
    public void A_catalogued_business_rule_answers_422_with_its_code_and_message()
    {
        var problem = Problem(_mapper.Map(Rule.ToError("The Type is still in use.")));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.Status);
        Assert.Equal("graph.type_in_use", problem.Extensions["code"]);
        Assert.Equal("The Type is still in use.", problem.Detail);
        Assert.Equal("Unprocessable Entity", problem.Title);
    }

    [Fact]
    public void An_uncatalogued_code_keeps_the_mapping_by_type()
    {
        var problem = Problem(_mapper.Map(new Error("graph.unlisted", "Rule broken.", ErrorType.Validation)));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public void An_unexpected_error_sends_the_generic_message_and_its_code()
    {
        var problem = Problem(_mapper.Map(CommonErrors.Unexpected.ToError("Npgsql: connection refused to 10.0.0.5")));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal(DefaultErrorHttpMapper.GenericUnexpectedMessage, problem.Detail);
        Assert.Equal("unexpected_error", problem.Extensions["code"]);
    }

    [Fact]
    public void Details_outside_validation_stay_on_the_server()
    {
        var error = ErrorCode.Conflict("graph.version_conflict")
            .ToError("Changed meanwhile.", new Dictionary<string, string[]> { ["node"] = ["internal"] });
        var mapper = new LifeGraphErrorHttpMapper(
            Options.Create(new LimajHttpErrorOptions { Format = LimajProblemDetailsFormat.V3, IncludeExceptionDetails = false }),
            new ErrorCatalog([ErrorCode.Conflict("graph.version_conflict")]));

        var problem = Problem(mapper.Map(error));

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.False(problem.Extensions.ContainsKey("details"));
    }

    // DA-118: the client needs the current version and the conflicting entries, while the
    // global option stays off (DA-102).
    [Fact]
    public void A_code_with_public_details_writes_them_outside_validation_too()
    {
        var conflict = ErrorCode.Conflict("graph.node_version_conflict").WithPublicDetails();
        var mapper = new LifeGraphErrorHttpMapper(
            Options.Create(new LimajHttpErrorOptions { Format = LimajProblemDetailsFormat.V3, IncludeExceptionDetails = false }),
            new ErrorCatalog([conflict]));

        var result = mapper.Map(conflict.ToError("Changed meanwhile.", new Dictionary<string, string[]> { ["version"] = ["3"] }));
        var problem = Problem(result);

        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("graph.node_version_conflict", problem.Extensions["code"]);
        Assert.Contains("\"3\"", System.Text.Json.JsonSerializer.Serialize(problem, problem.GetType()), StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_copy_for_public_details_covers_every_current_option()
    {
        var current = typeof(LimajHttpErrorOptions).GetProperties()
            .Where(property => property.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Length == 0)
            .Select(property => property.Name)
            .Order();
        var original = new LimajHttpErrorOptions { Format = LimajProblemDetailsFormat.V3, IncludeExceptionDetails = false };

        var copy = LifeGraphErrorHttpMapper.WithDetailsOutsideValidation(original);

        // A new Limaj option must be copied on purpose; this list fails until it is.
        Assert.Equal(["Format", "IncludeDetailsOutsideValidation", "IncludeExceptionDetails"], current);
        Assert.Equal((LimajProblemDetailsFormat.V3, (bool?)false, true), (copy.Format, copy.IncludeExceptionDetails, copy.IncludeDetailsOutsideValidation));
        Assert.False(original.IncludeDetailsOutsideValidation);
    }

    [Theory]
    [InlineData(StatusCodes.Status429TooManyRequests, "too_many_requests")]
    [InlineData(StatusCodes.Status400BadRequest, "bad_request")]
    [InlineData(StatusCodes.Status401Unauthorized, "unauthorized")]
    [InlineData(StatusCodes.Status403Forbidden, "forbidden")]
    [InlineData(StatusCodes.Status404NotFound, "not_found")]
    [InlineData(StatusCodes.Status405MethodNotAllowed, "method_not_allowed")]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "payload_too_large")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type")]
    [InlineData(StatusCodes.Status500InternalServerError, "unexpected_error")]
    [InlineData(StatusCodes.Status503ServiceUnavailable, "unexpected_error")]
    public void A_problem_without_a_code_gets_the_common_code_of_its_status(int status, string code)
    {
        var context = new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext(),
            ProblemDetails = new ProblemDetails { Status = status },
        };

        ProblemCodeFallback.Apply(context);

        Assert.Equal(code, context.ProblemDetails.Extensions["code"]);
        Assert.Contains(CommonErrors.All, entry => entry.Code == code && (entry.Status == status || status >= 500));
    }

    [Fact]
    public void A_problem_that_has_a_code_keeps_it()
    {
        var context = new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext(),
            ProblemDetails = new ProblemDetails { Status = 422, Extensions = { ["code"] = "graph.type_in_use" } },
        };

        ProblemCodeFallback.Apply(context);

        Assert.Equal("graph.type_in_use", context.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void The_safety_net_turns_any_exception_into_the_generic_unexpected_error()
    {
        var error = new SafetyNetExceptionToErrorMapper().Map(new InvalidOperationException("node title: my diary"));

        Assert.Equal("unexpected_error", error?.Code);
        Assert.Equal(CommonErrors.UnexpectedMessage, error?.Message);
    }

    private static ProblemDetails Problem(IResult result) => result switch
    {
        ProblemHttpResult problem => problem.ProblemDetails,
        ValidationProblem validation => validation.ProblemDetails,
        _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}."),
    };
}
