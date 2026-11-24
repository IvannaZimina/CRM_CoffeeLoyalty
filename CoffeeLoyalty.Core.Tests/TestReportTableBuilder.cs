using System.Text;

namespace CoffeeLoyalty.Core.Tests;

// Formats ASCII summary tables (Check | Value | Status) for xUnit test console output.
internal static class TestReportTableBuilder
{
    internal static string Build(
        string title,
        IEnumerable<(string Check, string Value, string Status)> rows)
    {
        List<(string Check, string Value, string Status)> list = rows.ToList();

        int col1 = Math.Max(
            "Check".Length,
            list.Count == 0 ? 0 : list.Max(row => row.Check.Length));

        int col2 = Math.Max(
            "Value".Length,
            list.Count == 0 ? 0 : list.Max(row => row.Value.Length));

        int col3 = Math.Max(
            "Status".Length,
            list.Count == 0 ? 0 : list.Max(row => row.Status.Length));

        string border =
            $"+-{new string('-', col1)}-+-{new string('-', col2)}-+-{new string('-', col3)}-+";

        StringBuilder sb = new();

        sb.AppendLine();
        sb.AppendLine($"=== {title} ===");
        sb.AppendLine(border);
        sb.AppendLine(
            $"| {"Check".PadRight(col1)} | {"Value".PadRight(col2)} | {"Status".PadRight(col3)} |");
        sb.AppendLine(border);

        foreach ((string check, string value, string status) in list)
        {
            sb.AppendLine(
                $"| {check.PadRight(col1)} | {value.PadRight(col2)} | {status.PadRight(col3)} |");
        }

        sb.AppendLine(border);

        return sb.ToString();
    }
}

// Run the tests that use this table builder from the solution directory:
// dotnet test --filter "FullyQualifiedName~CoffeeLoyalty.Core.Tests.DrinkServiceTests"