namespace CustomerCorrespondence.Domain.Entities
{
    public class NonRecoverableErrorLog
    {
        public string NonRecoverableErrorLogId { get; set; } = string.Empty;
        //public Guid CorrespondenceId { get; set; }
        //public string? MessageType { get; set; }
        //public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public DateTime ErrorTimestamp { get; set; }
       // public string? AdditionalInfo { get; set; }

    }
}