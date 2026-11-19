using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Xunit;
using Alveara.Api.Architecture.Logging;
using Alveara.Api.Architecture.Measurement;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 1 (PKG-10 group A observability), no database:
/// - F-128: every code becomes an accepted measurement category (<c>^[a-z][a-z0-9_]{0,39}$</c>), including the scheduling refusal codes and odd spellings;
/// - F-126 / F-010: a cancellation while the client has gone away is a client abort, not a fault; the same exception without an aborted request is not;
/// - F-127: the SQL error number is found anywhere in the exception chain (the middleware logs it with method and path, never the message).
/// </summary>
public class ObservabilityClassificationTests
{
    private static readonly Regex Accepted = new(@"^[a-z][a-z0-9_]{0,39}$");

    [Theory]
    [InlineData("appointment_conflict", "appointment_conflict")]
    [InlineData("InChair", "in_chair")]
    [InlineData("visit-flow refused", "visit_flow_refused")]
    [InlineData("409", "c_409")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("__Odd..Code__", "odd_code")]
    public void Any_code_becomes_an_accepted_measurement_category(string? code, string expected)
    {
        var category = MeasurementCategory.Of(code);
        Assert.Equal(expected, category);
        Assert.Matches(Accepted, category);
    }

    [Fact]
    public void A_very_long_code_is_cut_to_forty_characters_and_still_accepted()
    {
        var category = MeasurementCategory.Of(new string('x', 30) + "_" + new string('y', 30));
        Assert.True(category.Length <= 40);
        Assert.Matches(Accepted, category);
    }

    [Fact]
    public void A_cancellation_is_a_client_abort_only_when_the_request_itself_was_aborted()
    {
        var aborted = new DefaultHttpContext();
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            aborted.RequestAborted = cts.Token;
            Assert.True(SafeExceptionHandlingMiddleware.IsClientAbort(new TaskCanceledException(), aborted));
            Assert.True(SafeExceptionHandlingMiddleware.IsClientAbort(new InvalidOperationException("wrapped", new OperationCanceledException()), aborted));
            Assert.False(SafeExceptionHandlingMiddleware.IsClientAbort(new InvalidOperationException("a real fault"), aborted));
        }
        Assert.False(SafeExceptionHandlingMiddleware.IsClientAbort(new TaskCanceledException(), new DefaultHttpContext()));   // a timeout inside the server is still a fault
    }

    [Fact]
    public void No_sql_number_is_reported_for_an_exception_without_one()
    {
        Assert.Null(SafeExceptionHandlingMiddleware.SqlNumber(new InvalidOperationException("x", new TimeoutException())));
    }
}
