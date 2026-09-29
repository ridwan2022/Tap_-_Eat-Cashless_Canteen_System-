using Microsoft.AspNetCore.Mvc;

namespace TapAndEat.Api.Infrastructure;

public enum ErrorKind
{
    Invalid,
    NotFound,
    Conflict,
    Forbidden
}

/// <summary>
/// Base for the Sprint 2 domain exceptions. Sprint 1 threw one exception type
/// per service and let each controller map it to a fixed status code; Sprint 2
/// carries a <see cref="ErrorKind"/> so a service can say "not found" or
/// "conflict" and every controller maps it the same way.
/// </summary>
public abstract class AppException : Exception
{
    public ErrorKind Kind { get; }

    protected AppException(ErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }
}

public static class AppExceptionExtensions
{
    public static ActionResult ToActionResult(this AppException ex)
    {
        var body = new { message = ex.Message };
        return ex.Kind switch
        {
            ErrorKind.NotFound => new NotFoundObjectResult(body),
            ErrorKind.Conflict => new ConflictObjectResult(body),
            ErrorKind.Forbidden => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
            _ => new BadRequestObjectResult(body)
        };
    }
}
