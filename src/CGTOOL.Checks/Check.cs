namespace CGTOOL.Checks;

/// <summary>Counts what failed and says what it was. No framework, so this is the framework.</summary>
public static class Check
{
    private static int _failed;
    private static string _section = string.Empty;

    public static void Section(string name)
    {
        _section = name;
        Console.WriteLine();
        Console.WriteLine(name);
    }

    public static void That(string what, bool ok)
    {
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    }

    public static int Report()
    {
        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "All consistency checks passed."
            : $"{_failed} CHECK(S) FAILED. The sections above say which.");
        return _failed == 0 ? 0 : 1;
    }
}
