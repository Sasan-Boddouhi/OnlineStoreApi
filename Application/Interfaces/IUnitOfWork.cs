using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {

        IGenericRepository<Address> Address { get; }
        IGenericRepository<City> City { get; }
        IGenericRepository<Customer> Customer { get; }
        IGenericRepository<Employee> Employee { get; }
        IGenericRepository<EmployeeType> EmployeeType { get; }
        IGenericRepository<Inventory> Inventory { get; }
        IGenericRepository<Invoice> Invoice { get; }
        IGenericRepository<Order> Order { get; }
        IGenericRepository<OrderItem> OrderItem { get; }
        IGenericRepository<Payment> Payment { get; }
        IGenericRepository<Product> Product { get; }
        IGenericRepository<ProductCategory> ProductCategory { get; }
        IGenericRepository<ProductSubcategory> ProductSubcategory { get; }
        IGenericRepository<Province> Province { get; }
        IGenericRepository<User> User { get; }
        IGenericRepository<Warehouse> Warehouse { get; }
        IGenericRepository<Logs> Logs { get; }
        IGenericRepository<RefreshTokenEntity> RefreshToken { get; }

        // Save
        Task<int> SaveChangesAsync();

        // Transaction
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();

    }
}
