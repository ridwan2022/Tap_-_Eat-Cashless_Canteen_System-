namespace TapAndEat.Tests.Framework;

public class AssertionFailedException : Exception
{
    public AssertionFailedException(string message) : base(message) { }
}

/// <summary>Small xUnit-style assertion helpers so test bodies read the same as they would with [Fact]/Assert.*.</summary>
public static class Assert
{
    public static void True(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition) throw new AssertionFailedException(message);
    }

    public static void False(bool condition, string message = "Expected condition to be false.")
    {
        if (condition) throw new AssertionFailedException(message);
    }

    public static void Equal<T>(T expected, T actual, string? context = null)
    {
        if (!Equals(expected, actual))
        {
            var prefix = context is null ? string.Empty : $"[{context}] ";
            throw new AssertionFailedException($"{prefix}Expected <{expected}> but got <{actual}>.");
        }
    }

    public static void NotNull(object? value, string message = "Expected a non-null value.")
    {
        if (value is null) throw new AssertionFailedException(message);
    }

    public static void Null(object? value, string message = "Expected a null value.")
    {
        if (value is not null) throw new AssertionFailedException(message);
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionFailedException(
                $"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
        }
        throw new AssertionFailedException($"Expected {typeof(TException).Name} but no exception was thrown.");
    }
}
