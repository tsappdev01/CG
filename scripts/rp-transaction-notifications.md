# RP Transaction notifications

From **Related Party Register, Dec 2018**, §3.3 "Notifications sent by the System".
Transcribed for the same reason as the [field rules](rp-transaction-field-rules.md):
so it can be searched, diffed and quoted rather than squinted at.

> Auto-generated notifications shall be sent out by the system to users in line with
> workflow in Section 3.1. Each such notification shall contain a common body, and
> notification specific to stage of approval as per workflow.

Section 3.1 is the workflow drawn in
[rp-transaction-workflow.md](rp-transaction-workflow.md).

## 3.3.1 Common body included in all notifications

Every notification carries all of this, whatever stage it is sent from. Nothing here is
typed into the notification — each block is read back from the form that captured it, so
a notification is a view of the transaction as it stands, not a copy made when it was
sent.

| S.no. | Particulars | Source |
| --- | --- | --- |
| | **Details of Requestor** | |
| 1 | Entity | To be auto-filled based on *latest version* of information entered in form *&lt;Recording RP Transaction&gt;* (spans both rows) |
| 2 | User | |
| | **Details of Transaction** | |
| 1 | Counter-party | To be auto-filled based on *latest version* of information entered in form *&lt;Recording RP Transaction&gt;* (spans all four rows) |
| 2 | Transaction Value | |
| 3 | Description of the transaction, including material terms and conditions | |
| 4 | Date of request | |
| | **Details of Approver Action** | |
| 1 | Action *(Approval/Escalation/Rejection)* | To be auto-filled based on *latest version* of information entered in form *&lt;Approver — RP Transaction Approval / Escalation / Rejection&gt;* (spans all three rows) |
| 2 | Remarks | |
| 3 | Date | |
| | **Details of CCAO Action** | |
| 1 | Action *(Approval/Rejection)* | To be auto-filled based on *latest version* of information entered in form *&lt;Chief Corporate Affairs Officer — RP Transaction Approval/Rejection&gt;* (spans all three rows) |
| 2 | Remarks | |
| 3 | Date | |

Two things worth settling:

- The CCAO form is named **&lt;Chief Corporate Affairs Officer — RP Transaction
  Approval/Rejection&gt;** here and **&lt;CCAO — RP Transaction Approval/Rejection&gt;**
  in §3.2 section 4. Same form, two names.
- "Latest version" appears against every block, so a notification sent at stage 1 and
  read at stage 3 shows the stage 3 values. Whether that is intended — a live view
  rather than a record of what was sent — matters for what the emails actually say, and
  for whether they can be re-sent.

The scanned page for this section was not supplied — the table above is the record. The
source continues past it into the stage-specific notifications (§3.3.2 onwards), which
are not yet transcribed here.
