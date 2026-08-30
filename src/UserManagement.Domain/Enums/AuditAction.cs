namespace UserManagement.Domain.Enums;

public enum AuditAction
{
    LoginSucceeded = 1,
    LoginFailed = 2,
    Logout = 3,
    Register = 4,
    TokenRefreshed = 5,
    TokenRevoked = 6,
    PasswordChanged = 7,
    PasswordResetRequested = 8,
    PasswordResetCompleted = 9,
    UserCreated = 10,
    UserUpdated = 11,
    UserDeleted = 12,
    UserActivated = 13,
    UserDeactivated = 14,
    RolesAssigned = 15,
    RoleCreated = 16,
    RoleUpdated = 17,
    RoleDeleted = 18,
    ProfileUpdated = 19
}
