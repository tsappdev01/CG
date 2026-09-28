# RP Transaction field rules

From **Related Party Register, Dec 2018**, §3.2 "Field Rules" — the field-by-field
specification behind the RP Transaction screens. Transcribed rather than left as page
images so it can be searched, diffed and quoted in review; the scanned pages are kept
beside it where they were supplied.

Transcribed verbatim, including where the source contradicts itself — see the note
under section 2.

| Marker | Meaning |
| --- | --- |
| ✓ | Mandatory |
| ☐ | Optional |
| **bold row** | Button |

## 1. User — Recording RP Transaction

![Field rules, section 1](rp-transaction-field-rules-1-user-recording.png)

| | Field | Field rules | Field type | Dependency | Remarks |
| --- | --- | --- | --- | --- | --- |
| | *Details of Requestor:* | | | | |
| ✓ | Entity | Auto-filled; non-editable | | Login credentials of User | To be auto-filled based on details of user accessing the system |
| ✓ | User | Auto-filled; non-editable | | Login credentials of User | To be auto-filled based on details of user accessing the system |
| | *Details of Transaction:* | | | | |
| ✓ | Name of the counter-party | Editable | Drop-down | | *Related Party Master* |
| ✓ | Transaction Value | Editable | Numeric | | Value to be greater than 0 |
| ✓ | Description of the transaction, including material terms and conditions | Editable | Text | | |
| ✓ | Date of request | Auto-filled; non-editable | Date | | Capture system date |
| | **Submit** | | Clickable button | Clickable only after all mandatory fields have been filled up | System to show up error notification if mandatory fields have not been filled up |

The counter-party dropdown is specified to come from the **Related Party Master**. In
the app today it is fed by `RelatedPartyMasterSource`, which reads the names out of
submitted RP & COI declarations — not from the standing master that
`/reports/related-party-master` shows. The two answer different questions, so which one
this dropdown should offer is worth settling.

## 2. Approver — RP Transaction Approval / Escalation / Rejection

| | Field | Field rules | Field type | Dependency | Remarks |
| --- | --- | --- | --- | --- | --- |
| | *Details of Requestor:* | | | | |
| ✓ | Entity | Auto-filled; non-editable | | | To be auto-filled based on *latest version* of information entered in form *&lt;Recording RP Transaction&gt;* (this remark spans every row from Entity to Date of request) |
| ✓ | User | Auto-filled; non-editable | | | |
| | *Details of Transaction:* | | | | |
| ✓ | Name of the counter-party | Auto-filled; non-editable | | | |
| ✓ | Transaction Value | Auto-filled; non-editable | | | |
| ✓ | Description of the transaction, including material terms and conditions | Auto-filled; non-editable | | | |
| ✓ | Date of request | Auto-filled; non-editable | | | |
| | *Approver Action:* | | | | |
| ✓ | Remarks | Editable | Text | | Free text field |
| ✓ | Date | Auto-filled; non-editable | Date | | Capture system date |
| | **Approve** | | Clickable buttons | One of the three buttons clickable only after all mandatory fields have been filled up | System to show up error notification if mandatory fields have not been filled up. Either one of the two buttons may be clicked. |
| | **Escalate** | | | | |
| | **Reject** | | | | |

The source lists three buttons and its own remark then says "either one of the **two**
buttons may be clicked". Left as written; someone should say which is right.

The scanned page for this section was not supplied — the table above is the record.

## 3. RP Transaction Approval / Rejection based on MD&CEO / AC / Board / GA feedback

The CCAO recording the outcome of the offline decision — stage 2 of
[the workflow](rp-transaction-workflow.md). The source prints this heading without a
number; it is numbered 3 here for ordering. Section 4 refers back to this form as
*&lt;CCAO — RP Transaction Approval/Rejection&gt;*, which is its name — though §3.3 calls
the same form *&lt;Chief Corporate Affairs Officer — RP Transaction Approval/Rejection&gt;*.

![Field rules, section 3](rp-transaction-field-rules-3-ccao-recording-feedback.png)

| | Field | Field rules | Field type | Dependency | Remarks |
| --- | --- | --- | --- | --- | --- |
| | *Details of Requestor:* | | | | |
| ✓ | Entity | Auto-filled; non-editable | | | To be auto-filled based on *latest version* of information entered in form *&lt;Recording RP Transaction&gt;* (this remark spans every row from Entity to Date of request) |
| ✓ | User | Auto-filled; non-editable | | | |
| | *Details of Transaction:* | | | | |
| ✓ | Name of the counter-party | Auto-filled; non-editable | | | |
| ✓ | Transaction Value | Auto-filled; non-editable | | | |
| ✓ | Description of the transaction, including material terms and conditions | Auto-filled; non-editable | | | |
| ✓ | Date of request | Auto-filled; non-editable | | | |
| | *Approver Action:* | | | | |
| ✓ | Action *(Approval/Escalation)* | Auto-filled; non-editable | | | To be auto-filled based on information entered in form *&lt;Approver — RP Transaction Approval / Escalation / Rejection&gt;* (this remark spans Action, Date and Remarks) |
| ✓ | Date | Auto-filled; non-editable | | | |
| ✓ | Remarks | Auto-filled; non-editable | | | |
| | *CCAO Action:* | | | | |
| ✓ | Remarks | Editable | Text | | Free text field |
| ✓ | Date | Auto-filled; non-editable | Date | | Capture system date |
| | **Approve\*** | | Clickable buttons | One of the three buttons clickable only after all mandatory fields have been filled up | System to show up error notification if mandatory fields have not been filled up. Either one… |
| | **Reject** | | | | |

**The supplied page is cut off at the bottom.** Three things are below the fold and are
not recorded here: the third button (the dependency says "one of the three buttons", and
only Approve and Reject are visible), whatever footnote the asterisk on **Approve\***
points at, and the end of the remark, which breaks off at "Either one…" — presumably the
same "either one of the two buttons may be clicked" as section 2, but that is a guess
rather than a transcription. Worth re-supplying the full page.

## 4. RP Transaction Release to RP Register

The last stage: everything decided upstream is shown read-only, the CCAO confirms the
paperwork exists, and the transaction goes onto the register.

![Field rules, section 4](rp-transaction-field-rules-4-release-to-register.png)

| | Field | Field rules | Field type | Dependency | Remarks |
| --- | --- | --- | --- | --- | --- |
| | *Details of Requestor:* | | | | |
| ✓ | Entity | Auto-filled; non-editable | | | To be auto-filled based on *latest version* of information entered in form *&lt;Recording RP Transaction&gt;* (this remark spans every row from Entity to Date of request) |
| ✓ | User | Auto-filled; non-editable | | | |
| | *Details of Transaction:* | | | | |
| ✓ | Name of the counter-party | Auto-filled; non-editable | | | |
| ✓ | Transaction Value | Auto-filled; non-editable | | | |
| ✓ | Description of the transaction, including material terms and conditions | Auto-filled; non-editable | | | |
| ✓ | Date of request | Auto-filled; non-editable | | | |
| | *Approver Action:* | | | | |
| ✓ | Action *(Approval/Escalation)* | Auto-filled; non-editable | | | To be auto-filled based on information entered in form *&lt;Approver — RP Transaction Approval / Escalation / Rejection&gt;* (this remark spans Action, Remarks and Date) |
| ✓ | Remarks | Auto-filled; non-editable | | | |
| ✓ | Date | Auto-filled; non-editable | | | |
| | *CCAO Action:* | | | | |
| ✓ | Action *(Approval)* | Auto-filled; non-editable | | | To be auto-filled based on information entered in form *&lt;CCAO — RP Transaction Approval/Rejection&gt;* (this remark spans Action, Remarks and Date) |
| ✓ | Remarks | Auto-filled; non-editable | | | |
| ✓ | Date | Auto-filled; non-editable | | | |
| ✓ | All documentation in respect of approvals obtained from AC/Board/GM is in place for proposed RP transaction | Checkbox; editable | Checkbox | | |
| | **Release to RP Register** | | Clickable button | Clickable only after checkbox has been checked | System to show up error notification if checkbox has not been checked |

The documentation checkbox is the only thing on this screen anyone can change, and the
release button is gated on it alone -- not on the mandatory fields, as the earlier
screens are. That single tick is what the whole stage turns on.
