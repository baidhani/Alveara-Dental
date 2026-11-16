using System.Globalization;
using System.Text.RegularExpressions;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (T-2, decision 02): a date or time the server writes or reads in a fixed format (an API value, a key, a job payload, a file name, a fixed-format message) never depends on the
/// Windows culture of the machine. Two proofs:
/// - a source guard over the API: every date/time <c>ToString("...")</c> with a fixed pattern, every date/time <c>ParseExact</c>/<c>TryParseExact</c> and every interpolated date hole names the
///   invariant culture explicitly (no process-wide default culture is set: decision 02 forbids it, so each call must say it);
/// - behaviour under a non-Gregorian culture (th-TH, Thai Buddhist calendar: 1985 is 2528) and under a culture whose time separator is "." instead of ":".
/// </summary>
[Collection(CultureSensitiveTests.Name)]
public class TimeCultureGuardTests
{
    private static readonly string SrcRoot = FindSrcRoot();

    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Alveara.Api", "Alveara.Api.csproj"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("The repository root was not found from the test output folder.") : Path.Combine(dir.FullName, "src");
    }

    /// <summary>The projects the guard scans: the API and (ALV-N021 A2-R02, review CR-1) the chain host, which reads machine values too.</summary>
    private static readonly string[] ScannedProjects = ["Alveara.Api", "Alveara.Api.ChainHost"];

    private static IEnumerable<(string Path, int Line, string Text)> SourceLines() =>
        ScannedProjects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(SrcRoot, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f).Select((t, i) => (Path.GetRelativePath(SrcRoot, f).Replace('\\', '/'), i + 1, t)));

    // a fixed date/time pattern: contains a year, hour, minute, month or day token (not "N" for a Guid, not the culture-free round-trip "O"/"o")
    private const string DatePattern = @"[^""]*(yyyy|HH|mm|MM|dd)[^""]*";
    private static readonly Regex FixedFormatToString = new(@"\.ToString\(""" + DatePattern + @"""(\)|,)", RegexOptions.Compiled);
    private static readonly Regex DateParseExact = new(@"\b(DateOnly|DateTime|DateTimeOffset|TimeOnly)\.(Try)?ParseExact\(", RegexOptions.Compiled);
    // ALV-N021 A2-R02 (review CR-1): a non-exact date parse reads with the process culture unless it names one, so it must name the invariant culture too
    private static readonly Regex DateParse = new(@"\b(DateOnly|DateTime|DateTimeOffset|TimeOnly)\.(Try)?Parse\(", RegexOptions.Compiled);
    private static readonly Regex InterpolatedDateHole = new(@"\$@?""[^""]*\{[^{}]*:(yyyy|HH|MM|dd)[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex NamesInvariant = new(@"CultureInfo\.InvariantCulture|,\s*Inv\)", RegexOptions.Compiled);

    /// <summary>
    /// Reviewed exceptions: a line that formats a date for intentional HUMAN-LOCALIZED display would be listed here with its reason. There are none: every match in the API today is a
    /// machine or fixed-format value. Adding an entry is a reviewed decision, never a way to silence the guard.
    /// </summary>
    private static readonly (string Path, string Reason)[] LocalizedDisplayAllowlist = [];

    internal static bool IsOffending(string line) =>
        (FixedFormatToString.IsMatch(line) || DateParseExact.IsMatch(line) || DateParse.IsMatch(line) || InterpolatedDateHole.IsMatch(line)) && !NamesInvariant.IsMatch(line);

    [Fact]
    public void Every_fixed_format_date_or_time_in_the_api_names_the_invariant_culture()
    {
        var offenders = SourceLines().Where(l => IsOffending(l.Text) && !LocalizedDisplayAllowlist.Any(a => a.Path == l.Path))
            .Select(l => $"{l.Path}:{l.Line}: {l.Text.Trim()}").ToList();
        Assert.True(offenders.Count == 0, "Name CultureInfo.InvariantCulture at:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_guard_sees_the_api_and_the_sites_it_protects()
    {
        var lines = SourceLines().ToList();
        Assert.True(lines.Select(l => l.Path).Distinct().Count() > 100, "the guard found too few source files");
        Assert.True(lines.Count(l => FixedFormatToString.IsMatch(l.Text) || DateParseExact.IsMatch(l.Text) || InterpolatedDateHole.IsMatch(l.Text)) >= 40, "the guard no longer sees the date sites it protects");
        Assert.Contains(lines, l => l.Path == "Alveara.Api.ChainHost/ChainHostMain.cs" && DateParse.IsMatch(l.Text));   // ALV-N021 A2-R02: the chain host is scanned and its clock parse is seen
    }

    [Theory]
    [InlineData(@"var s = p.DateOfBirth.ToString(""yyyy-MM-dd"");")]
    [InlineData(@"var s = a.StartLocal.ToString(""HH:mm"");")]
    [InlineData(@"var d = DateOnly.ParseExact(payload.DueDate, ""yyyy-MM-dd"");")]
    [InlineData(@"var k = $""recall:{id:N}:{due:yyyyMMdd}"";")]
    [InlineData(@"if (!DateTimeOffset.TryParse(Environment.GetEnvironmentVariable(""CHAIN_CLOCK_UTC"") ?? DefaultClockUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var start))")]   // the A2-R01 line (review CR-1)
    [InlineData(@"var d = DateTime.Parse(text);")]
    [InlineData(@"var ok = DateOnly.TryParse(text, out var d);")]
    public void Negative_control_the_guard_flags_a_fixed_format_without_the_invariant_culture(string line) => Assert.True(IsOffending(line));

    [Theory]
    [InlineData(@"var s = p.DateOfBirth.ToString(""yyyy-MM-dd"", CultureInfo.InvariantCulture);")]
    [InlineData(@"var k = string.Create(CultureInfo.InvariantCulture, $""recall:{id:N}:{due:yyyyMMdd}"");")]
    [InlineData(@"var s = h.ChangedAtUtc.ToString(""O"");")]
    [InlineData(@"var s = id.ToString(""N"");")]
    [InlineData(@"var ok = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed);")]
    public void The_guard_accepts_an_explicit_invariant_culture_and_culture_free_formats(string line) => Assert.False(IsOffending(line));

    // ---------- behaviour under other machine cultures ----------

    private static CultureInfo Thai() => CultureInfo.GetCultureInfo("th-TH");

    private static CultureInfo DottedTime()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        c.DateTimeFormat.TimeSeparator = ".";
        return c;
    }

    public static TheoryData<string> Cultures => new() { "th-TH", "dotted-time", "invariant" };

    private static T Under<T>(string culture, Func<T> action)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var c = culture switch { "th-TH" => Thai(), "dotted-time" => DottedTime(), _ => CultureInfo.InvariantCulture };
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
        try { return action(); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved; }
    }

    [Fact]
    public void The_two_cultures_really_change_an_unpinned_format()
    {
        // what the defect would look like: proves the cultures used below are hostile, so the passing tests mean something
        Assert.Equal("2528-03-14", Under("th-TH", () => new DateOnly(1985, 3, 14).ToString("yyyy-MM-dd")));
        Assert.Equal("09.30", Under("dotted-time", () => new TimeOnly(9, 30).ToString("HH:mm")));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Keys_and_fixed_values_are_the_same_on_every_machine_culture(string culture)
    {
        var id = new Guid("11111111-2222-3333-4444-555555555555");
        Assert.Equal("recall:11111111222233334444555555555555:20300115", Under(culture, () => FollowUpRules.RecallSourceKey(id, new DateOnly(2030, 1, 15))));
        Assert.Equal("lab:11111111222233334444555555555555:20300115", Under(culture, () => FollowUpRules.LabSourceKey(id, new DateOnly(2030, 1, 15))));
        Assert.EndsWith("|1985-03-14", Under(culture, () => Patient.BuildDuplicateKey("Ann", "Lee", new DateOnly(1985, 3, 14))));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_appointment_local_time_is_the_same_on_every_machine_culture(string culture)
    {
        // the builder's Local() uses only the clock; the database is not touched
        var builder = new AppointmentViewBuilder(null!, SchedulingTestSupport.Clock);
        Assert.Equal("2030-01-14T09:30", Under(culture, () => builder.Local(new DateTimeOffset(2030, 1, 14, 15, 30, 0, TimeSpan.Zero))));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_fixed_format_message_names_the_gregorian_date_on_every_machine_culture(string culture)
    {
        var refusal = Under(culture, () => Assert.Throws<LocalTimeConversionException>(() => SchedulingTestSupport.Clock.FromPracticeLocal(new DateTime(2030, 3, 10, 2, 30, 0))));
        Assert.StartsWith("2030-03-10 02:30 does not exist", refusal.Message);
    }
}

/// <summary>Tests that change the thread culture never run beside each other.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureSensitiveTests
{
    public const string Name = "Culture-sensitive";
}
