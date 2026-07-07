namespace SearchGateway.Models;

public class SyncResponse
{
    public required string Message { get; set; }
    public long TookMs { get; set; }
    public bool HasErrors { get; set; }
}
