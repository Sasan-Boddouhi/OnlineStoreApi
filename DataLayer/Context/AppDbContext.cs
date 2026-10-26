using Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.Generic;

namespace DataLayer.Context
{
    public class AppDbContext : DbContext
    {
        private readonly IEnumerable<ISaveChangesInterceptor> _interceptors;

        public AppDbContext(DbContextOptions<AppDbContext> options, IEnumerable<ISaveChangesInterceptor> interceptors)
            : base(options)
        {
            _interceptors = interceptors;
        }

        // ------------------- DbSets -------------------
        public DbSet<User> User { get; set; }
        public DbSet<Customer> Customer { get; set; }
        public DbSet<Employee> Employee { get; set; }
        public DbSet<EmployeeType> EmployeeType { get; set; }
        public DbSet<Address> Address { get; set; }
        public DbSet<City> City { get; set; }
        public DbSet<Province> Province { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<ProductCategory> ProductCategory { get; set; }
        public DbSet<ProductSubcategory> ProductSubcategory { get; set; }
        public DbSet<Warehouse> Warehouse { get; set; }
        public DbSet<Inventory> Inventory { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderItem> OrderItem { get; set; }
        public DbSet<Invoice> Invoice { get; set; }
        public DbSet<Payment> Payment { get; set; }
        public DbSet<Logs> Logs { get; set; }
        public DbSet<RefreshTokenEntity> RefreshToken { get; set; }
        public DbSet<UserSession> UserSession { get; set; }

        // RBAC (Phase 1)
        public DbSet<Role> Role { get; set; }
        public DbSet<Permission> Permission { get; set; }
        public DbSet<RolePermission> RolePermission { get; set; }
        public DbSet<UserRole> UserRole { get; set; }
        public DbSet<AuditLog> AuditLog { get; set; }

        // Marketplace (Phase 2)
        public DbSet<Seller> Seller { get; set; }
        public DbSet<SellerProduct> SellerProduct { get; set; }

        // Financials & Order Splitting (Phase 3)
        public DbSet<OrderSellerGroup> OrderSellerGroup { get; set; }
        public DbSet<SellerPayout> SellerPayout { get; set; }

        // Phase 4 — سبد خرید
        public DbSet<Cart> Cart { get; set; }
        public DbSet<CartItem> CartItem { get; set; }

        // Phase 6 — تنوع و ویژگی‌ها
        public DbSet<ProductVariant> ProductVariant { get; set; }
        public DbSet<ProductAttribute> ProductAttribute { get; set; }
        public DbSet<ProductAttributeValue> ProductAttributeValue { get; set; }

        // Phase 5 — رسانه و نظرات
        public DbSet<ProductImage> ProductImage { get; set; }
        public DbSet<ProductReview> ProductReview { get; set; }
        public DbSet<SellerReview> SellerReview { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            foreach (var interceptor in _interceptors)
                optionsBuilder.AddInterceptors(interceptor);

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ------------------- Indexes -------------------
            modelBuilder.Entity<User>().HasIndex(u => u.PhoneNumber).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => p.Name);
            modelBuilder.Entity<Product>().HasIndex(p => p.Barcode).IsUnique();
            modelBuilder.Entity<ProductCategory>().HasIndex(c => c.CategoryName).IsUnique();
            modelBuilder.Entity<ProductSubcategory>().HasIndex(sc => sc.SubcategoryName).IsUnique();
            // Phase 2: نام انبار به‌ازای فروشنده یکتاست، نه سراسری.
            modelBuilder.Entity<Warehouse>()
                .HasIndex(w => new { w.SellerId, w.Name })
                .IsUnique();

            // و برای انبارهای پلتفرم (SellerId NULL) نام باید همچنان یکتا بماند.
            // SQL Server در ایندکس یکتا NULLها را برابر می‌داند، اما ایندکس فیلترشده صریح‌تر و مطمئن‌تر است.
            modelBuilder.Entity<Warehouse>()
                .HasIndex(w => w.Name)
                .IsUnique()
                .HasFilter("[SellerId] IS NULL");
            modelBuilder.Entity<Order>().HasIndex(o => o.OrderDate);
            modelBuilder.Entity<Order>().HasIndex(o => o.CustomerId);
            modelBuilder.Entity<Order>().HasIndex(o => o.Status);
            modelBuilder.Entity<OrderItem>().HasIndex(oi => oi.OrderId);
            modelBuilder.Entity<OrderItem>().HasIndex(oi => oi.ProductId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.InvoiceNumber).IsUnique();
            modelBuilder.Entity<Invoice>().HasIndex(i => i.OrderId);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.Status);
            modelBuilder.Entity<Address>().HasIndex(a => a.UserId);
            modelBuilder.Entity<Address>().HasIndex(a => a.CityId);
            modelBuilder.Entity<City>().HasIndex(c => c.ProvinceId);
            modelBuilder.Entity<Product>().HasIndex(p => p.SubcategoryId);
            modelBuilder.Entity<Product>().HasIndex(p => p.IsActive);
            modelBuilder.Entity<ProductVariant>().HasIndex(v => v.ProductId);
            // Phase 9: فیلتر صریح اعلام می‌شود تا مدل با اسکیما یکی باشد.
            // توجه: این ایندکس از InitialCreate فیلتر داشته و هیچ‌گاه یکتایِ بدون فیلتر نبوده؛
            // پیش از این فقط در مدل C# اعلام نشده بود و همین واگرایی، ادعای اشتباه
            // «باگ بحرانی» را در گزارش فاز ۸ ایجاد کرد.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.TransactionId)
                .IsUnique()
                .HasFilter("[TransactionId] IS NOT NULL");

            // Phase 8 — Idempotency در سطح دیتابیس، نه فقط کد.
            // فیلتر لازم است چون SQL Server فقط یک NULL را در ایندکس یکتا می‌پذیرد
            // و پرداخت‌های بدون کلید (مثلاً دستی) باید بتوانند چندتایی باشند.
            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.OrderId);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.State);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // RBAC (Phase 1)
            modelBuilder.Entity<Role>().HasIndex(r => r.Code).IsUnique();
            modelBuilder.Entity<Permission>().HasIndex(p => p.Code).IsUnique();
            modelBuilder.Entity<UserRole>().HasIndex(ur => ur.UserId);
            modelBuilder.Entity<RolePermission>().HasIndex(rp => rp.RoleId);
            modelBuilder.Entity<AuditLog>().HasIndex(a => a.OccurredOnUtc);
            modelBuilder.Entity<AuditLog>().HasIndex(a => new { a.UserId, a.Action });

            // Marketplace (Phase 2)
            modelBuilder.Entity<Seller>().HasIndex(s => s.UserId).IsUnique();
            modelBuilder.Entity<Seller>().HasIndex(s => s.StoreName).IsUnique();
            modelBuilder.Entity<Seller>()
                .HasIndex(s => s.Slug)
                .IsUnique()
                .HasFilter("[Slug] IS NOT NULL");

            // Phase 6: یک فروشنده یک Listing به‌ازای هر واریانت.
            modelBuilder.Entity<SellerProduct>()
                .HasIndex(l => new { l.SellerId, l.VariantId })
                .IsUnique();
            modelBuilder.Entity<SellerProduct>().HasIndex(l => l.VariantId);
            modelBuilder.Entity<SellerProduct>().HasIndex(l => new { l.SellerId, l.Status });
            modelBuilder.Entity<OrderItem>().HasIndex(oi => oi.ListingId);

            // Financials & Splitting (Phase 3)
            modelBuilder.Entity<OrderSellerGroup>()
                .HasIndex(g => new { g.OrderId, g.SellerId })
                .IsUnique();
            modelBuilder.Entity<OrderItem>().HasIndex(i => i.OrderSellerGroupId);
            modelBuilder.Entity<SellerPayout>().HasIndex(p => p.SellerId);
            modelBuilder.Entity<SellerPayout>().HasIndex(p => p.Status);

            // پرداخت تکراری برای یک گروه ممکن نباشد (idempotency تحویل).
            modelBuilder.Entity<SellerPayout>()
                .HasIndex(p => p.OrderSellerGroupId)
                .IsUnique()
                .HasFilter("[OrderSellerGroupId] IS NOT NULL");

            modelBuilder.Entity<Seller>()
                .Property(s => s.CommissionRate)
                .HasPrecision(5, 2);

            // ------------------- Relations -------------------

            // User : Customer (1:1)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Customer)
                .WithOne(c => c.User)
                .HasForeignKey<Customer>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .Property(u => u.UserType)
                .HasConversion<int>()
                .IsRequired();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // User : Employee (1:1)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Employee)
                .WithOne(e => e.User)
                .HasForeignKey<Employee>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ProductCategory : ProductSubcategory (1:N)
            modelBuilder.Entity<ProductCategory>()
                .HasMany(c => c.Subcategories)
                .WithOne(sc => sc.Category)
                .HasForeignKey(sc => sc.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ProductSubcategory : Product (1:N)
            modelBuilder.Entity<ProductSubcategory>()
                .HasMany(sc => sc.Products)
                .WithOne(p => p.Subcategory)
                .HasForeignKey(p => p.SubcategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Inventory>(entity =>
            {
                entity.Property(i => i.MinimumStock)
                    .HasPrecision(18, 2);

                entity.Property(i => i.RowVersion)
                    .IsRowVersion()
                    .IsConcurrencyToken();
            });

            // ------------------- RBAC (Phase 1) -------------------

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(rp => new { rp.RoleId, rp.PermissionId });

                entity.HasOne(rp => rp.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(rp => rp.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Restrict: SQL Server چند مسیر Cascade به RolePermission را رد می‌کند.
                entity.HasOne(rp => rp.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(rp => rp.PermissionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(ur => new { ur.UserId, ur.RoleId });

                entity.HasOne(ur => ur.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Restrict: همان دلیل بالا (چند مسیر Cascade به UserRole).
                entity.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------- Marketplace (Phase 2) -------------------

            // User : Seller (1:1) — Restrict: حذف کاربر نباید فروشگاه را بی‌صدا پاک کند.
            modelBuilder.Entity<Seller>()
                .HasOne(s => s.User)
                .WithOne(u => u.Seller)
                .HasForeignKey<Seller>(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seller : SellerProduct (1:N)
            modelBuilder.Entity<Seller>()
                .HasMany(s => s.Listings)
                .WithOne(l => l.Seller)
                .HasForeignKey(l => l.SellerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Phase 6 — Product : ProductVariant (1:N)
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Phase 6 — ProductVariant : SellerProduct (1:N).
            // Restrict: حذف واریانت نباید پیشنهاد فروشی که سفارش گرفته را پاک کند.
            modelBuilder.Entity<ProductVariant>()
                .HasMany(v => v.Listings)
                .WithOne(l => l.Variant)
                .HasForeignKey(l => l.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Phase 6 — Product : ProductAttribute (1:N)
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Attributes)
                .WithOne(a => a.Product)
                .HasForeignKey(a => a.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Phase 6 — ProductAttributeValue: یک مقدار برای هر (واریانت، ویژگی).
            modelBuilder.Entity<ProductAttributeValue>()
                .HasIndex(av => new { av.VariantId, av.AttributeId })
                .IsUnique();

            modelBuilder.Entity<ProductAttributeValue>()
                .HasOne(av => av.Variant)
                .WithMany(v => v.AttributeValues)
                .HasForeignKey(av => av.VariantId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductAttributeValue>()
                .HasOne(av => av.Attribute)
                .WithMany(a => a.Values)
                .HasForeignKey(av => av.AttributeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Phase 6 — SKU یکتاست (وقتی مقدار دارد).
            modelBuilder.Entity<ProductVariant>()
                .HasIndex(v => v.Sku)
                .IsUnique()
                .HasFilter("[Sku] IS NOT NULL");

            modelBuilder.Entity<ProductAttribute>()
                .HasIndex(a => new { a.ProductId, a.Name })
                .IsUnique();

            // Seller : Warehouse (1:N) — null یعنی انبار پلتفرم.
            modelBuilder.Entity<Seller>()
                .HasMany(s => s.Warehouses)
                .WithOne(w => w.Seller)
                .HasForeignKey(w => w.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // SellerProduct : Inventory (1:N) — D1: موجودی مال Listing است.
            modelBuilder.Entity<SellerProduct>()
                .HasMany(l => l.Inventories)
                .WithOne(i => i.Listing)
                .HasForeignKey(i => i.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Warehouse : Inventory (1:N)
            modelBuilder.Entity<Warehouse>()
                .HasMany(w => w.Inventories)
                .WithOne(i => i.Warehouse)
                .HasForeignKey(i => i.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderItem : SellerProduct (N:1) — در فاز ۲ nullable بود و در فاز ۳ اجباری شد.
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Listing)
                .WithMany(l => l.OrderItems)
                .HasForeignKey(oi => oi.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Variant)
                .WithMany(v => v.OrderItems)
                .HasForeignKey(oi => oi.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // ------------------- Financials & Splitting (Phase 3) -------------------

            // Order : OrderSellerGroup (1:N)
            modelBuilder.Entity<Order>()
                .HasMany(o => o.SellerGroups)
                .WithOne(g => g.Order)
                .HasForeignKey(g => g.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seller : OrderSellerGroup (1:N) — Restrict: حذف فروشنده نباید تاریخ مالی را پاک کند.
            modelBuilder.Entity<OrderSellerGroup>()
                .HasOne(g => g.Seller)
                .WithMany(s => s.OrderSellerGroups)
                .HasForeignKey(g => g.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // OrderSellerGroup : OrderItem (1:N)
            modelBuilder.Entity<OrderSellerGroup>()
                .HasMany(g => g.Items)
                .WithOne(i => i.OrderSellerGroup)
                .HasForeignKey(i => i.OrderSellerGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            // SellerPayout : Seller (N:1)
            modelBuilder.Entity<SellerPayout>()
                .HasOne(p => p.Seller)
                .WithMany(s => s.Payouts)
                .HasForeignKey(p => p.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // SellerPayout : OrderSellerGroup (1:1) — یک گروه، یک پرداخت.
            modelBuilder.Entity<SellerPayout>()
                .HasOne(p => p.OrderSellerGroup)
                .WithOne(g => g.Payout)
                .HasForeignKey<SellerPayout>(p => p.OrderSellerGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            // Customer : Order (1:N)
            modelBuilder.Entity<Customer>()
                .HasMany(c => c.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Order : OrderItem (1:N)
            modelBuilder.Entity<Order>()
                .HasMany(o => o.OrderItems)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId);

            // Order : Invoice (1:1, optional)
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Invoice)
                .WithOne(i => i.Order)
                .HasForeignKey<Invoice>(i => i.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Invoice : Payment (1:N)
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Payments)
                .WithOne(p => p.Invoice)
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            // Province : City (1:N)
            modelBuilder.Entity<Province>()
                .HasMany(p => p.Cities)
                .WithOne(c => c.Province)
                .HasForeignKey(c => c.ProvinceId)
                .OnDelete(DeleteBehavior.Restrict);

            // City : Address (1:N)
            modelBuilder.Entity<City>()
                .HasMany(c => c.Addresses)
                .WithOne(a => a.City)
                .HasForeignKey(a => a.CityId)
                .OnDelete(DeleteBehavior.Restrict);

            // Warehouse : Address (N:1) 
            modelBuilder.Entity<Warehouse>()
                .HasOne(w => w.Address)
                .WithMany(a => a.Warehouses)
                .HasForeignKey(w => w.AddressId)
                .OnDelete(DeleteBehavior.Restrict);

            // EmployeeType : Employee (1:N)
            modelBuilder.Entity<Employee>()
                .HasOne(e => e.EmployeeType)
                .WithMany(et => et.Employees)
                .HasForeignKey(e => e.EmployeeTypeId);

            // ------------------- UserSession -------------------
            modelBuilder.Entity<UserSession>(entity =>
            {
                entity.ToTable("UserSessions");

                entity.HasKey(s => s.Id);

                entity.HasOne(s => s.User)
                    .WithMany(u => u.Sessions)
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(s => s.RefreshTokens)
                    .WithOne(r => r.Session)
                    .HasForeignKey(r => r.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(s => s.DeviceId)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(s => s.DeviceName)
                    .HasMaxLength(200);

                entity.Property(s => s.IpAddress)
                    .HasMaxLength(45);

                entity.Property(s => s.UserAgent)
                    .HasMaxLength(500);

                entity.Property(s => s.CreatedAtUtc)
                    .IsRequired();

                entity.Property(s => s.LastActivityUtc)
                    .IsRequired();

                entity.Property(s => s.AbsoluteExpiryUtc)
                    .IsRequired();

                entity.Property(s => s.Status)
                    .HasConversion<int>()
                    .IsRequired();

                entity.Property(s => s.RowVersion)
                    .IsRowVersion()
                    .IsConcurrencyToken();

                entity.HasIndex(s => s.UserId);
                entity.HasIndex(s => s.Status);
                entity.HasIndex(s => s.AbsoluteExpiryUtc);
                entity.HasIndex(s => new { s.UserId, s.Status });
            });

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasIndex(r => new { r.SessionId, r.IsRevoked });

            // ------------------- RefreshToken -------------------

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasIndex(r => r.TokenIdentifier)
                .IsUnique();

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasIndex(r => r.AbsoluteExpiry);

            modelBuilder.Entity<RefreshTokenEntity>()
                .Property(r => r.RowVersion)
                .IsRowVersion();

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasOne(r => r.Session)
                .WithMany(s => s.RefreshTokens)
                .HasForeignKey(r => r.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasOne(r => r.ReplacedByToken)
                .WithMany()
                .HasForeignKey(r => r.ReplacedByTokenId)
                .OnDelete(DeleteBehavior.Restrict);

            // ------------------- Cart (Phase 4) -------------------

            modelBuilder.Entity<Cart>(entity =>
            {
                // هر کاربر و هر نشست فقط یک سبد دارد. ایندکس فیلترشده لازم است
                // چون SQL Server چند NULL را در ایندکس یکتا مجاز می‌داند ولی
                // بدون فیلتر، دو سبد مهمان با SessionId=NULL هم رد می‌شدند.
                entity.HasIndex(c => c.UserId)
                    .IsUnique()
                    .HasFilter("[UserId] IS NOT NULL");

                entity.HasIndex(c => c.SessionId)
                    .IsUnique()
                    .HasFilter("[SessionId] IS NOT NULL");

                entity.HasOne(c => c.User)
                    .WithOne(u => u.Cart)
                    .HasForeignKey<Cart>(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(c => c.Items)
                    .WithOne(i => i.Cart)
                    .HasForeignKey(i => i.CartId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CartItem>(entity =>
            {
                // یک Listing در یک سبد فقط یک قلم دارد؛ افزودن دوباره، تعداد را زیاد می‌کند.
                entity.HasIndex(i => new { i.CartId, i.ListingId }).IsUnique();

                entity.HasOne(i => i.Listing)
                    .WithMany(l => l.CartItems)
                    .HasForeignKey(i => i.ListingId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ------------------- ProductImage (Phase 5) -------------------

            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.HasOne(i => i.Product)
                    .WithMany(p => p.Images)
                    .HasForeignKey(i => i.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(i => new { i.ProductId, i.SortOrder });

                // حداکثر یک تصویر شاخص برای هر محصول — در سطح دیتابیس، نه فقط کد.
                entity.HasIndex(i => i.ProductId)
                    .IsUnique()
                    .HasFilter("[IsPrimary] = 1");
            });

            // ------------------- ProductReview (Phase 5) -------------------

            modelBuilder.Entity<ProductReview>(entity =>
            {
                entity.HasOne(r => r.Product)
                    .WithMany(p => p.Reviews)
                    .HasForeignKey(r => r.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Customer)
                    .WithMany(c => c.ProductReviews)
                    .HasForeignKey(r => r.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                // آیتم سفارشی که خرید را اثبات می‌کند؛ حذفش نظر را از بین نمی‌برد.
                entity.HasOne(r => r.OrderItem)
                    .WithOne(oi => oi.Review)
                    .HasForeignKey<ProductReview>(r => r.OrderItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                // یک نظر برای هر (محصول، مشتری).
                entity.HasIndex(r => new { r.ProductId, r.CustomerId }).IsUnique();

                entity.HasIndex(r => new { r.ProductId, r.IsApproved });
            });

            // ------------------- SellerReview (Phase 5) -------------------

            modelBuilder.Entity<SellerReview>(entity =>
            {
                entity.HasOne(r => r.Seller)
                    .WithMany(s => s.Reviews)
                    .HasForeignKey(r => r.SellerId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(r => r.Customer)
                    .WithMany(c => c.SellerReviews)
                    .HasForeignKey(r => r.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(r => new { r.SellerId, r.CustomerId }).IsUnique();

                entity.HasIndex(r => new { r.SellerId, r.IsApproved });
            });

            // Concurrency tokens
            modelBuilder.Entity<Order>(entity =>
            {
                entity.Property(o => o.RowVersion)
                    .IsRowVersion()
                    .IsConcurrencyToken();
            });

            modelBuilder.Entity<SellerProduct>(entity =>
            {
                entity.Property(sp => sp.RowVersion)
                    .IsRowVersion()
                    .IsConcurrencyToken();
            });
        }
    }
}