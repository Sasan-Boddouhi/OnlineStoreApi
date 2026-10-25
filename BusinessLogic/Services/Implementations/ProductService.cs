using System.Diagnostics;
using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Products;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class ProductService : IProductService
{
    private static readonly ActivitySource ActivitySource = new("OnlineStore.Services");

    /// <summary>Phase 7 (R26): نام واریانتی که همراه هر محصول جدید ساخته می‌شود.</summary>
    private const string DefaultVariantName = "Default";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProductService> _logger;
    private readonly IAuditService _auditService;

    public ProductService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ILogger<ProductService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _logger = logger;
    }

    #region Query

    public async Task<PagedResult<ProductDto>> GetByQueryAsync(
        QueryContract<Product> query,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("Product.GetByQuery", ActivityKind.Internal);

        try
        {
            var spec = query.ToSpec();

        var items = await _unitOfWork.Repository<Product>()
            .ListAsync(spec, ProductQueryConfig.Projection, cancellationToken);

        var totalCount = await _unitOfWork.Repository<Product>()
            .CountAsync(spec, cancellationToken);

        int pageNumber, pageSize;
        if (query.Skip.HasValue || query.Take.HasValue)
        {
            pageSize = query.Take ?? 20;
            var skip = query.Skip ?? 0;
            pageNumber = skip / pageSize + 1;
        }
        else
        {
            pageNumber = query.Page ?? 1;
            pageSize = query.Size ?? 20;
        }

            activity?.SetTag("products.count", totalCount);
            activity?.SetTag("page.number", pageNumber);
            activity?.SetTag("page.size", pageSize);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new PagedResult<ProductDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<ProductDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("Product.GetById", ActivityKind.Internal);
        activity?.SetTag("product.id", id);

        try
        {
            var spec = new Spec<Product>().Where(p => p.ProductId == id).Where(p => p.IsActive);

            var result = await _unitOfWork.Repository<Product>()
                .FirstOrDefaultAsync(spec, ProductQueryConfig.Projection, cancellationToken);

            activity?.SetTag("product.found", result != null);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    #endregion

    #region Commands

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("Product.Create", ActivityKind.Internal);
        activity?.SetTag("product.name", dto.Name);
        activity?.SetTag("product.price", dto.Price);
        activity?.SetTag("product.subcategory_id", dto.SubcategoryId);

        _logger.LogInformation("Creating product with name: {ProductName}", dto.Name);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureValidProductCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<Product>(dto);
            await _unitOfWork.Repository<Product>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Phase 7 (R26): فروشنده فقط روی واریانت می‌تواند پیشنهاد فروش بسازد.
            // بدون واریانت پیش‌فرض، هر محصول تازه‌ساخته‌شده عملاً غیرقابل عرضه بود.
            await _unitOfWork.Repository<ProductVariant>().AddAsync(
                new ProductVariant
                {
                    ProductId = entity.ProductId,
                    Name = DefaultVariantName,
                    IsActive = true
                },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Product created successfully with ID: {ProductId}", entity.ProductId);
            activity?.SetTag("product.id", entity.ProductId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return _mapper.Map<ProductDto>(entity);
        }
        catch (BusinessException)
        {
            // اگر خطای تجاری است، فقط rollback و دوباره پرتاب کن (بدون تغییر پیام)
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error creating product: {ProductName}", dto.Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new BusinessException("خطا در ایجاد محصول", "PRODUCT_CREATE_ERROR");
        }
    }

    public async Task<ProductDto?> UpdateAsync(
        UpdateProductDto dto,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating product with ID: {ProductId}", dto.ProductId);

        try
        {
            var entity = await _unitOfWork.Repository<Product>().GetByIdAsync(dto.ProductId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("Product not found: {ProductId}", dto.ProductId);
                return null;
            }

            if (!string.Equals(entity.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await _unitOfWork.Repository<Product>()
                    .AnyAsync(x => x.Name == dto.Name && x.ProductId != dto.ProductId, cancellationToken);
                if (exists)
                    throw new BusinessException("محصولی با این نام قبلاً ثبت شده است", "PRODUCT_NAME_DUPLICATE");
            }

            // Phase 9 (R40): تغییر قیمت باید ردپا داشته باشد.
            // قیمت مهم‌ترین فیلد تجاری است و تا امروز هیچ سابقه‌ای از تغییرش نبود.
            var previousPrice = entity.Price;

            _mapper.Map(dto, entity);

            if (previousPrice != entity.Price)
            {
                await _auditService.RecordAsync(
                    "Product.PriceChanged",
                    nameof(Product),
                    entity.ProductId.ToString(),
                    $"from={previousPrice} to={entity.Price}",
                    cancellationToken);
            }

            _unitOfWork.Repository<Product>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Product updated successfully: {ProductId}", dto.ProductId);
            return await GetByIdAsync(entity.ProductId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product: {ProductId}", dto.ProductId);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting product: {ProductId}", id);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var entity = await _unitOfWork.Repository<Product>().GetByIdAsync(id, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Product not found or inactive: {ProductId}", id);
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            entity.IsActive = false;
            _unitOfWork.Repository<Product>().Update(entity);

            await _auditService.RecordAsync(
                "Product.Deleted",
                nameof(Product),
                entity.ProductId.ToString(),
                $"name={entity.Name}",
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Product soft deleted successfully: {ProductId}", id);
            return true;
        }
        catch (BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error deleting product: {ProductId}", id);
            throw;
        }
    }

    #endregion

    #region Validation

    private async Task EnsureValidProductCreationAsync(
        CreateProductDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new BusinessException("نام محصول الزامی است.", "PRODUCT_NAME_REQUIRED");

        if (dto.Price <= 0)
            throw new BusinessException("قیمت محصول باید بیشتر از صفر باشد.", "PRODUCT_PRICE_INVALID");

        if (dto.SubcategoryId <= 0)
            throw new BusinessException("زیردسته‌بندی نامعتبر است.", "INVALID_SUBCATEGORY");

        var subcategoryExists = await _unitOfWork.Repository<ProductSubcategory>()
            .AnyAsync(x => x.SubcategoryId == dto.SubcategoryId, cancellationToken);
        if (!subcategoryExists)
            throw new BusinessException("زیردسته‌بندی انتخاب‌شده وجود ندارد.", "SUBCATEGORY_NOT_FOUND");

        var duplicateName = await _unitOfWork.Repository<Product>()
            .AnyAsync(x => x.Name == dto.Name, cancellationToken);
        if (duplicateName)
            throw new BusinessException("محصولی با این نام قبلاً ثبت شده است.", "PRODUCT_NAME_DUPLICATE");
    }

    #endregion

    #region Phase 6 — Variants & Attributes

    public async Task<ProductVariantDto> CreateVariantAsync(
        CreateProductVariantDto dto,
        CancellationToken cancellationToken = default)
    {
        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(dto.ProductId, cancellationToken);
        if (product is null)
            throw new NotFoundException("محصول یافت نشد.", "PRODUCT_NOT_FOUND");

        var sku = string.IsNullOrWhiteSpace(dto.Sku) ? null : dto.Sku.Trim();

        if (sku is not null)
        {
            var duplicateSku = await _unitOfWork.Repository<ProductVariant>()
                .AnyAsync(v => v.Sku == sku, cancellationToken);
            if (duplicateSku)
                throw new BusinessException("این SKU قبلاً استفاده شده است.", "SKU_ALREADY_EXISTS");
        }

        // ویژگی‌ها باید متعلق به همین محصول باشند، وگرنه واریانت به ویژگی محصول دیگری وصل می‌شود.
        var attributeIds = dto.Attributes.Select(a => a.AttributeId).Distinct().ToList();
        if (attributeIds.Count > 0)
        {
            var owned = await _unitOfWork.Repository<ProductAttribute>()
                .ListAsync(new Spec<ProductAttribute>()
                    .Where(a => a.ProductId == dto.ProductId && attributeIds.Contains(a.AttributeId)),
                    cancellationToken);

            var missing = attributeIds.Except(owned.Select(a => a.AttributeId)).ToList();
            if (missing.Count > 0)
                throw new BusinessException(
                    $"ویژگی‌های {string.Join(", ", missing)} به این محصول تعلق ندارند.",
                    "ATTRIBUTE_NOT_IN_PRODUCT");
        }

        var variant = new ProductVariant
        {
            ProductId = dto.ProductId,
            Name = dto.Name,
            Sku = sku,
            Barcode = dto.Barcode,
            IsActive = dto.IsActive
        };

        await _unitOfWork.Repository<ProductVariant>().AddAsync(variant, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var input in dto.Attributes)
        {
            await _unitOfWork.Repository<ProductAttributeValue>().AddAsync(
                new ProductAttributeValue
                {
                    VariantId = variant.VariantId,
                    AttributeId = input.AttributeId,
                    Value = input.Value
                },
                cancellationToken);
        }

        if (dto.Attributes.Count > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Variant {VariantId} created for product {ProductId}", variant.VariantId, dto.ProductId);

        return await BuildVariantDtoAsync(variant, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductVariantDto>> GetVariantsAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        var variants = await _unitOfWork.Repository<ProductVariant>()
            .ListAsync(new Spec<ProductVariant>()
                .Where(v => v.ProductId == productId)
                .OrderBy(v => v.VariantId),
                cancellationToken);

        var result = new List<ProductVariantDto>(variants.Count);
        foreach (var variant in variants)
            result.Add(await BuildVariantDtoAsync(variant, cancellationToken));

        return result;
    }

    public async Task<VariantAttributeDto> CreateAttributeAsync(
        CreateProductAttributeDto dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new BusinessException("نام ویژگی الزامی است.", "ATTRIBUTE_NAME_REQUIRED");

        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(dto.ProductId, cancellationToken);
        if (product is null)
            throw new NotFoundException("محصول یافت نشد.", "PRODUCT_NOT_FOUND");

        var name = dto.Name.Trim();

        var duplicate = await _unitOfWork.Repository<ProductAttribute>()
            .AnyAsync(a => a.ProductId == dto.ProductId && a.Name == name, cancellationToken);
        if (duplicate)
            throw new BusinessException("این ویژگی برای محصول ثبت شده است.", "ATTRIBUTE_ALREADY_EXISTS");

        var attribute = new ProductAttribute
        {
            ProductId = dto.ProductId,
            Name = name,
            Type = dto.Type
        };

        await _unitOfWork.Repository<ProductAttribute>().AddAsync(attribute, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new VariantAttributeDto
        {
            AttributeId = attribute.AttributeId,
            Name = attribute.Name,
            Type = attribute.Type.ToString(),
            Value = string.Empty
        };
    }

    public async Task<ProductVariantDto?> UpdateVariantAsync(int variantId, UpdateProductVariantDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating variant {VariantId}", variantId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(variantId, cancellationToken);
            if (variant == null)
                return null;

            // Validate SKU uniqueness if changed
            var sku = string.IsNullOrWhiteSpace(dto.Sku) ? null : dto.Sku.Trim();
            if (sku is not null && sku != variant.Sku)
            {
                var duplicateSku = await _unitOfWork.Repository<ProductVariant>()
                    .AnyAsync(v => v.Sku == sku && v.VariantId != variantId, cancellationToken);
                if (duplicateSku)
                    throw new BusinessException("این SKU قبلاً استفاده شده است.", "SKU_ALREADY_EXISTS");
            }

            // Validate attributes belong to the same product
            var attributeIds = dto.Attributes.Select(a => a.AttributeId).Distinct().ToList();
            if (attributeIds.Count > 0)
            {
                var owned = await _unitOfWork.Repository<ProductAttribute>()
                    .ListAsync(new Spec<ProductAttribute>()
                        .Where(a => a.ProductId == variant.ProductId && attributeIds.Contains(a.AttributeId)),
                        cancellationToken);

                var missing = attributeIds.Except(owned.Select(a => a.AttributeId)).ToList();
                if (missing.Count > 0)
                    throw new BusinessException(
                        $"ویژگی‌های {string.Join(", ", missing)} به این محصول تعلق ندارند.",
                        "ATTRIBUTE_NOT_IN_PRODUCT");
            }

            // Update variant properties
            variant.Name = dto.Name;
            variant.Sku = sku;
            variant.Barcode = dto.Barcode;
            variant.IsActive = dto.IsActive;

            _unitOfWork.Repository<ProductVariant>().Update(variant);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Update attributes if provided
            if (dto.Attributes.Count > 0)
            {
                // Get existing attribute values for this variant
                var existingValues = await _unitOfWork.Repository<ProductAttributeValue>()
                    .ListAsync(new Spec<ProductAttributeValue>().Where(v => v.VariantId == variantId), cancellationToken);
                
                // Create a lookup of existing values by AttributeId
                var existingByAttributeId = existingValues.ToDictionary(v => v.AttributeId);
                var incomingAttributeIds = dto.Attributes.Select(a => a.AttributeId).ToHashSet();

                // Remove attribute values that are no longer in the update
                foreach (var existing in existingValues)
                {
                    if (!incomingAttributeIds.Contains(existing.AttributeId))
                    {
                        _unitOfWork.Repository<ProductAttributeValue>().Delete(existing);
                    }
                }

                // Update existing or add new attribute values
                foreach (var input in dto.Attributes)
                {
                    if (existingByAttributeId.TryGetValue(input.AttributeId, out var existing))
                    {
                        // Update existing
                        existing.Value = input.Value;
                        _unitOfWork.Repository<ProductAttributeValue>().Update(existing);
                    }
                    else
                    {
                        // Add new
                        await _unitOfWork.Repository<ProductAttributeValue>().AddAsync(
                            new ProductAttributeValue
                            {
                                VariantId = variantId,
                                AttributeId = input.AttributeId,
                                Value = input.Value
                            },
                            cancellationToken);
                    }
                }
                
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Variant {VariantId} updated successfully", variantId);

            return await BuildVariantDtoAsync(variant, cancellationToken);
        }
        catch (BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error updating variant: {VariantId}", variantId);
            var msg = ex.InnerException?.Message ?? ex.Message;
            throw new BusinessException($"خطا در ویرایش واریانت: {msg}", "VARIANT_UPDATE_ERROR");
        }
    }

    public async Task<bool> DeleteVariantAsync(int variantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting variant {VariantId}", variantId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var variant = await _unitOfWork.Repository<ProductVariant>().GetByIdAsync(variantId, cancellationToken);
            if (variant == null)
                return false;

            // Check for active listings (SellerProduct) referencing this variant
            var hasActiveListings = await _unitOfWork.Repository<SellerProduct>()
                .AnyAsync(l => l.VariantId == variantId && l.Status == SellerProductStatus.Active, cancellationToken);

            if (hasActiveListings)
            {
                _logger.LogWarning("Cannot delete variant {VariantId} - has active listings", variantId);
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw new BusinessException(
                    "این واریانت پیشنهاد فروش فعال دارد و قابل حذف نیست. ابتدا پیشنهادهای فروش را غیرفعال کنید.",
                    "VARIANT_HAS_ACTIVE_LISTINGS");
            }

            // Check for OrderItems referencing this variant via listings
            var listingIds = await _unitOfWork.Repository<SellerProduct>()
                .ListAsync(new Spec<SellerProduct>().Where(l => l.VariantId == variantId), cancellationToken);
            
            var listingIdsList = listingIds.Select(l => l.ListingId).ToList();
            
            if (listingIdsList.Count > 0)
            {
                var hasOrderItems = await _unitOfWork.Repository<OrderItem>()
                    .AnyAsync(oi => listingIdsList.Contains(oi.ListingId), cancellationToken);

                if (hasOrderItems)
                {
                    _logger.LogWarning("Cannot delete variant {VariantId} - has order items", variantId);
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    throw new BusinessException(
                        "این واریант دارای سفارش ثبت‌شده است و قابل حذف نیست.",
                        "VARIANT_HAS_ORDERS");
                }
            }

            // Delete attribute values first
            var attrValues = await _unitOfWork.Repository<ProductAttributeValue>()
                .ListAsync(new Spec<ProductAttributeValue>().Where(v => v.VariantId == variantId), cancellationToken);
            
            foreach (var av in attrValues)
            {
                _unitOfWork.Repository<ProductAttributeValue>().Delete(av);
            }

            // Delete the variant
            _unitOfWork.Repository<ProductVariant>().Delete(variant);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Variant {VariantId} deleted successfully", variantId);

            return true;
        }
        catch (BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Error deleting variant: {VariantId}", variantId);
            throw new BusinessException("خطا در حذف واریانت", "VARIANT_DELETE_ERROR");
        }
    }

    /// <summary>
    /// مقادیر ویژگی‌ها در کوئری جدا خوانده می‌شوند تا به ThenInclude نیاز نباشد
    /// (Spec فقط Include تک‌سطحی دارد).
    /// </summary>
    private async Task<ProductVariantDto> BuildVariantDtoAsync(
        ProductVariant variant,
        CancellationToken cancellationToken)
    {
        var values = await _unitOfWork.Repository<ProductAttributeValue>()
            .ListAsync(new Spec<ProductAttributeValue>()
                .Where(v => v.VariantId == variant.VariantId),
                cancellationToken);

        var attributeIds = values.Select(v => v.AttributeId).Distinct().ToList();

        var attributes = attributeIds.Count == 0
            ? new List<ProductAttribute>()
            : (await _unitOfWork.Repository<ProductAttribute>()
                .ListAsync(new Spec<ProductAttribute>().Where(a => attributeIds.Contains(a.AttributeId)),
                    cancellationToken)).ToList();

        return new ProductVariantDto
        {
            VariantId = variant.VariantId,
            ProductId = variant.ProductId,
            Name = variant.Name,
            Sku = variant.Sku,
            Barcode = variant.Barcode,
            IsActive = variant.IsActive,
            Attributes = values
                .OrderBy(v => v.AttributeId)
                .Select(v =>
                {
                    var attribute = attributes.FirstOrDefault(a => a.AttributeId == v.AttributeId);
                    return new VariantAttributeDto
                    {
                        AttributeId = v.AttributeId,
                        Name = attribute?.Name ?? string.Empty,
                        Type = attribute?.Type.ToString() ?? "Text",
                        Value = v.Value
                    };
                })
                .ToList()
        };
    }

    #endregion
}