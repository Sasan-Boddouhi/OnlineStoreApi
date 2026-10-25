using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Inventory")]
    [Index(nameof(ListingId), nameof(WarehouseId), IsUnique = true)]
    public class Inventory : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int InventoryId { get; private set; }

        /// <summary>Phase 2: موجودی به یک پیشنهاد فروش (Listing) تعلق دارد، نه به محصول.</summary>
        public int ListingId { get; private set; }

        [ForeignKey(nameof(ListingId))]
        public SellerProduct Listing { get; private set; } = null!;

        public int WarehouseId { get; private set; }

        [ForeignKey(nameof(WarehouseId))]
        public Warehouse Warehouse { get; private set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; private set; }

        public decimal MinimumStock { get; private set; } = 0;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// توکن هم‌زمانی خوش‌بینانه. بدون آن، دو سفارش هم‌زمان می‌توانند موجودی را منفی کنند.
        /// </summary>
        public byte[] RowVersion { get; set; } = null!;

        private Inventory() { } // For EF

        public Inventory(int listingId, int warehouseId, decimal initialQuantity = 0)
        {
            if (initialQuantity < 0)
                throw new ArgumentException("Initial quantity cannot be negative.");

            ListingId = listingId;
            WarehouseId = warehouseId;
            Quantity = initialQuantity;
        }

        public void Increase(decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentException("Increase amount must be greater than zero.");

            Quantity += amount;
        }

        public void Decrease(decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentException("Decrease amount must be greater than zero.");

            if (Quantity < amount)
                throw new InvalidOperationException("موجودی کافی نیست.");

            Quantity -= amount;
        }

        public bool IsLowStock()
        {
            return Quantity <= MinimumStock;
        }

        public void SetMinimumStock(decimal minimum)
        {
            if (minimum < 0)
                throw new ArgumentException("Minimum stock cannot be negative.");

            MinimumStock = minimum;
        }
    }
}
