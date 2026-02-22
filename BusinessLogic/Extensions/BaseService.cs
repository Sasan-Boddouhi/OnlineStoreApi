using AutoMapper.QueryableExtensions;
using AutoMapper;
using BusinessLogic.DTOs.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Extensions
{
    public abstract class BaseService
    {
        protected readonly IMapper _mapper;

        protected BaseService(IMapper mapper)
        {
            _mapper = mapper;
        }

        protected async Task<PagedResult<TDto>> GetPagedAsync<TEntity, TDto>(
            IQueryable<TEntity> query,
            int pageNumber,
            int pageSize,
            string? sortBy,
            bool ascending)
            where TEntity : class
            where TDto : class
        {
            return await query
                .ProjectTo<TDto>(_mapper.ConfigurationProvider)
                .ApplySorting(sortBy, ascending)
                .ToPagedResultAsync(pageNumber, pageSize);
        }
    }
}
