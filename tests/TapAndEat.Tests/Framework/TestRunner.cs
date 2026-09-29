namespace TapAndEat.Tests.Framework;

public class TestRunner
{
    private readonly List<(string Suite, string Name, Func<Task> Body)> _tests = new();

    public void Add(string suite, string name, Func<Task> body) => _tests.Add((suite, name, body));

    /// <summary>Runs every registered test, prints PASS/FAIL per test, and returns the process exit code.</summary>
    public async Task<int> RunAllAsync()
    {
        var passed = 0;
        var failed = 0;
        string? currentSuite = null;

        foreach (var (suite, name, body) in _tests)
        {
            if (suite != currentSuite)
            {
                Console.WriteLine();
                Console.WriteLine($"== {suite} ==");
                currentSuite = suite;
            }

            try
            {
                await body();
                Console.WriteLine($"  PASS  {name}");
                passed++;
            }
            catch (AssertionFailedException ex)
            {
                Console.WriteLine($"  FAIL  {name}");
                Console.WriteLine($"        {ex.Message}");
                failed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR {name}");
                Console.WriteLine($"        Unexpected exception: {ex}");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{passed + failed} tests passed.");
        return failed == 0 ? 0 : 1;
    }
}
