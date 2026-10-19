namespace Kue.Api.Constants;

/// <summary>
/// Version identifier for the Privacy Policy and Terms of Service that users
/// accept at registration. Stored on <see cref="Entities.User.TermsVersion"/> so
/// there is a record of which text a person agreed to, not just when.
///
/// Bump this whenever the legal documents change materially. It must match
/// <c>LEGAL_VERSION</c> in kue-web/app/features/legal/legal-content.ts; a
/// mismatch makes the backend reject registrations so the web app cannot ship a
/// stale consent screen.
/// </summary>
public static class LegalVersion
{
    public const string Current = "2026-10-19";
}
