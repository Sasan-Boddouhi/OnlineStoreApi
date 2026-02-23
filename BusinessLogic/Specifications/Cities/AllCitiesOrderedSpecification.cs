using Application.Common.Specifications;
using Application.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Specifications.Cities
{
    public sealed class AllCitiesOrderedSpecification : BaseSpecification<City>
    {
        public AllCitiesOrderedSpecification()
        {
            ApplyOrderBy(c => c.CityName);
        }
    }
}
