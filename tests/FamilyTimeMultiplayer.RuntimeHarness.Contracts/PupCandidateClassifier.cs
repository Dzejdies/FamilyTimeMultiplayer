namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public static class PupCandidateClassifier
{
    public static bool ShouldReport(bool isChild, bool declaredPup, bool nameLooksLikePup, bool isPup)
    {
        return isChild || declaredPup || nameLooksLikePup || isPup;
    }

    public static string RejectionReason(string eligibility) => eligibility switch
    {
        "false" => "mod IsPup returned false",
        "not-evaluated" => "mod IsPup could not be evaluated for this instance",
        "result-unknown" => "mod IsPup returned a non-boolean result",
        _ when eligibility.StartsWith("not-evaluated:", System.StringComparison.Ordinal) => eligibility,
        _ when eligibility.StartsWith("error:", System.StringComparison.Ordinal) => eligibility,
        _ => "no rejection observed"
    };
}
