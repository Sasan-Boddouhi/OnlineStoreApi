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
            modelBuilder.Entity<Warehouse>().HasIndex(w => w.Name).IsUnique();
            modelBuilder.Entity<Order>().HasIndex(o => o.OrderDate);
            modelBuilder.Entity<Invoice>().HasIndex(i => i.InvoiceNumber).IsUnique();
            modelBuilder.Entity<Payment>().HasIndex(p => p.TransactionId).IsUnique();

            // ------------------- Relations -------------------

            // User : Customer (1:1)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Customer)
                .WithOne(c => c.User)
                .HasForeignKey<Customer>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

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
                .HasForeignKey(sc => sc.CategoryId);

            // ProductSubcategory : Product (1:N)
            modelBuilder.Entity<ProductSubcategory>()
                .HasMany(sc => sc.Products)
                .WithOne(p => p.Subcategory)
                .HasForeignKey(p => p.SubcategoryId);

            // Product : Inventory (1:N)
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Inventories)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductId);

            // Warehouse : Inventory (1:N)
            modelBuilder.Entity<Warehouse>()
                .HasMany(w => w.Inventories)
                .WithOne(i => i.Warehouse)
                .HasForeignKey(i => i.WarehouseId);

            // Customer : Order (1:N)
            modelBuilder.Entity<Customer>()
                .HasMany(c => c.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId);

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
                .OnDelete(DeleteBehavior.Cascade);

            // Invoice : Payment (1:N)
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Payments)
                .WithOne(p => p.Invoice)
                .HasForeignKey(p => p.InvoiceId);

            // Province : City (1:N)
            modelBuilder.Entity<Province>()
                .HasMany(p => p.Cities)
                .WithOne(c => c.Province)
                .HasForeignKey(c => c.ProvinceId);

            // City : Address (1:N)
            modelBuilder.Entity<City>()
                .HasMany(c => c.Addresses)
                .WithOne(a => a.City)
                .HasForeignKey(a => a.CityId);

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

            // RefreshToken
            modelBuilder.Entity<RefreshTokenEntity>()
                .Property(e => e.RowVersion)
                .IsRowVersion();

            modelBuilder.Entity<RefreshTokenEntity>()
                .HasIndex(r => r.AbsoluteExpiry);
        }
    }
}