namespace Aether.Config;

public static class TuiArgs
{
    public static string? ParseAgentName(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--agent" || args[i] == "-a")
            {
                var val = args[i + 1];
                if (!string.IsNullOrEmpty(val) && !val.StartsWith("-"))
                    return val;
            }
        }
        return null;
    }
}
