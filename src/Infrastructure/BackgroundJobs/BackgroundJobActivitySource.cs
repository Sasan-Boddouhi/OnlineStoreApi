using System.Diagnostics;

namespace Infrastructure.BackgroundJobs;

public static class BackgroundJobActivitySource
{
    public const string Name = "Infrastructure.BackgroundJobs";

    internal static readonly ActivitySource Instance =
        new(Name, "1.0.0");
}