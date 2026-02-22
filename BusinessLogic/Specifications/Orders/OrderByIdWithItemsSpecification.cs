using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Orders
{
    public sealed class OrderByIdWithItemsSpecification : BaseSpecification<Order>
    {
        public OrderByIdWithItemsSpecification(int orderId)
        {
            Criteria = o => o.OrderId == orderId;
            AddInclude(o => o.OrderItems);
        }
    }
}
