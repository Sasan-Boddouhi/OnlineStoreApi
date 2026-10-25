using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.BackgroundJobs;

internal sealed class BackgroundJobRegistrationValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<BackgroundJobHandlerRegistration> _registrations;

    public BackgroundJobRegistrationValidator(
        IServiceProvider serviceProvider,
        IEnumerable<BackgroundJobHandlerRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        _registrations = registrations.ToArray();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_registrations.Count == 0)
            throw new InvalidOperationException(
                "At least one background job handler must be registered.");

        var duplicate = _registrations
            .GroupBy(x => x.JobType)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Multiple background job handlers are registered for '{duplicate.Key.FullName}'.");

        using var scope = _serviceProvider.CreateScope();

        foreach (var registration in _registrations)
        {
            var serviceType = typeof(Application.BackgroundJobs.IBackgroundJobHandler<>)
                .MakeGenericType(registration.JobType);

            if (scope.ServiceProvider.GetServices(serviceType).Count() != 1)
                throw new InvalidOperationException(
                    $"Background job handler registration is invalid for '{registration.JobTypeName}'.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
