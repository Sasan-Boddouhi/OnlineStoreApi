using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Orders
{
    public sealed class OrdersByCustomerSpecification : BaseSpecification<Order>
    {
        public OrdersByCustomerSpecification(int customerId)
        {
            Criteria = o => o.CustomerId == customerId;
            AddInclude(o => o.OrderItems);
            ApplyOrderByDescending(o => o.OrderDate);
        }
    }
}
