using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 2 — CRUD فروشنده و پیشنهاد فروش (Listing).
/// قواعد کسب‌وکار اینجا نگه داشته می‌شوند تا Controller لاغر بماند.
/// </summary>
public sealed class SellerService : ISellerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<SellerService> _logger;

    public SellerService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<SellerService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    #region Seller

    public async Task<SellerDto> CreateAsync(CreateSellerDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.StoreName))
            throw new BusinessException("نام فروشگاه الزامی است.", "SELLER_STORE_NAME_REQUIRED");

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(dto.UserId, cancellationToken);
        if (user == null)
            throw new BusinessException("کاربر یافت نشد.", "USER_NOT_FOUND");

        // UserId یکتاست ⇒ رابطه ۱:۱ (Design Spec §۵.۱)
        var alreadySeller = await _unitOfWork.Repository<Seller>()
            .AnyAsync(s => s.UserId == dto.UserId, cancellationToken);
        if (alreadySeller)
            throw new BusinessException("این کاربر از قبل یک فروشگاه دارد.", "SELLER_ALREADY_EXISTS");

        var nameTaken = await _unitOfWork.Repository<Seller>()
            .AnyAsync(s => s.StoreName == dto.StoreName, cancellationToken);
        if (nameTaken)
            throw new BusinessException("نام فروشگاه تکراری است.", "SELLER_STORE_NAME_DUPLICATE");

        if (!string.IsNullOrWhiteSpace(dto.Slug))
        {
            var slugTaken = await _unitOfWork.Repository<Seller>()
                .AnyAsync(s => s.Slug == dto.Slug, cancellationToken);
            if (slugTaken)
                throw new BusinessException("شناسه URL فروشگاه تکراری است.", "SELLER_SLUG_DUPLICATE");
        }

        var entity = _mapper.Map<Seller>(dto);

        // فروشندهٔ تازه هرگز Approved شروع نمی‌کند.
        entity.Status = SellerStatus.Pending;
        entity.IsActive = true;

        await _unitOfWork.Repository<Seller>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerId} created for user {UserId}", entity.SellerId, entity.UserId);

        return _mapper.Map<SellerDto>(entity);
    }

    /// <summary>
    /// Phase 7: میانگین امتیاز را فقط از نظرات تأییدشده و در یک کوئری محاسبه می‌کند،
    /// سپس روی DTOها اعمال می‌کند. ستون ذخیره‌شده نداریم تا داده تکراری و کهنه نشود.
    /// </summary>
    /// <summary>
    /// Phase 8 (R36): پروفایل فروشگاهِ کاربر جاری.
    ///
    /// بدون این متد، فروشنده باید sellerId خودش را حدس بزند و همان کلاس
    /// اشتباه IDOR (فاز ۴) دوباره باز می‌شد.
    /// </summary>
    public async Task<SellerDto?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var seller = await _unitOfWork.Repository<Seller>()
            .FirstOrDefaultAsync(new Spec<Seller>().Where(s => s.UserId == userId), cancellationToken);

        if (seller is null)
            return null;

        var dto = _mapper.Map<SellerDto>(seller);
        return dto;
    }

    public async Task<SellerDto> GetByIdAsync(int sellerId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
        if (entity == null)
            throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

        var dto = _mapper.Map<SellerDto>(entity);

        return dto;
    }

    public async Task<IReadOnlyList<SellerDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _unitOfWork.Repository<Seller>()
            .ListAsync(new Spec<Seller>(), cancellationToken);

        // Phase 9 (R33): امتیاز از ستون کش‌شدهٔ Seller خوانده می‌شود؛ کوئری اضافی لازم نیست.
        var dtos = rows.Select(s => _mapper.Map<SellerDto>(s)).ToList();

        return dtos;
    }

    public async Task<SellerDto> UpdateAsync(int sellerId, UpdateSellerDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
        if (entity == null)
            throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

        var nameTaken = await _unitOfWork.Repository<Seller>()
            .AnyAsync(s => s.StoreName == dto.StoreName && s.SellerId != sellerId, cancellationToken);
        if (nameTaken)
            throw new BusinessException("نام فروشگاه تکراری است.", "SELLER_STORE_NAME_DUPLICATE");

        if (!string.IsNullOrWhiteSpace(dto.Slug))
        {
            var slugTaken = await _unitOfWork.Repository<Seller>()
                .AnyAsync(s => s.Slug == dto.Slug && s.SellerId != sellerId, cancellationToken);
            if (slugTaken)
                throw new BusinessException("شناسه URL فروشگاه تکراری است.", "SELLER_SLUG_DUPLICATE");
        }

        _mapper.Map(dto, entity);

        _unitOfWork.Repository<Seller>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerId} updated to status {Status}", sellerId, entity.Status);

        return _mapper.Map<SellerDto>(entity);
    }

    public async Task DeleteAsync(int sellerId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Seller>().GetByIdAsync(sellerId, cancellationToken);
        if (entity == null)
            throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

        // حذف فروشنده‌ای که Listing یا انبار دارد، داده‌های وابسته را از بین می‌برد.
        var hasListings = await _unitOfWork.Repository<SellerProduct>()
            .AnyAsync(l => l.SellerId == sellerId, cancellationToken);
        var hasWarehouses = await _unitOfWork.Repository<Warehouse>()
            .AnyAsync(w => w.SellerId == sellerId, cancellationToken);

        if (hasListings || hasWarehouses)
            throw new BusinessException(
                "این فروشنده پیشنهاد فروش یا انبار دارد و قابل حذف نیست؛ ابتدا آن‌ها را غیرفعال کنید.",
                "SELLER_HAS_DEPENDENCIES");

        _unitOfWork.Repository<Seller>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seller {SellerId} deleted", sellerId);
    }

    #endregion

    #region Listing

    public async Task<SellerProductDto> CreateListingAsync(CreateSellerProductDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Price <= 0)
            throw new BusinessException("قیمت باید بزرگ‌تر از صفر باشد.", "INVALID_PRICE");

        var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(dto.SellerId, cancellationToken);
        if (seller == null)
            throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");

        if (seller.Status != SellerStatus.Approved || !seller.IsActive)
            throw new BusinessException("فروشنده تأیید نشده است و نمی‌تواند پیشنهاد فروش بسازد.", "SELLER_NOT_APPROVED");

        // Phase 6: پیشنهاد فروش روی یک واریانت مشخص ساخته می‌شود، نه روی محصول کلی.
        var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(dto.VariantId, cancellationToken);
        if (variant == null)
            throw new BusinessException("واریانت محصول یافت نشد.", "VARIANT_NOT_FOUND");

        if (!variant.IsActive)
            throw new BusinessException("این واریانت غیرفعال است و قابل عرضه نیست.", "VARIANT_INACTIVE");

        var duplicate = await _unitOfWork.Repository<SellerProduct>()
            .AnyAsync(l => l.SellerId == dto.SellerId && l.VariantId == dto.VariantId, cancellationToken);
        if (duplicate)
            throw new BusinessException(
                "این فروشنده از قبل برای این واریانت پیشنهاد فروش دارد.", "LISTING_ALREADY_EXISTS");

        var entity = _mapper.Map<SellerProduct>(dto);

        // ناوبری را دستی پر می‌کنیم تا نگاشت خروجی به ProductId دسترسی داشته باشد.
        entity.Variant = variant;

        await _unitOfWork.Repository<SellerProduct>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Listing {ListingId} created for seller {SellerId}, variant {VariantId}, product {ProductId}",
            entity.ListingId, entity.SellerId, variant.VariantId, variant.ProductId);

        return _mapper.Map<SellerProductDto>(entity);
    }

    public async Task<SellerProductDto> GetListingAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<SellerProduct>().GetByIdAsync(listingId, cancellationToken);
        if (entity == null)
            throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

        return _mapper.Map<SellerProductDto>(entity);
    }

    public async Task<IReadOnlyList<SellerProductDto>> GetListingsAsync(int? sellerId = null, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<SellerProduct>();
        if (sellerId.HasValue)
            spec = spec.Where(l => l.SellerId == sellerId.Value);

        var rows = await _unitOfWork.Repository<SellerProduct>().ListAsync(spec, cancellationToken);

        return rows.Select(l => _mapper.Map<SellerProductDto>(l)).ToList();
    }

    public async Task<SellerProductDto> UpdateListingAsync(int listingId, UpdateSellerProductDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Price <= 0)
            throw new BusinessException("قیمت باید بزرگ‌تر از صفر باشد.", "INVALID_PRICE");

        var entity = await _unitOfWork.Repository<SellerProduct>().GetByIdAsync(listingId, cancellationToken);
        if (entity == null)
            throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

        _mapper.Map(dto, entity);

        _unitOfWork.Repository<SellerProduct>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Listing {ListingId} updated", listingId);

        return _mapper.Map<SellerProductDto>(entity);
    }

    #endregion
}