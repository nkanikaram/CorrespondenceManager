using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Entities
{
    public class SequentialProcess
    {
        //[Required]
        //public Guid CorrespondenceId { get; set; }

        //[StringLength(19, MinimumLength = 19)]
        public string? DocId { get; set; }

        [Required]
        public required string RequestingSystemName { get; set; }

        public string? ECCOTrackingPolicyNumber { get; set; }
        public string? ECCOTrackingClaimNumber { get; set; }
        public string? ECCOTrackingCustomerId { get; set; }

        [Required]
        public required string DeliveryMethod { get; set; }

        public string? DocumentDescription { get; set; }

        [Required]
        public required MailDeliveryInfo MailDeliveryInfo { get; set; }

        public JourneyDeliveryInfo? JourneyDeliveryInfo { get; set; }

        [Required]
        public required CompositionInfo CompositionInfo { get; set; }

        //[Required]
        //public required ArchiveInfo ArchiveInfo { get; set; }
        //ADD NEW PROPERTIES FOR API RESPONSES
        public VentureFusionApiResponse? VentureFusionResponse { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
        
        // Model Context Protocol (MCP) metadata — optional container describing
        // the model/schema/version that applies to this payload.
        public ModelContext? ModelContext { get; set; }

        /// <summary>
        /// Supported delivery methods for correspondence requests.
        /// </summary>
        public static class DeliveryMethods
        {
            public const string Mail = "Mail";
            public const string EPresent = "ePresent";
            public const string EPresentment = "ePresentment"; // Alternative name
        }
        /// <summary>
        /// Checks if the delivery method is ePresent (accepts "ePresent" or "ePresentment", case-insensitive).
        /// </summary>
        public bool IsEPresentDelivery
        {
            get
            {
                var normalized = DeliveryMethod?.Trim();
                return string.Equals(normalized, DeliveryMethods.EPresent, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(normalized, DeliveryMethods.EPresentment, StringComparison.OrdinalIgnoreCase);
            }
        }
        /// <summary>
        /// Checks if the delivery method is Mail.
        /// </summary>
        public bool IsMailDelivery =>
            string.Equals(DeliveryMethod?.Trim(), DeliveryMethods.Mail, StringComparison.OrdinalIgnoreCase);

        public void Validate()
        {
            if (!string.IsNullOrWhiteSpace(DocId) && !System.Text.RegularExpressions.Regex.IsMatch(DocId, "^[0-9]{19}$"))
                throw new ValidationException("DocId must be exactly 19 digits with no spaces.");

            if (string.IsNullOrWhiteSpace(RequestingSystemName))
                throw new ValidationException("RequestingSystemName is required and cannot be null or empty.");

            if (string.IsNullOrWhiteSpace(DeliveryMethod))
                throw new ValidationException("DeliveryMethod is required and cannot be null or empty.");
            
            // Validate delivery method is supported
            if (!IsMailDelivery && !IsEPresentDelivery)
                throw new ValidationException($"DeliveryMethod must be '{DeliveryMethods.Mail}', '{DeliveryMethods.EPresent}', or '{DeliveryMethods.EPresentment}' (case-insensitive).");

            if (MailDeliveryInfo == null)
                throw new ValidationException("MailDeliveryInfo is required and cannot be null.");

            if (CompositionInfo == null)
                throw new ValidationException("CompositionInfo is required and cannot be null.");

            // Validate ePresent-specific requirements
            if (IsEPresentDelivery)
            {
                ValidateEPresentEligibility();
            }
        }

        /// <summary>
        /// Validates that the request payload meets ePresent eligibility requirements.
        /// </summary>
        public void ValidateEPresentEligibility()
        {
            if (JourneyDeliveryInfo == null)
                throw new ValidationException("JourneyDeliveryInfo is required for ePresent delivery.");

            if (JourneyDeliveryInfo.AccountId <= 0)
                throw new ValidationException("JourneyDeliveryInfo.AccountId must be a positive integer for ePresent delivery.");

            if (JourneyDeliveryInfo.MessageBody == null)
                throw new ValidationException("JourneyDeliveryInfo.MessageBody is required for ePresent delivery.");

            if (string.IsNullOrWhiteSpace(JourneyDeliveryInfo.MessageBody.ContactKey))
                throw new ValidationException("JourneyDeliveryInfo.MessageBody.ContactKey is required for ePresent delivery.");

            if (string.IsNullOrWhiteSpace(JourneyDeliveryInfo.MessageBody.EventDefinitionKey))
                throw new ValidationException("JourneyDeliveryInfo.MessageBody.EventDefinitionKey is required for ePresent delivery.");

            if (JourneyDeliveryInfo.MessageBody.Data == null)
                throw new ValidationException("JourneyDeliveryInfo.MessageBody.Data is required for ePresent delivery.");

            if (string.IsNullOrWhiteSpace(JourneyDeliveryInfo.MessageBody.Data.SubscriberKey))
                throw new ValidationException("JourneyDeliveryInfo.MessageBody.Data.SubscriberKey is required for ePresent delivery.");

            // Validate email format if provided
            if (!string.IsNullOrWhiteSpace(JourneyDeliveryInfo.MessageBody.Data.EmailAddress))
            {
                var emailAttribute = new EmailAddressAttribute();
                if (!emailAttribute.IsValid(JourneyDeliveryInfo.MessageBody.Data.EmailAddress))
                    throw new ValidationException("JourneyDeliveryInfo.MessageBody.Data.EmailAddress is not a valid email format.");
            }
        }

        /// <summary>
        /// Validates that no forbidden special characters exist in string fields of this object and all nested objects.
        /// </summary>
        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(RequestingSystemName, nameof(RequestingSystemName));
            ProcessRequestHelper.CheckField(ECCOTrackingPolicyNumber, nameof(ECCOTrackingPolicyNumber));
            ProcessRequestHelper.CheckField(ECCOTrackingClaimNumber, nameof(ECCOTrackingClaimNumber));
            ProcessRequestHelper.CheckField(ECCOTrackingCustomerId, nameof(ECCOTrackingCustomerId));
            ProcessRequestHelper.CheckField(DeliveryMethod, nameof(DeliveryMethod));
            ProcessRequestHelper.CheckField(DocumentDescription, nameof(DocumentDescription));

            MailDeliveryInfo?.ValidateNoSpecialCharacters();
            JourneyDeliveryInfo?.ValidateNoSpecialCharacters();
            CompositionInfo?.ValidateNoSpecialCharacters();

            // Validate model context fields if present
            ModelContext?.ValidateNoSpecialCharacters();

            if (AdditionalData != null)
            {
                foreach (var kvp in AdditionalData)
                {
                    // When deserialized from JSON via System.Text.Json, values arrive as JsonElement
                    // not as string - so we must handle both cases
                    if (kvp.Value is string strValue)
                    {
                        ProcessRequestHelper.CheckField(strValue, $"AdditionalData[{kvp.Key}]");
                    }
                    else if (kvp.Value is System.Text.Json.JsonElement jsonElement &&
                             jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        ProcessRequestHelper.CheckField(jsonElement.GetString(), $"AdditionalData[{kvp.Key}]");
                    }
                }
            }
        }

        /// <summary>
        /// Trims whitespace from all string fields in this object and all nested objects.
        /// </summary>
        public void TrimAllStringFields()
        {
            DocId = DocId?.Replace(" ", "").Trim();
            RequestingSystemName = RequestingSystemName?.Trim() ?? string.Empty;
            ECCOTrackingPolicyNumber = ECCOTrackingPolicyNumber?.Trim();
            ECCOTrackingClaimNumber = ECCOTrackingClaimNumber?.Trim();
            ECCOTrackingCustomerId = ECCOTrackingCustomerId?.Trim();
            DeliveryMethod = DeliveryMethod?.Trim() ?? string.Empty;
            DocumentDescription = DocumentDescription?.Trim();

            MailDeliveryInfo?.TrimAllStringFields();
            JourneyDeliveryInfo?.TrimAllStringFields();
            CompositionInfo?.TrimAllStringFields();

            // Trim ModelContext fields if present
            ModelContext?.TrimAllStringFields();

            if (AdditionalData != null)
            {
                foreach (var key in AdditionalData.Keys.ToList())
                {
                    // When deserialized from JSON via System.Text.Json, values arrive as JsonElement
                    // not as string - so we must handle both cases
                    if (AdditionalData[key] is string strValue)
                    {
                        AdditionalData[key] = strValue.Trim();
                    }
                    else if (AdditionalData[key] is System.Text.Json.JsonElement jsonElement &&
                             jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        AdditionalData[key] = jsonElement.GetString()?.Trim() ?? string.Empty;
                    }
                }
            }
        }
    }
    
    //CREATE NEW RESPONSE DATA STRUCTURE
    public class VentureFusionApiResponse
    {
        public System.Net.HttpStatusCode StatusCode { get; set; }
        public string? ResponseContent { get; set; }
        public DateTime ResponseTimestamp { get; set; }
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, string>? ResponseHeaders { get; set; }
        public string? VFResponseId { get; set; }

    }
}
