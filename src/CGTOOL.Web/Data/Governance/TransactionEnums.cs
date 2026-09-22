namespace CGTOOL.Web.Data.Governance;

public enum TransactionSide
{
    Buy,
    Sell,
    Gift
}

public enum TransactionCategory
{
    PreClearance,
    BlackoutPeriod,
    RestrictedList,
    RelatedParty,
    ThresholdBreach
}

public enum TransactionStatus
{
    Cleared,
    Pending,
    Flagged,
    Escalated
}
