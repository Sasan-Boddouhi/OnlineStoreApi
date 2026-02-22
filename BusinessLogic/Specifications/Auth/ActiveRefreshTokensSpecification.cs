using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Auth
{
    public sealed class ActiveRefreshTokensSpecification
        : BaseSpecification<RefreshTokenEntity>
    {
        public ActiveRefreshTokensSpecification()
        {
            Criteria = t =>
                !t.IsRevoked &&
                t.ExpiryDate > DateTime.Now;
        }
    }
}
