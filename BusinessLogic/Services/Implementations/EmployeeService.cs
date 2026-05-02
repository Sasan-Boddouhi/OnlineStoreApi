using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class EmployeeService : IEmployeeService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<EmployeeService> _logger;

        public EmployeeService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<EmployeeService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Creating employee for UserId: {UserId}", dto.UserId);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await ValidateEmployeeCreationAsync(dto, cancellationToken);

                var entity = _mapper.Map<Employee>(dto);
                // اگر در `AuditableEntity` فیلدی مثل IsActive ندارید، می‌توانید پیش‌فرض بگذارید
                // entity.IsActive = true; // در صورت وجود

                await _unitOfWork.Repository<Employee>().AddAsync(entity, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation("Employee created with ID: {EmployeeId}", entity.EmployeeId);
                return _mapper.Map<EmployeeDto>(entity);
            }
            catch (BusinessException)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Failed to create employee for UserId: {UserId}", dto.UserId);
                throw new BusinessException("خطا در ایجاد کارمند", ex);
            }
        }

        public async Task<EmployeeDto?> UpdateAsync(UpdateEmployeeDto dto, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Updating employee ID: {EmployeeId}", dto.EmployeeId);

            var entity = await _unitOfWork.Repository<Employee>().GetByIdAsync(dto.EmployeeId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("Employee not found: {EmployeeId}", dto.EmployeeId);
                return null;
            }

            if (dto.EmployeeTypeId.HasValue)
            {
                var typeExists = await _unitOfWork.Repository<EmployeeType>()
                    .AnyAsync(et => et.EmployeeTypeId == dto.EmployeeTypeId.Value, cancellationToken);
                if (!typeExists)
                    throw new BusinessException("نوع کارمند انتخاب‌شده وجود ندارد.");
            }

            if (!string.IsNullOrWhiteSpace(dto.EmployeeNumber) && dto.EmployeeNumber != entity.EmployeeNumber)
            {
                var numberExists = await _unitOfWork.Repository<Employee>()
                    .AnyAsync(e => e.EmployeeNumber == dto.EmployeeNumber && e.EmployeeId != entity.EmployeeId, cancellationToken);
                if (numberExists)
                    throw new BusinessException("این شماره پرسنلی قبلاً ثبت شده است.");
            }

            if (dto.Salary.HasValue && dto.Salary.Value <= 0)
                throw new BusinessException("حقوق باید بزرگتر از صفر باشد.");

            _mapper.Map(dto, entity);
            _unitOfWork.Repository<Employee>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Employee updated successfully: {EmployeeId}", dto.EmployeeId);
            return _mapper.Map<EmployeeDto>(entity);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Deleting employee ID: {EmployeeId}", id);

            var employee = await _unitOfWork.Repository<Employee>().GetByIdAsync(id, cancellationToken);
            if (employee == null)
            {
                _logger.LogWarning("Delete failed: employee not found {EmployeeId}", id);
                return false;
            }

            // در صورت تمایل به SoftDelete مشروط بر وجود فیلد IsActive (اینجا حذف سخت)
            _unitOfWork.Repository<Employee>().Delete(employee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Employee deleted: {EmployeeId}", id);
            return true;
        }

        public async Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Get employee by ID: {EmployeeId}", id);

            var employee = await _unitOfWork.Repository<Employee>()
                .GetByIdAsync(id, cancellationToken);

            return employee == null ? null : _mapper.Map<EmployeeDto>(employee);
        }

        public async Task<EmployeeDto?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            var filter = $"UserId eq {userId}";
            var spec = new QuerySpecification<Employee, EmployeeDto>(
                filter,
                sort: null,
                skip: null,
                take: null,
                projection: GetEmployeeProjection(),
                allowedFields: AllowedEmployeeFields()
            );

            return await _unitOfWork.Repository<Employee>()
                .FirstOrDefaultAsync<EmployeeDto>(spec, cancellationToken);
        }

        public async Task<PagedResult<EmployeeDto>> GetAllAsync(
            string? filter,
            string? sort,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Fetching employees page {Page}", pageNumber);

            var spec = new QuerySpecification<Employee, EmployeeDto>(
                filter,
                sort,
                (pageNumber - 1) * pageSize,
                pageSize,
                GetEmployeeProjection(),
                AllowedEmployeeFields());

            var countSpec = new QueryCountSpecification<Employee>(filter, AllowedEmployeeFields());

            var items = await _unitOfWork.Repository<Employee>()
                .ListAsync<EmployeeDto>(spec, cancellationToken);
            var total = await _unitOfWork.Repository<Employee>()
                .CountAsync(countSpec, cancellationToken);

            return new PagedResult<EmployeeDto>
            {
                Items = items,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        #region Helpers

        private async Task ValidateEmployeeCreationAsync(CreateEmployeeDto dto, CancellationToken cancellationToken)
        {
            if (dto.Salary <= 0)
                throw new BusinessException("حقوق باید بزرگتر از صفر باشد.");

            if (dto.UserId <= 0)
                throw new BusinessException("شناسه کاربر معتبر نیست.");

            if (dto.EmployeeTypeId <= 0)
                throw new BusinessException("نوع کارمند باید انتخاب شود.");

            if (string.IsNullOrWhiteSpace(dto.EmployeeNumber))
                throw new BusinessException("شماره پرسنلی الزامی است.");

            // 1. کاربر باید وجود داشته باشد و UserType == Employee (2)
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(dto.UserId, cancellationToken);
            if (user == null)
                throw new BusinessException("کاربری با این شناسه یافت نشد.");
            if (user.UserType != UserType.Employee)
                throw new BusinessException("نوع کاربر باید 'کارمند' باشد.");

            // 2. نوع کارمند باید معتبر باشد
            var empTypeExists = await _unitOfWork.Repository<EmployeeType>()
                .AnyAsync(et => et.EmployeeTypeId == dto.EmployeeTypeId, cancellationToken);
            if (!empTypeExists)
                throw new BusinessException("نوع کارمند انتخاب‌شده وجود ندارد.");

            // 3. شماره پرسنلی یکتا باشد
            var empNumberExists = await _unitOfWork.Repository<Employee>()
                .AnyAsync(e => e.EmployeeNumber == dto.EmployeeNumber, cancellationToken);
            if (empNumberExists)
                throw new BusinessException("این شماره پرسنلی قبلاً ثبت شده است.");

            // 4. یک کاربر فقط یک Employee دارد (به‌دلیل رابطه 1:1)
            var userAlreadyEmployee = await _unitOfWork.Repository<Employee>()
                .AnyAsync(e => e.UserId == dto.UserId, cancellationToken);
            if (userAlreadyEmployee)
                throw new BusinessException("این کاربر از قبل کارمند است.");
        }

        private static System.Linq.Expressions.Expression<System.Func<Employee, EmployeeDto>> GetEmployeeProjection()
        {
            return e => new EmployeeDto
            {
                EmployeeId = e.EmployeeId,
                UserId = e.UserId,
                UserFullName = e.User.FullName,
                PhoneNumber = e.User.PhoneNumber,
                EmployeeTypeId = e.EmployeeTypeId,
                EmployeeTypeName = e.EmployeeType.TypeName,
                EmployeeNumber = e.EmployeeNumber,
                HireDate = e.HireDate,
                TerminationDate = e.TerminationDate,
                Salary = e.Salary,
                IsActive = true // یا بر اساس فیلد واقعی
            };
        }

        private static string[] AllowedEmployeeFields() => new[]
        {
            "EmployeeId", "UserId", "EmployeeTypeId", "EmployeeNumber",
            "HireDate", "TerminationDate", "Salary"
        };

        #endregion
    }
}