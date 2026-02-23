using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Sessions
{
    public sealed class ActiveUserSessionsSpecification
        : BaseSpecification<UserSession>
    {
        public ActiveUserSessionsSpecification(int userId)
        {
            Criteria = x =>
                x.UserId == userId &&
                x.Status == UserSession.SessionStatus.Active;
        }
    }
}
