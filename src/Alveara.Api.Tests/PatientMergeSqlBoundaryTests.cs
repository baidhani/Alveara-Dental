using System.Text.RegularExpressions;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Tests;

/// <summary>
/// F-003 (ALV-N019): the merge and unmerge SQL interpolates identifiers and key expressions, never request text. This pins the boundary that justification rests on: the identifiers come from the
/// <see cref="PatientOwnedData"/> catalog and are plain names, and <see cref="PatientMergeCatalog.Q"/> neutralises a closing bracket, so even a hostile name could not end its quoted identifier.
/// </summary>
public class PatientMergeSqlBoundaryTests
{
    [Fact]
    public void Every_table_and_column_the_merge_interpolates_is_a_plain_identifier()
    {
        Assert.NotEmpty(PatientOwnedData.All);
        Assert.All(PatientOwnedData.All, c =>
        {
            Assert.Matches(new Regex("^[A-Za-z][A-Za-z0-9_]*$"), c.Table);
            Assert.Matches(new Regex("^[A-Za-z][A-Za-z0-9_]*$"), c.Column);
        });
    }

    [Theory]
    [InlineData("Patients", "[Patients]")]
    [InlineData("a]b", "[a]]b]")]
    [InlineData("x]; DROP TABLE Patients; --", "[x]]; DROP TABLE Patients; --]")]
    [InlineData("", "[]")]
    public void Quoting_an_identifier_doubles_every_closing_bracket_so_it_cannot_be_ended_early(string identifier, string expected)
        => Assert.Equal(expected, PatientMergeCatalog.Q(identifier));

    [Fact]
    public void The_session_flag_that_is_interpolated_is_a_fixed_plain_constant()
        => Assert.Matches(new Regex("^[a-z_.]+$"), PatientOwnedData.MergeFlag);
}
