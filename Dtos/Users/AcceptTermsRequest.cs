namespace Kue.Api.Dtos.Users;

/// <summary>
/// Re-acceptance of the current Privacy Policy and Terms of Service, used when
/// the documents change after an account was created.
/// </summary>
public record AcceptTermsRequest(
    bool IsTermsAccepted,

    // Must match Constants/LegalVersion.Current, so a cached client cannot
    // record consent to an older version of the documents.
    string? TermsVersion = null
);
