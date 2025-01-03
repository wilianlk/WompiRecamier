namespace WompiRecamier.Models
{
    public class PaymentInfo
    {
        public string InvoiceNumber { get; set; }
        public decimal NetValue { get; set; }
        public decimal DiscountResult { get; set; }
        public decimal NetValueWithDiscount { get; set; }
        public DateTime InvoiceDate { get; set; }
    }
}