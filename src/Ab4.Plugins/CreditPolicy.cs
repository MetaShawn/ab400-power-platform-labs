namespace Ab4.Plugins
{
    // Shared by the W02 custom API plug-ins.
    // A custom API main-operation plug-in can't receive secure or unsecure configuration,
    // so the ceiling is a constant here. W11 moves it to an environment variable.
    // Kept below the W01 CreditLimitGuard ceiling (150,000) so the guard never blocks an auto-approval.
    internal static class CreditPolicy
    {
        internal const decimal AutoApproveCeiling = 100000m;

        internal const string RequestMessage = "ab4_RequestCreditIncrease";
        internal const string HeadroomMessage = "ab4_GetCreditHeadroom";
        internal const string EventMessage = "ab4_OnCreditIncreaseRequested";
    }
}