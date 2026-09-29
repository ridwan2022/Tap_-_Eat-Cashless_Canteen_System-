namespace TapAndEat.Api.Models;

/// <summary>
/// Roles supported in Sprint 1. Matches FR 1.x / FR 12.x role-based access
/// control requirements. Additional roles (e.g. ITSupport) can be added here
/// in later sprints without changing the auth pipeline.
/// </summary>
public enum UserRole
{
    Customer = 0,
    KitchenStaff = 1,
    Admin = 2
}
