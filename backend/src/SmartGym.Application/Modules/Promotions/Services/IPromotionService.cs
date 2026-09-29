using SmartGym.Application.Modules.Promotions.Dtos;

namespace SmartGym.Application.Modules.Promotions.Services;

public interface IPromotionService
{
    Task<List<PromotionDto>> GetAllAsync(bool onlyActive = false, CancellationToken cancellationToken = default);
    Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PromotionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionApplicationResultDto> EvaluatePromotionAsync(ApplyPromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionRedemptionDto> RedeemPromotionAsync(ApplyPromotionRequest request, Guid? membershipId, CancellationToken cancellationToken = default);
    Task<List<PromotionRedemptionDto>> GetRedemptionsByPersonAsync(Guid personId, CancellationToken cancellationToken = default);
}
