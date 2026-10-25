using System.ComponentModel.DataAnnotations;

namespace Application.Options;

public sealed class OtelOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; } = true;

    [Required]
    public string ServiceName { get; set; } = "OnlineStoreApi";

    [Required]
    public string ServiceVersion { get; set; } = "1.0.0";

    [Required]
    public string Endpoint { get; set; } = "http://otel-collector:4317";

    [Range(0.0, 1.0)]
    public double TraceSamplingRatio { get; set; } = 1.0;

    public bool IncludeEfCore { get; set; } = true;

    public bool IncludeRedis { get; set; } = true;

    [Range(1000, 30000)]
    public int ExportTimeoutMs { get; set; } = 5000;

    [Range(1, 8192)]
    public int MaxBatchSize { get; set; } = 2048;

    /// <summary>
    /// Prometheus exposition port on the OTel Collector.
    /// Prometheus scrapes this port.
    /// </summary>
    [Range(1024, 65535)]
    public int PrometheusExporterPort { get; set; } = 8889;
}
