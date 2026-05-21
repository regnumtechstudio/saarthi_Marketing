namespace regnumdigitalappApi.Model.modelObject
{
    public class RequestDTO
    {
        public record LoginRequest(string mobile, string password);
        public record OtpRequest(string mobile);
        public record VerifyOtpRequest(string mobile, string otp);
        public record RegisterRequest(string firstName, string lastName, string email, string mobile, string pan, string password, string? partnerCode);
        public record PurchaseRequest(long schemeId, decimal amount, string paymentMode, long? bankAccountId);
        public record RedemptionRequest(long schemeId, string? folioNumber, decimal? units, decimal? amount, bool isFullRedeem);
        public record SipCreateRequest(long schemeId, decimal amount, string frequency, int sipDate, DateTime startDate, DateTime? endDate);
        public record PauseRequest(string reason);
        public record OnboardClientRequest(string firstName, string lastName, string mobile, string email, string pan, string? partnerCode);
        public record RiskAnswers(int q1, int q2, int q3, int q4, int q5);
        public record CreateClientRequest(string firstName, string lastName, string email, string mobile, string pan, string? partnerCode);
        public record AddPartnerRequest(string firstName, string lastName, string email, string mobile, string arn, string? euin, string tier, string city, string state);
        public record ApproveRequest(string? Remarks);
        public record RejectRequest(string Reason);
        public class UserRow
        {
            public long Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string PasswordHash { get; set; } = "";
            public int RoleId { get; set; }
            public string? Pan { get; set; }
        }
    }
}
