using System.ComponentModel.DataAnnotations;

namespace CustomerCorrespondence.Domain.Entities
{
    public class JourneyDeliveryInfo
    {
        [Required]
        public int AccountId { get; set; }

        [Required]
        public required MessageBody MessageBody { get; set; }

        /// <summary>
        /// Validates that no forbidden special characters exist in string fields.
        /// </summary>
        public void ValidateNoSpecialCharacters()
        {
            MessageBody?.ValidateNoSpecialCharacters();
        }

        /// <summary>
        /// Trims whitespace from all string fields in this object and nested objects.
        /// </summary>
        public void TrimAllStringFields()
        {
            MessageBody?.TrimAllStringFields();
        }
    }

    public class MessageBody
    {
        [Required]
        public required string ContactKey { get; set; }

        [Required]
        public required string EventDefinitionKey { get; set; }

        [Required]
        public required MessageData Data { get; set; }

        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(ContactKey, nameof(ContactKey));
            ProcessRequestHelper.CheckField(EventDefinitionKey, nameof(EventDefinitionKey));
            Data?.ValidateNoSpecialCharacters();
        }

        public void TrimAllStringFields()
        {
            ContactKey = ContactKey?.Trim() ?? string.Empty;
            EventDefinitionKey = EventDefinitionKey?.Trim() ?? string.Empty;
            Data?.TrimAllStringFields();
        }
    }

    public class MessageData
    {
        [Required]
        public required string SubscriberKey { get; set; }

        [EmailAddress]
        public string? EmailAddress { get; set; }

        public string? SourceSystem { get; set; }

        public string? FirstName { get; set; }

        public string? AttachmentName { get; set; }

        public void ValidateNoSpecialCharacters()
        {
            ProcessRequestHelper.CheckField(SubscriberKey, nameof(SubscriberKey));
            ProcessRequestHelper.CheckField(EmailAddress, nameof(EmailAddress));
            ProcessRequestHelper.CheckField(SourceSystem, nameof(SourceSystem));
            ProcessRequestHelper.CheckField(FirstName, nameof(FirstName));
            ProcessRequestHelper.CheckField(AttachmentName, nameof(AttachmentName));
        }
        public void TrimAllStringFields()
        {
            SubscriberKey = SubscriberKey?.Trim() ?? string.Empty;
            EmailAddress = EmailAddress?.Trim();
            SourceSystem = SourceSystem?.Trim();
            FirstName = FirstName?.Trim();
            AttachmentName = AttachmentName?.Trim();
        }
    }
}