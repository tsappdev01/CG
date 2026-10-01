const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType, PageBreak,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle,
  LevelFormat, convertInchesToTwip, TableOfContents, Footer, PageNumber,
} = require('docx');
const fs = require('fs');

const NAVY = '12325C', GOLD = 'D8A62A', GREY = 'F2F4F7', INK = '16202E', MUTED = '56606B';
const FONT = 'Arial';
const W = 9026; // content width in DXA for A4 with 1" margins

const P = (text, o = {}) => new Paragraph({
  spacing: { after: o.after ?? 120, before: o.before ?? 0 },
  alignment: o.align,
  children: [new TextRun({ text, font: FONT, size: o.size ?? 21, bold: o.bold, italics: o.italics, color: o.color ?? INK })],
});

const Rich = (runs, o = {}) => new Paragraph({
  spacing: { after: o.after ?? 120, before: o.before ?? 0 },
  children: runs.map(r => new TextRun({ font: FONT, size: 21, color: INK, ...r })),
});

const H1 = t => new Paragraph({
  heading: HeadingLevel.HEADING_1, spacing: { before: 360, after: 160 },
  children: [new TextRun({ text: t, font: FONT, size: 30, bold: true, color: NAVY })],
});
const H2 = t => new Paragraph({
  heading: HeadingLevel.HEADING_2, spacing: { before: 260, after: 120 },
  children: [new TextRun({ text: t, font: FONT, size: 24, bold: true, color: NAVY })],
});
const H3 = t => new Paragraph({
  heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 100 },
  children: [new TextRun({ text: t, font: FONT, size: 22, bold: true, color: INK })],
});

const Bullet = t => new Paragraph({
  numbering: { reference: 'bullets', level: 0 }, spacing: { after: 80 },
  children: [new TextRun({ text: t, font: FONT, size: 21, color: INK })],
});
const Step = (t, inst = 0) => new Paragraph({
  numbering: { reference: 'steps', level: 0, instance: inst }, spacing: { after: 80 },
  children: [new TextRun({ text: t, font: FONT, size: 21, color: INK })],
});

const Note = t => new Paragraph({
  spacing: { before: 120, after: 160 },
  shading: { type: ShadingType.CLEAR, fill: GREY },
  border: { left: { style: BorderStyle.SINGLE, size: 18, color: GOLD, space: 8 } },
  indent: { left: 120, right: 120 },
  children: [new TextRun({ text: t, font: FONT, size: 20, italics: true, color: MUTED })],
});

function makeTable(headers, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  const cols = widths.map(w => Math.round(w / total * W));
  const cell = (text, opts = {}) => new TableCell({
    width: { size: cols[opts.i], type: WidthType.DXA },
    shading: opts.header ? { type: ShadingType.CLEAR, fill: NAVY } : undefined,
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: [new Paragraph({
      spacing: { after: 0 },
      children: [new TextRun({
        text, font: FONT, size: 19,
        bold: opts.header, color: opts.header ? 'FFFFFF' : INK,
      })],
    })],
  });
  return new Table({
    width: { size: W, type: WidthType.DXA },
    columnWidths: cols,
    rows: [
      new TableRow({ tableHeader: true, children: headers.map((h, i) => cell(h, { header: true, i })) }),
      ...rows.map(r => new TableRow({ children: r.map((c, i) => cell(c, { i })) })),
    ],
  });
}

const children = [];

// ── Cover ──
children.push(
  new Paragraph({ spacing: { before: 2200, after: 0 },
    children: [new TextRun({ text: 'Dubai Investments PJSC', font: FONT, size: 24, color: GOLD, bold: true })] }),
  new Paragraph({ spacing: { before: 60, after: 0 },
    children: [new TextRun({ text: 'Corporate Governance Tool', font: FONT, size: 56, bold: true, color: NAVY })] }),
  new Paragraph({ spacing: { before: 100, after: 400 },
    border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: GOLD, space: 6 } },
    children: [new TextRun({ text: 'User Guide', font: FONT, size: 40, color: INK })] }),
  P('For everyone who files a declaration, raises a related party transaction, or approves one.', { size: 22, color: MUTED }),
  P('Version 1.0  ·  October 2026', { size: 20, color: MUTED, before: 240 }),
  new Paragraph({ children: [new PageBreak()] }),
);

// ── Contents ──
children.push(
  H1('Contents'),
  new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }),
  new Paragraph({ children: [new PageBreak()] }),
);

// ── 1 About ──
children.push(
  H1('1. About this guide'),
  P('The Corporate Governance Tool — CGTOOL — is where Dubai Investments records the things the group has to be able to prove: who is related to whom, who holds shares, who declared a conflict of interest, and which transactions with related parties were approved and by whom.'),
  P('This guide covers what you do in it. It does not cover the administrator screens — setting up declaration cycles, managing entities and users, the audit log — which are described separately.'),
  H2('Who does what'),
  makeTable(
    ['If you are', 'You will mainly use'],
    [
      ['Everyone', 'My Register, and Declarations when a period opens'],
      ['Anyone raising a transaction', 'RP Transactions → Add New Transaction, and My Transactions'],
      ['An approver', 'RP Transactions → Pending My Approval'],
      ['The CCAO', 'RP Transactions → CCAO Review, and the Register'],
      ['Compliance', 'Reports'],
    ],
    [30, 70]),
  Note('A note on the word “you”. Where someone is approved to act on your behalf, they see your screens and file for you. Anything they submit is recorded as filed by them, on your behalf — both names are kept.'),
);

// ── 2 Signing in ──
children.push(
  H1('2. Signing in'),
  P('CGTOOL uses your Dubai Investments account. You do not have a separate CGTOOL password.'),
  Step('Open CGTOOL in your browser.'),
  Step('Choose Sign in with Microsoft.'),
  Step('If you are already signed in to your work account, you will go straight through.'),
  P('If you see “Your login isn’t linked to a User record yet”, your sign-in worked but it has not been connected to your person record. Ask an Administrator to link it on the User Management page. Until that is done the screens will be empty — there is nothing wrong with your account.', { before: 120 }),
  H2('Being signed out'),
  P('CGTOOL locks the screen after a period of inactivity. Your work is not lost: sign in again and you will be back where you were. Anything you had typed into a form but not saved will need retyping, so use Save as draft if you are interrupted.'),
);

// ── 3 Getting around ──
children.push(
  H1('3. Finding your way around'),
  P('The menu on the left groups the screens by what you are trying to do. You will only see the groups your access allows, so your menu may be shorter than a colleague’s.'),
  makeTable(
    ['Menu', 'What is in it'],
    [
      ['My Account', 'Your profile, My Register, and your Declarations'],
      ['RP Transactions', 'Raising, tracking, approving and reviewing related party transactions'],
      ['Reports', 'The compliance returns — submissions, registers and the related party master'],
      ['Investor Relations', 'The share register and share dealing records'],
      ['Admin Panel', 'Administrators only — entities, users, settings and the audit log'],
      ['Declaration Setup', 'Administrators only — notification cycles and scheduling'],
    ],
    [26, 74]),
  Note('If a menu you expect is missing, it is access rather than a fault. An Administrator can grant it on your user record; the Users report shows who can reach what.'),
);

// ── 4 My Register ──
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  H1('4. My Register'),
  P('My Register is your own standing record of the people and companies you are connected to. It is the thing your declarations are built from, so keeping it current is most of the work — once it is right, declaring takes minutes.'),
  H2('Relatives'),
  P('Add each relative the policy covers: spouse, children, stepchildren, parents, parents-in-law, siblings and in-laws. For each one you record their name and their relationship to you, and you can attach supporting documents.'),
  Step('Go to My Account → My Register.', 1),
  Step('Choose Add relative.', 1),
  Step('Enter the name and pick the relationship.', 1),
  Step('Attach a document if you have one, then Save.', 1),
  Note('CGTOOL will stop you adding the same person twice and tell you which record already exists, rather than quietly creating a duplicate that then appears twice on every return.'),
  H2('Companies'),
  P('Record the companies you or your relatives have an interest in. For each one you record the legal name, the nature of the business, and the nature of the holding — and, for a relative’s company, which relative it belongs to.'),
  P('Every addition, change and deletion here is recorded in the audit trail with who made it and when. That is deliberate: the register is evidence, and evidence has to be traceable.'),
  H2('Documents'),
  P('Trade licences and identity documents can be attached to a relative or a company. CGTOOL tracks expiry dates and will show you which ones have lapsed, so a licence that expired two years ago does not sit on a return looking current.'),
);

// ── 5 Declarations ──
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  H1('5. Declarations'),
  P('There are two declarations. You will be emailed when a period opens, and the same notification appears on your Declarations screen. Both have a deadline, and both can be saved as a draft and finished later.'),
  Note('A draft is not a submission. Until you press Submit you count as not having declared, and you will keep appearing on the outstanding list. The Review step tells you clearly which state you are in.'),
  H2('Insider Declaration'),
  P('Formally, the Declaration of Transactions in DI Shares by Insiders. Three steps:'),
  makeTable(
    ['Step', 'What you do'],
    [
      ['1. Personal details', 'Whether you hold a National Investor Number, and the NIN itself; whether you hold DI shares; whether your relatives do, and their NINs'],
      ['2. Documents & shareholding', 'Upload your Emirates ID and passport, and a trade licence if one applies; record the shareholding'],
      ['3. Review & submit', 'Read back everything you have entered, then submit'],
    ],
    [26, 74]),
  H3('Reading your Emirates ID and passport'),
  P('When you upload them, CGTOOL reads the card and the document and fills in the number, the name and the expiry date for you. Check what it filled in — it is right the great majority of the time, but it is reading a photograph, and the expiry date in particular is worth a glance before you move on.'),
  H2('Related Party & Conflict of Interest Declaration'),
  P('Six steps, each of which you can complete or mark as nothing to declare:'),
  makeTable(
    ['Step', 'What you declare'],
    [
      ['1. Relatives', 'The relatives on your register, confirmed for this period'],
      ['2. My companies', 'Companies you hold an interest in'],
      ['3. Relatives’ companies', 'Companies your relatives hold an interest in'],
      ['4. Board roles', 'Boards you sit on'],
      ['5. Conflicts', 'Any actual or potential conflict of interest'],
      ['6. Review & submit', 'Read back, sign, and submit'],
    ],
    [26, 74]),
  H3('“I have nothing to declare”'),
  P('Each section has this option, and it is a real answer rather than a way of skipping the step. Ticking it records that you were asked and said no — which is what a reviewer needs to see. Leaving a section empty without ticking it is not the same thing, and CGTOOL will ask you to choose.'),
  H3('Signing'),
  P('The last step asks for your signature, drawn with the mouse or your finger. It appears on the submitted declaration and on the PDF, beside the date you signed.'),
  H3('After you submit'),
  P('You are emailed a confirmation with the completed declaration attached as a PDF. The declaration stays visible on your Declarations screen, and you can reopen and print it at any time. If the edit window is still open you can amend and resubmit; once it closes the submission is final and the screen will say so.'),
);

// ── 6 RP Transactions ──
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  H1('6. Related Party Transactions'),
  P('A related party transaction is any transaction between a group entity and someone connected to it. Each one is raised, approved, reviewed by the CCAO and then released to a register that can be shown to an auditor.'),
  H2('Raising a transaction'),
  Step('Go to RP Transactions → Add New Transaction.', 2),
  Step('Your entity and your name are filled in and cannot be changed — the transaction is raised in your own name.', 2),
  Step('Choose the counter-party from the list. This is the Related Party Master, so if the party is not there it has not been declared yet; have it added before raising the transaction.', 2),
  Step('Enter the transaction value and the date of request.', 2),
  Step('Describe the transaction, including the material terms and conditions. This is the part a reviewer reads first — a line that says only “services” will come back to you.', 2),
  Step('Attach the supporting documents: the purchase order, the contract, the quotation.', 2),
  Step('Submit.', 2),
  Note('CGTOOL runs its pre-checks as you submit and shows you the result rather than failing silently — whether the counter-party is on the master, and whether the value is within the approver’s delegated authority. A transaction above that limit is routed higher automatically.'),
  H2('Tracking your transactions'),
  P('RP Transactions → My Transactions lists everything you have raised, with its reference, its status, and who it is currently with. Open any one to see its whole history: who raised it, every amendment, who approved it and when, and what the CCAO decided.'),
  makeTable(
    ['Status', 'What it means'],
    [
      ['Awaiting approval', 'With your approver'],
      ['Returned', 'Sent back to you for amendment — read the remarks, fix it and resubmit'],
      ['Approved', 'Approved by your approver, now with the CCAO'],
      ['Escalated', 'Not acted on within 30 days, so it went to the CCAO automatically'],
      ['Released to register', 'Complete, and on the register'],
      ['Rejected', 'Declined — the remarks say why'],
    ],
    [30, 70]),
  H2('If you are an approver'),
  P('RP Transactions → Pending My Approval is your queue. Each item shows the entity, the value, who raised it and how long it has been waiting, with the supporting documents attached. You can approve it, or return it with remarks explaining what needs to change.'),
  P('The queue shows how many days are left before a transaction escalates to the CCAO. After 30 days it goes automatically, which is a fact about the process rather than a judgement about you — but it is better to act than to let it escalate.'),
  H2('If you are the CCAO'),
  P('RP Transactions → CCAO Review holds everything approved and waiting on a governance decision. You record the decision taken offline: which body took it, the meeting date, which members abstained as conflicted, and your compliance conclusion, with the minutes attached as evidence. Approving it releases the transaction to the register.'),
  H2('The register'),
  P('RP Transactions → RP Transaction Register is the complete record of released transactions, filterable and exportable. It is what you hand to an auditor.'),
);

// ── 7 Reports ──
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  H1('7. Reports'),
  P('Reports are available to Compliance and administrators. Each one opens as the printed sheet it will become, with the figures that answer the question at the top, and the filters you chose stated on the page.'),
  makeTable(
    ['Report', 'Answers'],
    [
      ['Users', 'Who has an account, and which parts of the system each of them can reach'],
      ['Insider Submission', 'Who was asked to file an Insider Declaration for a period, and who has'],
      ['RP & COI Submission', 'The same, for the Related Party & COI Declaration'],
      ['Related Party Register', 'The declared related parties'],
      ['Related Party Master', 'Entities, users, relatives and their companies, in one list'],
    ],
    [30, 70]),
  H2('Getting a report out'),
  makeTable(
    ['Button', 'What you get'],
    [
      ['Print', 'The sheet as shown, on A4 landscape with page numbers'],
      ['PDF', 'The same document as a file, identical for everyone who opens it'],
      ['Excel', 'A spreadsheet with a filterable header row, for working on further'],
    ],
    [20, 80]),
  P('Below the sheet are the viewer controls: page forward and back, a page number to type into, zoom, fit to width, and full screen. Turn on All pages before printing if you want the whole report rather than the page you are looking at.', { before: 120 }),
  Note('Every output carries the filters it was taken with. That matters: a report that does not say what it excluded cannot be told apart from one that found nothing — and the two mean very different things to an auditor.'),
);

// ── 8 Questions ──
children.push(
  H1('8. Common questions'),
  H3('I did not get the notification email.'),
  P('Check your junk folder first. If it is not there, ask an Administrator — the Pending Notifications screen shows exactly who was sent what and when, so this is quick to answer.'),
  H3('The counter-party I need is not in the list.'),
  P('The list is the Related Party Master. If the party is not on it, it has not been declared. Have it added to the register first; raising the transaction against the wrong party is worse than waiting.'),
  H3('I submitted and then noticed a mistake.'),
  P('If the edit window is still open, reopen the declaration, amend it and submit again. Once it has closed, tell Compliance — the correction has to be recorded rather than made quietly.'),
  H3('My transaction says “With CCAO” and nothing is happening.'),
  P('It has been approved and is waiting on a governance decision, which is taken at a meeting rather than on screen. The timeline on the transaction shows who it is with.'),
  H3('Someone else filed my declaration.'),
  P('That is impersonation, and it is allowed only where it has been approved in advance. The declaration records both names — theirs as the person who filed, yours as the person it was filed for. If you did not expect it, raise it with Compliance.'),
  new Paragraph({ spacing: { before: 400 },
    border: { top: { style: BorderStyle.SINGLE, size: 6, color: GOLD, space: 8 } },
    children: [new TextRun({ text: 'For anything this guide does not cover, contact the Compliance team.', font: FONT, size: 20, italics: true, color: MUTED })] }),
);

const doc = new Document({
  creator: 'Dubai Investments',
  title: 'CGTOOL User Guide',
  description: 'End-user guide to the Corporate Governance Tool',
  numbering: {
    config: [
      { reference: 'bullets', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: convertInchesToTwip(0.3), hanging: convertInchesToTwip(0.18) } } } }] },
      { reference: 'steps', levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: convertInchesToTwip(0.35), hanging: convertInchesToTwip(0.22) } } } }] },
    ],
  },
  sections: [{
    properties: { page: { margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } } },
    footers: {
      default: new Footer({ children: [new Paragraph({
        alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'CGTOOL User Guide  ·  Page ', font: FONT, size: 17, color: MUTED }),
                   new TextRun({ children: [PageNumber.CURRENT], font: FONT, size: 17, color: MUTED })] })] }),
    },
    children,
  }],
});

Packer.toBuffer(doc).then(b => { fs.writeFileSync('CGTOOL-User-Guide.docx', b); console.log('written', b.length, 'bytes'); });
