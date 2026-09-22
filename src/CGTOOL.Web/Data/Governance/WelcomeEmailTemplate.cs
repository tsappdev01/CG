namespace CGTOOL.Web.Data.Governance;

/// <summary>FRD §2.3 new-user notification email content, shared by every IMemberWelcomeEmailSender
/// implementation regardless of which SMTP transport actually sends it.</summary>
internal static class WelcomeEmailTemplate
{
    public const string DeclarationsLink = "https://cg.dubaiinvestments.com/";

    public const string Subject = "Corporate Governance – Insider Declaration and Related Party & COI Submission Required";

    // Body text follows the confirmed Corporate Affairs template as closely as HTML formatting allows.
    public static string BuildHtml(Member member, string logoTag) => $$"""
        <!DOCTYPE html>
        <html>
        <body style="margin:0;padding:0;background-color:#EEF0F3;font-family:Segoe UI,Arial,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#EEF0F3;padding:24px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="background-color:#FFFFFF;border-radius:6px;overflow:hidden;">
                  <tr>
                    <td style="background-color:#0E2A47;padding:24px 32px;">
                      {{logoTag}}
                      <div style="color:#D9B65A;font-size:16px;font-weight:600;margin-top:8px;">Corporate Governance Tool</div>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:32px;color:#1B2430;font-size:14px;line-height:1.6;">
                      <p>Dear {{member.FullName}},</p>
                      <p>
                        In line with DI's updated Corporate Governance Framework, you are required to submit your
                        Insider Declaration and Related Party &amp; COI.
                      </p>
                      <p>
                        Please submit your declarations using this link
                        (<a href="{{DeclarationsLink}}" style="color:#A8790C;">{{DeclarationsLink}}</a>).
                      </p>
                      <p>
                        Should you have any queries on this matter, please contact the Corporate Affairs Office.
                      </p>
                      <p>Thank you</p>
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
}
