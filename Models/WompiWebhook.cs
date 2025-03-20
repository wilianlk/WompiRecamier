using System.Text.Json.Serialization;

namespace WompiRecamier.Models
{
    public class WompiWebhook
    {
        public string Event { get; set; }
        public DataWrapper Data { get; set; }
        public string SentAt { get; set; }
        public long Timestamp { get; set; }
        public Signature Signature { get; set; }
        public string Environment { get; set; }
    }

    public class DataWrapper
    {
        public Transaction Transaction { get; set; }
    }

    public class Transaction
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string FinalizedAt { get; set; }
        public long AmountInCents { get; set; }
        public string Reference { get; set; }
        public string CustomerEmail { get; set; }
        public string Currency { get; set; }

        [JsonPropertyName("payment_method")]
        public PaymentMethod PaymentMethod { get; set; }
        public string Status { get; set; }
        public string StatusMessage { get; set; }
        public CustomerData CustomerData { get; set; }
    }

    public class PaymentMethod
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        public Extra Extra { get; set; }
        public string PhoneNumber { get; set; }
    }

    public class Extra
    {
        public bool IsThreeDs { get; set; }
        public string TransactionId { get; set; }
        public string ThreeDsAuthType { get; set; }
        public string ExternalIdentifier { get; set; }
    }

    public class CustomerData
    {
        public string DeviceId { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }

    public class Signature
    {
        public string Checksum { get; set; }
        public string[] Properties { get; set; }
    }
}
