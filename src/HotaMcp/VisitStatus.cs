namespace HotaMcp;

internal static class VisitStatus
{
    // A rule saying an object is one-time does not say whether this hero has visited it.
    public static string FromHint(string? hint)
    {
        if(hint?.Contains("(Не посещено)",StringComparison.OrdinalIgnoreCase)==true)return "not_visited";
        if(hint?.Contains("(Посещено)",StringComparison.OrdinalIgnoreCase)==true)return "visited";
        return "unknown";
    }
}
