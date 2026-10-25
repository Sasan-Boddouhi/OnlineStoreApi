using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// پیاده‌سازی موجودی روی موجودیت Inventory (کلید: ListingId + WarehouseId).
/// Phase 2 (D1): موجودی به پیشنهاد فروش (SellerProduct) تعلق دارد، نه به محصول.
/// سیاست Phase 0: تخصیص Greedy از بزرگ‌ترین انبار. منطق پیشرفته (نزدیک‌ترین انبار، FEFO) به فاز ۴ موکول شده است.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryService> _logger;
    private readonly InventorySettings _settings;
    private readonly ICurrentUserService _currentUser;

    public InventoryService(
        IUnitOfWork unitOfWork,
        IOptions<InventorySettings> settings,
        ICurrentUserService currentUser,
        ILogger<InventoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _settings = settings.Value;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<decimal> GetAvailableAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var rows = await GetActiveRowsAsync(listingId, cancellationToken);
        return rows.Sum(r => r.Quantity);
    }

    public async Task ReserveAsync(int listingId, decimal quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new BusinessException("مقدار درخواستی باید بزرگ‌تر از صفر باشد.", "INVALID_QUANTITY");

        var rows = await GetActiveRowsAsync(listingId, cancellationToken);

        // تصمیم ناظر (گزینهٔ C): در بازهٔ مهاجرت، محصول بدون هیچ ردیف موجودی «نامحدود» تلقی می‌شود.
        if (rows.Count == 0 && !_settings.EnforceStock)
        {
            _logger.LogWarning(
                "Stock enforcement disabled and listing {ListingId} has no inventory rows; treating as unlimited.",
                listingId);
            return;
        }

        var available = rows.Sum(r => r.Quantity);

        if (available < quantity)
        {
            _logger.LogWarning(
                "Insufficient stock for listing {ListingId}. Requested={Requested}, Available={Available}",
                listingId, quantity, available);
            throw new BusinessException("موجودی کافی نیست.", "INSUFFICIENT_STOCK");
        }

        var remaining = quantity;
        foreach (var row in rows.OrderByDescending(r => r.Quantity))
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(row.Quantity, remaining);
            if (take <= 0)
                continue;

            row.Decrease(take);
            remaining -= take;
        }
    }

    public async Task ReleaseAsync(int listingId, decimal quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return;

        var rows = await GetActiveRowsAsync(listingId, cancellationToken);

        if (rows.Count == 0)
        {
            // محدودیت شناخته‌شده: بدون دفتر رزرو، انبار دقیقِ کسر‌شده قابل بازسازی نیست.
            _logger.LogWarning(
                "Release requested for listing {ListingId} but no active inventory row exists. Quantity={Quantity} was not restored.",
                listingId, quantity);
            return;
        }

        // بازگرداندن به بزرگ‌ترین انبار — مجموع موجودی درست بازمی‌گردد، هرچند توزیع بین انبارها تقریبی است.
        var target = rows.OrderByDescending(r => r.Quantity).First();
        target.Increase(quantity);
    }

    // ==================== Phase 10 (R49) — نوشتن موجودی ====================

    /// <summary>
    /// افزودن موجودی به یک Listing.
    ///
    /// سه تصمیم که هر کدام یک دام واقعی را می‌بندد:
    ///
    /// 1) مالکیت: بدون این بررسی، هر فروشنده‌ای می‌توانست موجودی رقیبش را
    ///    بالا ببرد یا صفر کند. «فروشنده فقط Listing خودش» تنها با policy
    ///    قابل بیان نیست (policy نمی‌داند این Listing مال کیست)، پس اینجا
    ///    اعمال می‌شود.
    /// 2) انبار اختیاری: فروشندهٔ تازه هیچ انباری ندارد و ساخت انبار خودش
    ///    یک فرآیند چندمرحله‌ای است. بدون پیش‌فرض، R49 دوباره ساخته می‌شد.
    /// 3) حالت سخت‌گیرانه: موجودی باید مثبت باشد؛ «افزودن صفر» بی‌معناست
    ///    و «افزودن منفی» باید از مسیر موجودی‌سنجی رد شود، نه اینجا.
    /// </summary>
    public async Task<InventoryStockDto> AddStockAsync(
        int listingId,
        int? warehouseId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new BusinessException("مقدار افزوده باید بزرگ‌تر از صفر باشد.", "INVALID_QUANTITY");

        var listing = await _unitOfWork.Repository<SellerProduct>()
            .GetByIdAsync(listingId, cancellationToken)
            ?? throw new NotFoundException("پیشنهاد فروش یافت نشد.", "LISTING_NOT_FOUND");

        await EnsureCanManageListingAsync(listing, cancellationToken);

        var targetWarehouseId = warehouseId ?? await ResolveDefaultWarehouseIdAsync(cancellationToken);

        var warehouse = await _unitOfWork.Repository<Warehouse>()
            .GetByIdAsync(targetWarehouseId, cancellationToken)
            ?? throw new NotFoundException("انبار یافت نشد.", "WAREHOUSE_NOT_FOUND");

        var row = await _unitOfWork.Repository<Inventory>()
            .FirstOrDefaultAsync(
                new Spec<Inventory>()
                    .Where(i => i.ListingId == listingId && i.WarehouseId == targetWarehouseId)
                    .AsTracking(),
                cancellationToken);

        if (row is null)
        {
            await _unitOfWork.Repository<Inventory>()
                .AddAsync(new Inventory(listingId, warehouse.WarehouseId, quantity), cancellationToken);

            _logger.LogInformation(
                "Inventory created for listing {ListingId} in warehouse {WarehouseId} with {Quantity}.",
                listingId, warehouse.WarehouseId, quantity);
        }
        else
        {
            // ردیفی که با IsActive=false خاموش شده باید دوباره روشن شود؛
            // وگرنه موجودی ثبت می‌شود ولی GetAvailable آن را نمی‌بیند.
            if (!row.IsActive)
                row.IsActive = true;

            row.Increase(quantity);
            _unitOfWork.Repository<Inventory>().Update(row);

            _logger.LogInformation(
                "Inventory increased for listing {ListingId} in warehouse {WarehouseId} by {Quantity}.",
                listingId, warehouse.WarehouseId, quantity);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InventoryStockDto
        {
            ListingId = listingId,
            WarehouseId = warehouse.WarehouseId,
            AddedQuantity = quantity,
            AvailableQuantity = await GetAvailableAsync(listingId, cancellationToken)
        };
    }

    /// <summary>فروشندهٔ مالک، یا یک نقش مدیریتی.</summary>
    private async Task EnsureCanManageListingAsync(SellerProduct listing, CancellationToken cancellationToken)
    {
        var userId = _currentUser.TryGetCurrentUserId()
            ?? throw new ForbiddenException("برای ثبت موجودی باید وارد شوید.", "UNAUTHENTICATED");

        var seller = await _unitOfWork.Repository<Seller>()
            .FirstOrDefaultAsync(
                new Spec<Seller>().Where(s => s.UserId == userId),
                cancellationToken);

        if (seller is not null)
        {
            if (listing.SellerId != seller.SellerId)
                throw new ForbiddenException(
                    "این پیشنهاد فروش به شما تعلق ندارد.", "LISTING_NOT_OWNED");

            if (seller.Status != SellerStatus.Approved || !seller.IsActive)
                throw new ForbiddenException(
                    "فروشنده تأیید نشده نمی‌تواند موجودی ثبت کند.", "SELLER_NOT_APPROVED");

            return;
        }

        var role = _currentUser.GetCurrentUserRole();

        if (role is "SuperAdmin" or "Admin" or "Manager")
            return;

        throw new ForbiddenException(
            "برای ثبت موجودی این پیشنهاد فروش دسترسی ندارید.", "FORBIDDEN");
    }

    /// <summary>
    /// انبار پیش‌فرض پلتفرم (SellerId == null). اگر وجود نداشته باشد ساخته می‌شود.
    ///
    /// ساختن خودکار عمدی است: بدون آن، اولین فروشندهٔ یک نصب تازه هرگز
    /// نمی‌تواند موجودی بگذارد و R49 فقط ظاهراً حل می‌شود. لاگ هشدار
    /// می‌دهد تا این «ساختن ضمنی» در تاریکی نماند.
    /// </summary>
    private async Task<int> ResolveDefaultWarehouseIdAsync(CancellationToken cancellationToken)
    {
        var warehouse = await _unitOfWork.Repository<Warehouse>()
            .FirstOrDefaultAsync(
                new Spec<Warehouse>()
                    .Where(w => w.SellerId == null)
                    .OrderBy(w => w.WarehouseId),
                cancellationToken);

        if (warehouse is not null)
            return warehouse.WarehouseId;

        _logger.LogWarning(
            "No platform warehouse found; provisioning one so that stock can be recorded.");

        // شناسه‌ها identity هستند، پس بعد از هر Add باید SaveChanges بزنیم؛
        // وگرنه ProvinceId/CityId همچنان صفر می‌ماند و FK می‌شکند.
        var now = DateTime.UtcNow;

        var province = new Province { ProvinceName = "Platform", CreatedOn = now, ModifiedOn = now };
        await _unitOfWork.Repository<Province>().AddAsync(province, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var city = new City
        {
            CityName = "Platform",
            ProvinceId = province.ProvinceId,
            CreatedOn = now,
            ModifiedOn = now
        };
        await _unitOfWork.Repository<City>().AddAsync(city, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "1",
            Unit = "1",
            PostalCode = "0000000000",
            RecipientFirstName = "Platform",
            RecipientLastName = "Warehouse",
            UserId = _currentUser.GetCurrentUserId(),
            CreatedOn = now,
            ModifiedOn = now
        };
        await _unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var created = new Warehouse
        {
            Name = "Platform Warehouse",
            AddressId = address.AddressId,
            SellerId = null,
            CreatedOn = now,
            ModifiedOn = now
        };
        await _unitOfWork.Repository<Warehouse>().AddAsync(created, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return created.WarehouseId;
    }
    private async Task<IReadOnlyList<Inventory>> GetActiveRowsAsync(int listingId, CancellationToken cancellationToken)
    {
        var spec = new Spec<Inventory>()
            .Where(i => i.ListingId == listingId && i.IsActive)
            .AsTracking();

        return await _unitOfWork.Repository<Inventory>().ListAsync(spec, cancellationToken);
    }
}