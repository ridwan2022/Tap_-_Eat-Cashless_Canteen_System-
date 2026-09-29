using System.ComponentModel.DataAnnotations;

namespace TapAndEat.Api.DTOs;

public record CreateUserByAdminRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required] string Role);

public record AssignRoleRequest([Required] string Role);

public record SetActiveRequest([Required] bool IsActive);
