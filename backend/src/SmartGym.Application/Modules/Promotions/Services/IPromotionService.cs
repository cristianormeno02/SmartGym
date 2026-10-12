using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Services;

public interface IPromotionService
{
    Task<List<PromotionDto>> GetAllAsync(bool onlyActive = false, PromotionStatus? status = null, CancellationToken cancellationToken = default);
    Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionDto> PauseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionDto> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluación previa de un código para la contratación de un plan: no registra canjes ni modifica contadores.
    /// Un alumno sólo puede evaluar para sí mismo.
    /// </summary>
    Task<PromotionApplicationResultDto> PreviewForMembershipAsync(EvaluatePromotionRequest request, CancellationToken cancellationToken = default);

    Task<PromotionApplicationResultDto> EvaluatePromotionAsync(ApplyPromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionRedemptionDto> RedeemPromotionAsync(ApplyPromotionRequest request, Guid? membershipId, CancellationToken cancellationToken = default);
    Task<PromotionApplicationResultDto> EvaluatePromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        CancellationToken cancellationToken = default);
    Task<PromotionRedemptionDto> RedeemPromotionForReservationAsync(
        Guid studentId,
        Guid classSessionId,
        Guid activityId,
        string code,
        Guid reservationId,
        CancellationToken cancellationToken = default);
    Task AttachRedemptionToMembershipAsync(Guid redemptionId, Guid membershipId, CancellationToken cancellationToken = default);
    Task VoidRedemptionAsync(Guid redemptionId, CancellationToken cancellationToken = default);

    /// <summary>Canjes vigentes de una persona. Sólo el personal o el propio titular pueden consultarlos.</summary>
    Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>Canjes de una promoción, más recientes primero, incluidos los anulados.</summary>
    Task<PagedResult<PromotionRedemptionDto>> GetRedemptionsByPromotionAsync(Guid promotionId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
}
