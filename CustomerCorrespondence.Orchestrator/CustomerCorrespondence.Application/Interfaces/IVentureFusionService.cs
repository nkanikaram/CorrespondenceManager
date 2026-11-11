using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CustomerCorrespondence.Domain.Entities.DataRecords;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for a service that interacts with Venture Fusion, a third-party system used for managing customer correspondence. This service provides methods for checking the health of the Venture Fusion API, posting data records to Venture Fusion, obtaining and setting bearer tokens for authentication, and mapping correspondence requests to the format required by Venture Fusion. By abstracting these interactions behind an interface, the application can maintain loose coupling and facilitate easier testing and maintenance when integrating with Venture Fusion.
    /// </summary>
    public interface IVentureFusionService
    {
        /// <summary>
        /// Checks the health of the Venture Fusion API.
        /// </summary>
        /// <returns>A task representing the asynchronous operation, containing the HTTP response message.</returns>
        public Task<HttpResponseMessage> CheckHealth();
        /// <summary>
        /// Posts data records to the Venture Fusion API.
        /// </summary>
        public Task<HttpResponseMessage> PostDataRecords(SequentialProcess request);

        /// <summary>
        /// Gets a bearer token for authenticating with the Venture Fusion API. This token is typically required for making authorized requests to the API and may have an expiration time, after which a new token would need to be obtained. The method returns a task that, when completed, provides a VentureFusionBearerToken object containing the token details.
        /// </summary>
        /// <returns>A task representing the asynchronous operation, containing the bearer token.</returns>
        public Task<VentureFusionBearerToken> GetBearerToken();

        /// <summary>
        /// Sets the bearer token on the Venture Fusion client for subsequent API requests. This method is used to ensure that the client is authenticated with the correct token before making any API calls that require authentication. The token is typically obtained using the GetBearerToken method and then passed to this method to configure the client for authorized access to the Venture Fusion API.
        /// </summary>
        /// <param name="token">The bearer token to set on the Venture Fusion client.</param>   
        public void SetBearerTokenOnVentureFusionClient(VentureFusionBearerToken token);

        /// <summary>
        /// maps a SequentialProcess request to a DataRecord format that is compatible with the Venture Fusion API. This method takes a SequentialProcess object, which contains details about the customer correspondence request, and transforms it into a DataRecord object that can be sent to Venture Fusion. The mapping process involves extracting relevant information from the SequentialProcess and organizing it according to the structure expected by Venture Fusion, ensuring that all necessary fields are populated correctly for successful API integration.
        /// </summary>
        /// <param name="request">The SequentialProcess request to map.</param>
        /// <returns>A DataRecord object compatible with the Venture Fusion API.</returns>
        public DataRecord MapCorrespondenceRequestToVentureFusionRequest(SequentialProcess request);

    }
}
