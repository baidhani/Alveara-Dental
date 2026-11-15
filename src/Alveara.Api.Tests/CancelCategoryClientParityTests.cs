using System.Text.RegularExpressions;
using Alveara.Api.Architecture.Scheduling;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-19): the calendar's cancel form offers the categories from a fixed client list (<c>CANCEL_CATEGORIES</c> in schedulingApi.ts). This reads that list from the
/// client source and compares its codes, in order, with <see cref="AppointmentCancelCategories.All"/>, so adding or renaming a category on one side fails here. No database.
/// </summary>
public class CancelCategoryClientParityTests
{
    [Fact]
    public void The_client_cancel_categories_are_exactly_the_servers_in_the_same_order()
    {
        var source = File.ReadAllText(ClientFile("src/alveara-client/src/services/schedulingApi.ts"));
        var block = Regex.Match(source, @"export const CANCEL_CATEGORIES = \[(?<body>.*?)\] as const;", RegexOptions.Singleline);
        Assert.True(block.Success, "CANCEL_CATEGORIES not found in schedulingApi.ts");

        var codes = Regex.Matches(block.Groups["body"].Value, @"code:\s*""(?<c>[a-z_]+)""").Select(m => m.Groups["c"].Value).ToList();

        Assert.Equal(AppointmentCancelCategories.All, codes);
    }

    [Fact]
    public void The_client_checkout_rules_match_the_servers_and_every_recordable_outcome_has_a_label()
    {
        var source = File.ReadAllText(ClientFile("src/alveara-client/src/services/checkoutApi.ts"));
        IReadOnlyList<string> SetOf(string name)
        {
            var m = Regex.Match(source, $@"export const {name} = new Set\(\[(?<body>[^\]]*)\]\);");
            Assert.True(m.Success, $"{name} not found in checkoutApi.ts");
            return Regex.Matches(m.Groups["body"].Value, @"""(?<c>[a-z_]+)""").Select(x => x.Groups["c"].Value).Order(StringComparer.Ordinal).ToList();
        }

        Assert.Equal(Architecture.Scheduling.CheckoutOutcomes.NeedReason.Order(StringComparer.Ordinal), SetOf("NEEDS_REASON"));
        Assert.Equal(Architecture.Scheduling.CheckoutOutcomes.NeedLedgerEntry.Order(StringComparer.Ordinal), SetOf("NEEDS_LEDGER_ENTRY"));
        var labels = Regex.Match(source, @"export const CHECKOUT_OUTCOME_LABELS: Record<string, string> = \{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.True(labels.Success, "CHECKOUT_OUTCOME_LABELS not found in checkoutApi.ts");
        foreach (var outcome in Architecture.Scheduling.CheckoutOutcomes.ByItem.Values.SelectMany(v => v))
            Assert.Matches($@"\b{outcome}:", labels.Groups["body"].Value);
    }

    /// <summary>Walks up from the test binaries to the repository root (the folder that holds src/alveara-client).</summary>
    private static string ClientFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}
