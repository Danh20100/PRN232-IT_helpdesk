namespace Helpdesk.Priority;

public static class PriorityRules
{
    public static (string Priority, int SlaHours) Suggest(string? category)
    {
        var n = category?.ToLowerInvariant() ?? "";
        if (n.Contains("network") || n.Contains("security")) return ("High", 8);
        if (n.Contains("hardware") || n.Contains("printer")) return ("Medium", 24);
        return ("Low", 72);
    }
}
