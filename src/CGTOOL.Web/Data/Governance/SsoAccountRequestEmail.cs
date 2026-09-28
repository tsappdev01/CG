namespace CGTOOL.Web.Data.Governance;

/// <summary>The mail asking the service desk to create the single sign-on identity for a person an
/// administrator has just linked a sign-in account to.
///
/// The app creates the *local* account itself -- username, email, Normal User -- but the Microsoft
/// identity behind it lives in the directory, which this app cannot write to. Until that identity
/// exists the person still cannot sign in, and nothing in the app says so, so the click that creates
/// the local account also raises the request for the other half.</summary>
public static class SsoAccountRequestEmail
{
    /// <summary>Where the request goes unless SsoAccountRequests:Recipient says otherwise -- so a
    /// change of service desk is a setting, not a rebuild.</summary>
    public const string DefaultRecipient = "Techdesk@techsource.ae";

    public const string Subject = "Create SSO login for user";

    public static string Recipient(IConfiguration configuration) =>
        configuration["SsoAccountRequests:Recipient"] is { Length: > 0 } configured ? configured : DefaultRecipient;

    public static string BuildHtml(string userEmail, string fullName, string? company, string requestedBy) => $$"""
        <!DOCTYPE html>
        <html>
        <body style="margin:0;padding:0;background-color:#EEF0F3;font-family:Segoe UI,Arial,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#EEF0F3;padding:24px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="background-color:#FFFFFF;border-radius:6px;overflow:hidden;">
                  <tr>
                    <td style="background-color:#0E2A47;padding:24px 32px;">
                      <div style="color:#D9B65A;font-size:16px;font-weight:600;">Corporate Governance Tool</div>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:32px;color:#1B2430;font-size:14px;line-height:1.6;">
                      <p>Please create an SSO login for the user below.</p>
                      <table role="presentation" cellpadding="0" cellspacing="0" style="font-size:14px;line-height:1.6;">
                        <tr>
                          <td style="padding:2px 16px 2px 0;color:#8993A3;">User email</td>
                          <td style="padding:2px 0;">{{Escape(userEmail)}}</td>
                        </tr>
                        <tr>
                          <td style="padding:2px 16px 2px 0;color:#8993A3;">Name</td>
                          <td style="padding:2px 0;">{{Escape(fullName)}}</td>
                        </tr>
                        <tr>
                          <td style="padding:2px 16px 2px 0;color:#8993A3;">Company</td>
                          <td style="padding:2px 0;">{{Escape(string.IsNullOrWhiteSpace(company) ? "—" : company)}}</td>
                        </tr>
                        <tr>
                          <td style="padding:2px 16px 2px 0;color:#8993A3;">Requested by</td>
                          <td style="padding:2px 0;">{{Escape(requestedBy)}}</td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                  <tr>
                    <td style="background-color:#F5F6F8;padding:16px 32px;color:#8993A3;font-size:11px;line-height:1.5;">
                      <strong>Disclaimer:</strong> Please do not reply to this email. The information contained in
                      this message may be CONFIDENTIAL and is for the intended addressee only. Any unauthorized use,
                      dissemination of the information, or copying of this message is prohibited. If you are not the
                      intended addressee, please notify the sender immediately and delete this message.
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    /// <summary>Every value here is typed in by an administrator, so it is escaped rather than trusted
    /// -- a name with an angle bracket in it should read as that name, not as markup.</summary>
    private static string Escape(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
