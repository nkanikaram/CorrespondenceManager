using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    public interface ISecretsCacheService
    {
        Task<string> GetSecretAsync(string secretName);
        bool IsSecretCached(string secretName);
        string? GetCachedSecret(string secretName);
        void ClearCache();
        void RemoveSecret(string secretName);
        IEnumerable<string> GetCachedSecretNames();
        void SetSecret(string secretName, string value, TimeSpan duration);

    }
}
