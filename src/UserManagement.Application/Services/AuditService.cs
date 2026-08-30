using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;
using UserManagement.Application.Mapping;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.Services;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AuditService(IAuditLogRepository repository, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task LogAsync(AuditAction action, AuditContext audit, Guid? userId = null, string? userName = null,
        string? entityName = null, string? entityId = null, string? oldValues = null, string? newValues = null,
        bool succeeded = true, string? message = null, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            UserId = userId,
            UserName = userName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = Truncate(audit.IpAddress, 64),
            UserAgent = Truncate(audit.UserAgent, 512),
            Succeeded = succeeded,
            Message = Truncate(message, 1024),
            Timestamp = _clock.UtcNow
        };

        await _repository.AddAsync(log, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQueryRequest request, CancellationToken ct = default)
    {
        var (items, total, filtered) = await _repository.SearchAsync(
            request.Search, request.UserId, request.Action, request.From, request.To,
            request.SortBy, request.SortDescending, request.Skip, request.PageSize, ct);

        return PagedResult<AuditLogDto>.Create(items.Select(x => x.ToDto()).ToList(), request, total, filtered);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
