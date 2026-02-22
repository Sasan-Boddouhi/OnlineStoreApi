using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Auth
{
    public class ActiveUserRefreshTokensSpecification : BaseSpecification<RefreshTokenEntity>
    {
        public ActiveUserRefreshTokensSpecification(int userId)
        {
            Criteria = t => t.UserId == userId && !t.IsRevoked;
        }
    }
}
