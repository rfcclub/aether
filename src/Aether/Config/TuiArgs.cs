namespace Aether.Config;

public static class TuiArgs
{
    public static string? ParseAgentName(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--agent", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i], "-a", StringComparison.OrdinalIgnoreCase))
            {
                var val = args[i + 1];
                if (!string.IsNullOrEmpty(val) && !val.StartsWith("-"))
                    return val;
            }
        }
        return null;
    }
}
