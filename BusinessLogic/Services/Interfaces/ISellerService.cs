using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Seller;

namespace BusinessLogic.Services.Interfaces
{
    public interface ISellerService
    {
        Task<SellerDto> CreateAsync(CreateSellerDto dto, CancellationToken cancellationToken = default);
        Task<SellerDto> GetByIdAsync(int sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 8 (R36): پروفایل فروشگاهِ کاربر جاری.
    /// اگر کاربر فروشگاه نداشته باشد null برمی‌گردد (کنترلر 404 می‌دهد).
    /// </summary>
    Task<SellerDto?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SellerDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<SellerDto> UpdateAsync(int sellerId, UpdateSellerDto dto, CancellationToken cancellationToken = default);
        Task DeleteAsync(int sellerId, CancellationToken cancellationToken = default);

        Task<SellerProductDto> CreateListingAsync(CreateSellerProductDto dto, CancellationToken cancellationToken = default);
        Task<SellerProductDto> GetListingAsync(int listingId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SellerProductDto>> GetListingsAsync(int? sellerId = null, CancellationToken cancellationToken = default);
        Task<SellerProductDto> UpdateListingAsync(int listingId, UpdateSellerProductDto dto, CancellationToken cancellationToken = default);

        // M7.1: Listing CRUD completion
        Task<bool> DeleteListingAsync(int sellerId, int listingId, CancellationToken cancellationToken = default);
        Task<SellerProductDto?> UpdateListingPriceAsync(int sellerId, int listingId, decimal newPrice, CancellationToken cancellationToken = default);
    }
}