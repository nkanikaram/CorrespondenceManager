using ECMServicesDemo;
using System.Threading.Tasks;

namespace DocumentStatusSyncFunction.Services
{
    public interface IECMServiceClient
    {
        Task<string> getTokenAsync(string repository, string superuser, string password);
        Task<string> execDQLAsync(DocumentumSecurityToken token, string user, string repo, string query);
    }
}