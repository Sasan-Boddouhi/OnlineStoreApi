namespace Infrastructure.BackgroundJobs;

public sealed record BackgroundJobHandlerRegistration(
    Type JobType,
    Type HandlerType,
    string JobTypeName);
