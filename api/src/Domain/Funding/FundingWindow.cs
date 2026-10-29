using Domain.Common;

namespace Domain.Funding;

public class FundingWindow : IEntity<FundingWindowId>
{
    public FundingWindowId Id { get; private set; }
    public string FinancialYear { get; private set; }
    public string WindowLabel { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private FundingWindow(FundingWindowId id, string financialYear, string windowLabel, DateTime createdAt)
    {
        Id = id;
        FinancialYear = financialYear;
        WindowLabel = windowLabel;
        CreatedAt = createdAt;
    }

    public static FundingWindow New(FundingWindowId id, string financialYear, string windowLabel, DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNullOrEmptyOrWhiteSpace(financialYear, nameof(financialYear));
        Guard.NotNullOrEmptyOrWhiteSpace(windowLabel, nameof(windowLabel));

        return new FundingWindow(id, financialYear, windowLabel, utcNow);
    }
}