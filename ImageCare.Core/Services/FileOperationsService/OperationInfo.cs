namespace ImageCare.Core.Services.FileOperationsService;

public sealed class OperationInfo
{
    public OperationInfo(DateTime startedTimestamp, long bytesTransfered)
    {
        BytesTransferred = bytesTransfered;
        BytesPerSecond = BytesTransferred / DateTime.Now.Subtract(startedTimestamp).TotalSeconds;
    }

    public long Total { get; set; }

    public long Transferred { get; set; }

    public long BytesTransferred { get; set; }

    public long StreamSize { get; set; }

    public string ProcessedFile { get; set; }

    public double BytesPerSecond { get; }

    public double Fraction => BytesTransferred / (double)Total;

    public double Percentage => 100.0 * Fraction;
}