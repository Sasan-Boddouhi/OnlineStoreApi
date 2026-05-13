using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.EmployeeTypes;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class EmployeeTypeService : IEmployeeTypeService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<EmployeeTypeService> _logger;

        public EmployeeTypeService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<EmployeeTypeService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<EmployeeTypeDto> CreateAsync(CreateEmployeeTypeDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating employee type: {TypeName}", dto.TypeName);

            if (string.IsNullOrWhiteSpace(dto.TypeName))
                throw new BusinessException("نوع کارمند الزامی است.");

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var nameExists = await _unitOfWork.Repository<EmployeeType>()
                    .AnyAsync(et => et.TypeName == dto.TypeName, cancellationToken);
                if (nameExists)
                    throw new BusinessException("این نوع کارمند قبلاً ثبت شده است.");

                var entity = _mapper.Map<EmployeeType>(dto);

                await _unitOfWork.Repository<EmployeeType>().AddAsync(entity, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation("Employee type created with ID: {Id}", entity.EmployeeTypeId);
                return _mapper.Map<EmployeeTypeDto>(entity);
            }
            catch (BusinessException)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Failed to create employee type '{TypeName}'", dto.TypeName);
                throw new BusinessException("خطا در ایجاد نوع کارمند", ex);
            }
        }

        public async Task<EmployeeTypeDto?> UpdateAsync(UpdateEmployeeTypeDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Updating employee type ID: {Id}", dto.EmployeeTypeId);

            var entity = await _unitOfWork.Repository<EmployeeType>().GetByIdAsync(dto.EmployeeTypeId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("Employee type not found: {Id}", dto.EmployeeTypeId);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(dto.TypeName) && dto.TypeName != entity.TypeName)
            {
                var nameExists = await _unitOfWork.Repository<EmployeeType>()
                    .AnyAsync(et => et.TypeName == dto.TypeName && et.EmployeeTypeId != entity.EmployeeTypeId, cancellationToken);
                if (nameExists)
                    throw new BusinessException("این نام برای نوع کارمند تکراری است.");
            }

            _mapper.Map(dto, entity);
            _unitOfWork.Repository<EmployeeType>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Employee type updated: {Id}", dto.EmployeeTypeId);
            return _mapper.Map<EmployeeTypeDto>(entity);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Deleting employee type ID: {Id}", id);

            var entity = await _unitOfWork.Repository<EmployeeType>().GetByIdAsync(id, cancellationToken);
            if (entity == null)
                return false;

            // ممکن است نخواهیم نوع کارمندی که در حال استفاده است حذف شود
            var used = await _unitOfWork.Repository<Employee>()
                .AnyAsync(e => e.EmployeeTypeId == id, cancellationToken);
            if (used)
                throw new BusinessException("این نوع کارمند به کارمندانی تخصیص داده شده و قابل حذف نیست.");

            _unitOfWork.Repository<EmployeeType>().Delete(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Employee type deleted: {Id}", id);
            return true;
        }

        public async Task<EmployeeTypeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _unitOfWork.Repository<EmployeeType>().GetByIdAsync(id, cancellationToken);
            return entity == null ? null : _mapper.Map<EmployeeTypeDto>(entity);
        }

        public async Task<PagedResult<EmployeeTypeDto>> GetAllAsync(
            string? filter, string? sort, int pageNumber, int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryContract { Filter = filter, Sort = sort, Page = pageNumber, Size = pageSize };
            var profile = EmployeeTypeQueryProfile.Profile; // QueryProfile<EmployeeType, EmployeeTypeDto>
            var spec = QueryBuilder.BuildFromProfile(profile, query);

            var items = await _unitOfWork.Repository<EmployeeType>()
                .ListAsync(spec, profile.Projection, cancellationToken);

            var countSpec = QueryBuilder.BuildForCount(profile, query.Filter);
            var total = await _unitOfWork.Repository<EmployeeType>()
                .CountAsync(countSpec, cancellationToken);

            return new PagedResult<EmployeeTypeDto>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }
    }
}