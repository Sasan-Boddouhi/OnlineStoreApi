using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BusinessLogic.Specifications.Addresses
{
    public sealed class DefaultAddressesByUserSpec : BaseSpecification<Address>
    {
        public DefaultAddressesByUserSpec(int userId)
        {
            AddCriteria(a => a.UserId == userId && a.IsDefault);
        }
    }
}
