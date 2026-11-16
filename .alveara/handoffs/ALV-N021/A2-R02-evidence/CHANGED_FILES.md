# ALV-N021 A2-R02: changed files (git diff 7fcefe6..b7efad6)

R01 implementation `2ef0817`; then the R01 handoff/evidence/status commits `27dda5c`, `f0f81bb` and the review record `cec8c8a`, `7fcefe6` (documentation only). R02 implementation `b7efad6` is one commit containing only CR-1:

```
 PROGRESS.md                                       |  6 ++
 src/Alveara.Api.ChainHost/ChainHostMain.cs        | 15 ++++-
 src/Alveara.Api.Tests/ChainHostClockParseTests.cs | 70 +++++++++++++++++++++++
 src/Alveara.Api.Tests/TimeCultureGuardTests.cs    | 22 +++++--
 4 files changed, 106 insertions(+), 7 deletions(-)
```

| Status | Path |
|---|---|
| M | `PROGRESS.md` |
| M | `src/Alveara.Api.ChainHost/ChainHostMain.cs` |
| A | `src/Alveara.Api.Tests/ChainHostClockParseTests.cs` |
| M | `src/Alveara.Api.Tests/TimeCultureGuardTests.cs` |

Executable code changed between R01 and R02: only `src/Alveara.Api.ChainHost/ChainHostMain.cs` (`git diff --stat 2ef0817..b7efad6 -- src` lists exactly the three files above). Full diff:

```diff
diff --git a/src/Alveara.Api.ChainHost/ChainHostMain.cs b/src/Alveara.Api.ChainHost/ChainHostMain.cs
index d69ccdb..988c9ff 100644
--- a/src/Alveara.Api.ChainHost/ChainHostMain.cs
+++ b/src/Alveara.Api.ChainHost/ChainHostMain.cs
@@ -1,3 +1,4 @@
+using System.Globalization;
 using Alveara.Api.Architecture.Time;
 
 namespace Alveara.Api.ChainHost;
@@ -11,10 +12,22 @@ public static class ChainHostMain
 {
     public const string DefaultClockUtc = "2026-01-15T15:00:00Z";
 
+    /// <summary>
+    /// ALV-N021 A2-R02 (review CR-1, decision 02 T-2): <c>CHAIN_CLOCK_UTC</c> is a documented machine value (ISO 8601 instant), so it is read with the invariant culture -
+    /// never the process culture, which on a Thai (Buddhist calendar) or dotted-time machine reads the same text differently or not at all. A value without an offset is UTC;
+    /// the result is always UTC.
+    /// </summary>
+    public static bool TryParseClockUtc(string? value, out DateTimeOffset utc)
+    {
+        var ok = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed);
+        utc = ok ? parsed.ToUniversalTime() : default;
+        return ok;
+    }
+
     public static async Task<int> Main(string[] args)
     {
         var url = Environment.GetEnvironmentVariable("CHAIN_URL") ?? "http://localhost:5072";
-        if (!DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("CHAIN_CLOCK_UTC") ?? DefaultClockUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var start))
+        if (!TryParseClockUtc(Environment.GetEnvironmentVariable("CHAIN_CLOCK_UTC") ?? DefaultClockUtc, out var start))
         {
             Console.Error.WriteLine("CHAIN_CLOCK_UTC is not a valid ISO 8601 instant.");
             return 2;
diff --git a/src/Alveara.Api.Tests/ChainHostClockParseTests.cs b/src/Alveara.Api.Tests/ChainHostClockParseTests.cs
new file mode 100644
index 0000000..95aace3
--- /dev/null
+++ b/src/Alveara.Api.Tests/ChainHostClockParseTests.cs
@@ -0,0 +1,70 @@
+using System.Globalization;
+using Alveara.Api.ChainHost;
+using Xunit;
+
+namespace Alveara.Api.Tests;
+
+/// <summary>
+/// ALV-N021 A2-R02 (review CR-1, decision 02 T-2): the chain host's documented ISO 8601 <c>CHAIN_CLOCK_UTC</c> is read the same on every machine culture - a Thai (Buddhist
+/// calendar) machine, a machine whose time separator is "." and the invariant culture all accept the same text as the same UTC instant, and refuse the same bad text.
+/// The source guard that keeps this parse on the invariant culture is in <see cref="TimeCultureGuardTests"/>.
+/// </summary>
+[Collection(CultureSensitiveTests.Name)]   // changes the thread culture: never beside another such test
+public class ChainHostClockParseTests
+{
+    private static CultureInfo Culture(string name) => name switch
+    {
+        "th-TH" => CultureInfo.GetCultureInfo("th-TH"),
+        "dotted-time" => DottedTime(),
+        _ => CultureInfo.InvariantCulture,
+    };
+
+    private static CultureInfo DottedTime()
+    {
+        var c = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
+        c.DateTimeFormat.TimeSeparator = ".";
+        return c;
+    }
+
+    private static (bool Ok, DateTimeOffset Utc) ParseUnder(string culture, string? value)
+    {
+        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
+        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture(culture);
+        try { return (ChainHostMain.TryParseClockUtc(value, out var utc), utc); }
+        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved; }
+    }
+
+    public static TheoryData<string> Cultures => new() { "th-TH", "dotted-time", "invariant" };
+
+    private static readonly DateTimeOffset Expected = new(2030, 1, 14, 13, 30, 0, TimeSpan.Zero);
+
+    [Theory]
+    [MemberData(nameof(Cultures))]
+    public void The_same_iso_instant_is_accepted_as_the_same_utc_value_on_every_machine_culture(string culture)
+    {
+        foreach (var text in new[] { "2030-01-14T13:30:00Z", "2030-01-14T07:30:00-06:00", "2030-01-14T13:30:00" })   // Z, an offset, and no offset (read as UTC)
+        {
+            var (ok, utc) = ParseUnder(culture, text);
+            Assert.True(ok, $"{text} under {culture}");
+            Assert.Equal(Expected, utc);
+            Assert.Equal(TimeSpan.Zero, utc.Offset);
+        }
+    }
+
+    [Theory]
+    [MemberData(nameof(Cultures))]
+    public void The_documented_default_is_accepted_on_every_machine_culture(string culture)
+    {
+        var (ok, utc) = ParseUnder(culture, ChainHostMain.DefaultClockUtc);
+        Assert.True(ok);
+        Assert.Equal(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero), utc);
+    }
+
+    [Theory]
+    [MemberData(nameof(Cultures))]
+    public void Text_that_is_not_an_instant_is_refused_on_every_machine_culture(string culture)
+    {
+        foreach (var text in new[] { "not a time", "", null, "2030-13-40T25:61:00Z" })
+            Assert.False(ParseUnder(culture, text).Ok, $"'{text}' under {culture}");
+    }
+}
diff --git a/src/Alveara.Api.Tests/TimeCultureGuardTests.cs b/src/Alveara.Api.Tests/TimeCultureGuardTests.cs
index c6a5593..53b4f07 100644
--- a/src/Alveara.Api.Tests/TimeCultureGuardTests.cs
+++ b/src/Alveara.Api.Tests/TimeCultureGuardTests.cs
@@ -19,24 +19,29 @@ namespace Alveara.Api.Tests;
 [Collection(CultureSensitiveTests.Name)]
 public class TimeCultureGuardTests
 {
-    private static readonly string ApiRoot = FindApiRoot();
+    private static readonly string SrcRoot = FindSrcRoot();
 
-    private static string FindApiRoot()
+    private static string FindSrcRoot()
     {
         var dir = new DirectoryInfo(AppContext.BaseDirectory);
         while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Alveara.Api", "Alveara.Api.csproj"))) dir = dir.Parent;
-        return dir is null ? throw new InvalidOperationException("The repository root was not found from the test output folder.") : Path.Combine(dir.FullName, "src", "Alveara.Api");
+        return dir is null ? throw new InvalidOperationException("The repository root was not found from the test output folder.") : Path.Combine(dir.FullName, "src");
     }
 
+    /// <summary>The projects the guard scans: the API and (ALV-N021 A2-R02, review CR-1) the chain host, which reads machine values too.</summary>
+    private static readonly string[] ScannedProjects = ["Alveara.Api", "Alveara.Api.ChainHost"];
+
     private static IEnumerable<(string Path, int Line, string Text)> SourceLines() =>
-        Directory.EnumerateFiles(ApiRoot, "*.cs", SearchOption.AllDirectories)
+        ScannedProjects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(SrcRoot, p), "*.cs", SearchOption.AllDirectories))
             .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
-            .SelectMany(f => File.ReadAllLines(f).Select((t, i) => (Path.GetRelativePath(ApiRoot, f).Replace('\\', '/'), i + 1, t)));
+            .SelectMany(f => File.ReadAllLines(f).Select((t, i) => (Path.GetRelativePath(SrcRoot, f).Replace('\\', '/'), i + 1, t)));
 
     // a fixed date/time pattern: contains a year, hour, minute, month or day token (not "N" for a Guid, not the culture-free round-trip "O"/"o")
     private const string DatePattern = @"[^""]*(yyyy|HH|mm|MM|dd)[^""]*";
     private static readonly Regex FixedFormatToString = new(@"\.ToString\(""" + DatePattern + @"""(\)|,)", RegexOptions.Compiled);
     private static readonly Regex DateParseExact = new(@"\b(DateOnly|DateTime|DateTimeOffset|TimeOnly)\.(Try)?ParseExact\(", RegexOptions.Compiled);
+    // ALV-N021 A2-R02 (review CR-1): a non-exact date parse reads with the process culture unless it names one, so it must name the invariant culture too
+    private static readonly Regex DateParse = new(@"\b(DateOnly|DateTime|DateTimeOffset|TimeOnly)\.(Try)?Parse\(", RegexOptions.Compiled);
     private static readonly Regex InterpolatedDateHole = new(@"\$@?""[^""]*\{[^{}]*:(yyyy|HH|MM|dd)[^{}]*\}", RegexOptions.Compiled);
     private static readonly Regex NamesInvariant = new(@"CultureInfo\.InvariantCulture|,\s*Inv\)", RegexOptions.Compiled);
 
@@ -47,7 +52,7 @@ public class TimeCultureGuardTests
     private static readonly (string Path, string Reason)[] LocalizedDisplayAllowlist = [];
 
     internal static bool IsOffending(string line) =>
-        (FixedFormatToString.IsMatch(line) || DateParseExact.IsMatch(line) || InterpolatedDateHole.IsMatch(line)) && !NamesInvariant.IsMatch(line);
+        (FixedFormatToString.IsMatch(line) || DateParseExact.IsMatch(line) || DateParse.IsMatch(line) || InterpolatedDateHole.IsMatch(line)) && !NamesInvariant.IsMatch(line);
 
     [Fact]
     public void Every_fixed_format_date_or_time_in_the_api_names_the_invariant_culture()
@@ -63,6 +68,7 @@ public class TimeCultureGuardTests
         var lines = SourceLines().ToList();
         Assert.True(lines.Select(l => l.Path).Distinct().Count() > 100, "the guard found too few source files");
         Assert.True(lines.Count(l => FixedFormatToString.IsMatch(l.Text) || DateParseExact.IsMatch(l.Text) || InterpolatedDateHole.IsMatch(l.Text)) >= 40, "the guard no longer sees the date sites it protects");
+        Assert.Contains(lines, l => l.Path == "Alveara.Api.ChainHost/ChainHostMain.cs" && DateParse.IsMatch(l.Text));   // ALV-N021 A2-R02: the chain host is scanned and its clock parse is seen
     }
 
     [Theory]
@@ -70,6 +76,9 @@ public class TimeCultureGuardTests
     [InlineData(@"var s = a.StartLocal.ToString(""HH:mm"");")]
     [InlineData(@"var d = DateOnly.ParseExact(payload.DueDate, ""yyyy-MM-dd"");")]
     [InlineData(@"var k = $""recall:{id:N}:{due:yyyyMMdd}"";")]
+    [InlineData(@"if (!DateTimeOffset.TryParse(Environment.GetEnvironmentVariable(""CHAIN_CLOCK_UTC"") ?? DefaultClockUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var start))")]   // the A2-R01 line (review CR-1)
+    [InlineData(@"var d = DateTime.Parse(text);")]
+    [InlineData(@"var ok = DateOnly.TryParse(text, out var d);")]
     public void Negative_control_the_guard_flags_a_fixed_format_without_the_invariant_culture(string line) => Assert.True(IsOffending(line));
 
     [Theory]
@@ -77,6 +86,7 @@ public class TimeCultureGuardTests
     [InlineData(@"var k = string.Create(CultureInfo.InvariantCulture, $""recall:{id:N}:{due:yyyyMMdd}"");")]
     [InlineData(@"var s = h.ChangedAtUtc.ToString(""O"");")]
     [InlineData(@"var s = id.ToString(""N"");")]
+    [InlineData(@"var ok = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed);")]
     public void The_guard_accepts_an_explicit_invariant_culture_and_culture_free_formats(string line) => Assert.False(IsOffending(line));
 
     // ---------- behaviour under other machine cultures ----------
```
