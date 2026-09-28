namespace Application.BackgroundJobs;

public sealed class NonRetryableJobException : Exception
{
    public NonRetryableJobException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
