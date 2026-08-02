using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Employees;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

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

    #region Create

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating employee for UserId: {UserId}", dto.UserId);
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateEmployeeCreationAsync(dto, cancellationToken);
            var entity = _mapper.Map<Employee>(dto);
            await _unitOfWork.Repository<Employee>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Employee created with ID: {EmployeeId}", entity.EmployeeId);
            return await GetByIdAsync(entity.EmployeeId, cancellationToken)
                   ?? throw new BusinessException("خطا در بازیابی کارمند ایجاد شده", "EMPLOYEE_RETRIEVAL_ERROR");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create employee for UserId: {UserId}", dto.UserId);
            throw new BusinessException("خطا در ایجاد کارمند", "EMPLOYEE_CREATE_ERROR");
        }
    }

    #endregion

    #region Update

    public async Task<EmployeeDto?> UpdateAsync(UpdateEmployeeDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating employee ID: {EmployeeId}", dto.EmployeeId);
        var entity = await _unitOfWork.Repository<Employee>().GetByIdAsync(dto.EmployeeId, cancellationToken);
        if (entity == null)
        {
            _logger.LogWarning("Employee not found: {EmployeeId}", dto.EmployeeId);
            return null;
        }

        if (dto.EmployeeTypeId.HasValue && dto.EmployeeTypeId != entity.EmployeeTypeId)
        {
            var typeExists = await _unitOfWork.Repository<EmployeeType>()
                .AnyAsync(et => et.EmployeeTypeId == dto.EmployeeTypeId.Value, cancellationToken);
            if (!typeExists)
                throw new BusinessException("نوع کارمند انتخاب‌شده وجود ندارد.", "EMPLOYEE_TYPE_NOT_FOUND");
        }

        if (!string.IsNullOrWhiteSpace(dto.EmployeeNumber) && dto.EmployeeNumber != entity.EmployeeNumber)
        {
            var numberExists = await _unitOfWork.Repository<Employee>()
                .AnyAsync(e => e.EmployeeNumber == dto.EmployeeNumber && e.EmployeeId != entity.EmployeeId, cancellationToken);
            if (numberExists)
                throw new BusinessException("این شماره پرسنلی قبلاً ثبت شده است.", "EMPLOYEE_NUMBER_DUPLICATE");
        }

        if (dto.Salary.HasValue && dto.Salary.Value <= 0)
            throw new BusinessException("حقوق باید بزرگتر از صفر باشد.", "SALARY_INVALID");

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<Employee>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Employee updated successfully: {EmployeeId}", dto.EmployeeId);
        return await GetByIdAsync(entity.EmployeeId, cancellationToken);
    }

    #endregion

    #region Delete

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting employee ID: {EmployeeId}", id);
        var employee = await _unitOfWork.Repository<Employee>().GetByIdAsync(id, cancellationToken);
        if (employee == null)
        {
            _logger.LogWarning("Delete failed: employee not found {EmployeeId}", id);
            return false;
        }

        _unitOfWork.Repository<Employee>().Delete(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Employee deleted: {EmployeeId}", id);
        return true;
    }

    #endregion

    #region GetById

    public async Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Employee>();
        spec.Where(e => e.EmployeeId == id);
        return await _unitOfWork.Repository<Employee>()
            .FirstOrDefaultAsync(spec, EmployeeQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region GetByUserId

    public async Task<EmployeeDto?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Employee>();
        spec.Where(e => e.UserId == userId);
        return await _unitOfWork.Repository<Employee>()
            .FirstOrDefaultAsync(spec, EmployeeQueryConfig.Projection, cancellationToken);
    }

    #endregion

    #region Query with QueryContract

    public async Task<PagedResult<EmployeeDto>> GetByQueryAsync(
        QueryContract<Employee> query,
        CancellationToken cancellationToken = default)
    {
        var spec = query.ToSpec();
        var items = await _unitOfWork.Repository<Employee>()
            .ListAsync(spec, EmployeeQueryConfig.Projection, cancellationToken);

        var totalCount = await _unitOfWork.Repository<Employee>()
            .CountAsync(spec, cancellationToken);

        int pageNumber, pageSize;
        if (query.Skip.HasValue || query.Take.HasValue)
        {
            pageSize = query.Take ?? 20;
            var skip = query.Skip ?? 0;
            pageNumber = skip / pageSize + 1;
        }
        else
        {
            pageNumber = query.Page ?? 1;
            pageSize = query.Size ?? 20;
        }

        return new PagedResult<EmployeeDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    #endregion

    #region Validation

    private async Task ValidateEmployeeCreationAsync(CreateEmployeeDto dto, CancellationToken cancellationToken)
    {
        if (dto.Salary <= 0)
            throw new BusinessException("حقوق باید بزرگتر از صفر باشد.", "SALARY_INVALID");
        if (dto.UserId <= 0)
            throw new BusinessException("شناسه کاربر معتبر نیست.", "INVALID_USER_ID");
        if (dto.EmployeeTypeId <= 0)
            throw new BusinessException("نوع کارمند باید انتخاب شود.", "EMPLOYEE_TYPE_REQUIRED");
        if (string.IsNullOrWhiteSpace(dto.EmployeeNumber))
            throw new BusinessException("شماره پرسنلی الزامی است.", "EMPLOYEE_NUMBER_REQUIRED");

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(dto.UserId, cancellationToken);
        if (user == null)
            throw new BusinessException("کاربری با این شناسه یافت نشد.", "USER_NOT_FOUND");
        if (user.UserType != UserType.Employee)
            throw new BusinessException("نوع کاربر باید 'کارمند' باشد.", "USER_NOT_EMPLOYEE_TYPE");

        var empTypeExists = await _unitOfWork.Repository<EmployeeType>()
            .AnyAsync(et => et.EmployeeTypeId == dto.EmployeeTypeId, cancellationToken);
        if (!empTypeExists)
            throw new BusinessException("نوع کارمند انتخاب‌شده وجود ندارد.", "EMPLOYEE_TYPE_NOT_FOUND");

        var empNumberExists = await _unitOfWork.Repository<Employee>()
            .AnyAsync(e => e.EmployeeNumber == dto.EmployeeNumber, cancellationToken);
        if (empNumberExists)
            throw new BusinessException("این شماره پرسنلی قبلاً ثبت شده است.", "EMPLOYEE_NUMBER_DUPLICATE");

        var userAlreadyEmployee = await _unitOfWork.Repository<Employee>()
            .AnyAsync(e => e.UserId == dto.UserId, cancellationToken);
        if (userAlreadyEmployee)
            throw new BusinessException("این کاربر از قبل کارمند است.", "USER_ALREADY_EMPLOYEE");
    }

    #endregion
}