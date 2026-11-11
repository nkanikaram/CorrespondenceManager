using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public class VentureFusionServiceFactory
    {
        private readonly IOptionsFactory<VentureFusionServiceCredentials> _optionsFactory;
        private readonly IOptionsFactory<Solutions> _solutionOptionsFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISecretsCacheService _cache;
        public VentureFusionServiceFactory(
            IOptionsFactory<VentureFusionServiceCredentials> optionsFactory,
            IOptionsFactory<Solutions> solutionOptionsFactory,
            IServiceProvider serviceProvider,
            ISecretsCacheService cache)
        {
            _optionsFactory = optionsFactory;
            _solutionOptionsFactory = solutionOptionsFactory;
            _serviceProvider = serviceProvider;
            _cache = cache;
        }
        public IVentureFusionService CreateVentureFusionService()
        {
            // FORCE OPTIONS CREATION WITH SERVICE PROVIDER CONTEXT
            var credentialsOptions = Options.Create(_optionsFactory.Create(Options.DefaultName));
            var solutionOptions = Options.Create(_solutionOptionsFactory.Create(Options.DefaultName));

            if (credentialsOptions.Value == null)
            {
                throw new InvalidOperationException("VentureFusionServiceCredentials options cannot be null.");
            }

            if (solutionOptions.Value == null)
            {
                throw new InvalidOperationException("Solutions options cannot be null.");
            }

            return new VentureFusionService(credentialsOptions, solutionOptions, _cache);
        }
    }
}
