using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Services
{
    [Serializable]
    public class PermanentFailureException : Exception
    {
        private string? v;
        private HttpRequestException? ex;
        private object? externalServiceError;

        public PermanentFailureException()
        {
        }

        public PermanentFailureException(string? message) : base(message)
        {
        }

        public PermanentFailureException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public PermanentFailureException(string v, HttpRequestException ex, object externalServiceError)
        {
            this.v = v;
            this.ex = ex;
            this.externalServiceError = externalServiceError;
        }
    }
}
