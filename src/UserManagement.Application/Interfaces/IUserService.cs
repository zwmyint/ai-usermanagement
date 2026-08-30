using UserManagement.Application.DTOs;

namespace UserManagement.Application.Interfaces;

public interface IUserService
{
    Task<PagedResult<UserDto>> GetUsersAsync(UserQueryRequest request, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserDto dto, AuditContext audit, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserDto dto, AuditContext audit, CancellationToken ct = default);
    Task<UserDto> UpdateProfilePictureAsync(Guid id, Stream image, string fileName, AuditContext audit, CancellationToken ct = default);
    Task DeleteAsync(Guid id, AuditContext audit, CancellationToken ct = default);
    Task<UserDto> SetActiveAsync(Guid id, bool isActive, AuditContext audit, CancellationToken ct = default);
    Task<UserDto> AssignRolesAsync(Guid id, AssignRolesDto dto, AuditContext audit, CancellationToken ct = default);

    Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, AuditContext audit, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, AuditContext audit, CancellationToken ct = default);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default);
    Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleDto dto, AuditContext audit, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, UpdateRoleDto dto, AuditContext audit, CancellationToken ct = default);
    Task DeleteAsync(Guid id, AuditContext audit, CancellationToken ct = default);
}

public interface IAuthService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, AuditContext audit, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, AuditContext audit, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, AuditContext audit, CancellationToken ct = default);
    Task RevokeAsync(RevokeRequest request, AuditContext audit, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, AuditContext audit, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordDto dto, AuditContext audit, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordDto dto, AuditContext audit, CancellationToken ct = default);
}

public interface IAuditService
{
    Task LogAsync(Domain.Enums.AuditAction action, AuditContext audit, Guid? userId = null, string? userName = null,
        string? entityName = null, string? entityId = null, string? oldValues = null, string? newValues = null,
        bool succeeded = true, string? message = null, CancellationToken ct = default);

    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQueryRequest request, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, bool isAdmin, CancellationToken ct = default);
}
