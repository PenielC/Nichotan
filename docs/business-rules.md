# Nichotan business rules

The rules the system follows, in one place. Each rule has a status:

- **Built**: implemented and covered by tests (or, for interface behaviour, checked in a browser).
- **Decided**: agreed with the business but not built yet.
- **Open**: needs a decision from Nichotan. Do not decide these silently.

Sources: `CLAUDE.md` (implementation rules), the BRD (v1.0 draft, 26 Oct 2026), and decisions made while building. Where a rule came from a conversation with the business owner, the date is given. Update this file whenever a rule is added or changed.

---

## 1. Scope and rollout

| ID | Rule | Status |
|---|---|---|
| BR-1.1 | Canada and Germany are both in the first release. | Decided (2026-10-27) |
| BR-1.2 | Applicants fall into three levels: **High School**, **Undergraduate**, **Postgraduate**. High School follows the same form and flow as Undergraduate. | Decided (2026-10-27) |
| BR-1.3 | The Nichotan application form is the same for Canada and Germany. Only the required documents differ, by country and by applicant level. | Decided (2026-10-27) |
| BR-1.4 | Visa Extension is a future module. Nothing should be built that stops it being added later as a separate case and workflow. | Decided (BRD 6.6) |
| BR-1.5 | Out of scope for the first release: automatic submission to university or immigration portals, AI making eligibility or visa decisions, live status from institutions or authorities, Kafka, a full accounting ledger, mandatory OCR or translation. | Decided (BRD 4.2) |
| BR-1.6 | Manual status entry must never be presented as a live integration with universities or immigration authorities. | Decided (CLAUDE.md 14) |

## 2. People, roles and access

| ID | Rule | Status |
|---|---|---|
| BR-2.1 | **Three kinds of user:** Administrators, Counsellors (staff who work with students) and Students. Counsellors do all the day-to-day work: admissions, documents (uploading and reviewing), visa cases and the pre-departure list, enquiries, and reports. Administrators do everything a counsellor does, plus staff accounts, the audit log, checklist and template editing, and correcting a wrongly recorded status. The earlier Visa Officer, Document Reviewer and Report Viewer roles were removed: the immigration authority's own officers review on their platform, and nobody needs a read-only or single-task staff role. Anyone who held one of them became a Counsellor (migration `SimplifyStaffRoles`). In the code the counsellor role is still named `AdmissionsCounsellor`. | Built |
| BR-2.2 | "Case workers" means Administrators and Counsellors. Only they see individual students and application forms. Reports show counts only. | Built |
| BR-2.3 | Only Administrators manage staff accounts (invite, resend invitation, change roles, disable or enable, reset MFA) and read the audit log. | Built |
| BR-2.4 | A candidate can only reach their own profile, form and (later) applications, documents, tasks and messages. No candidate-facing endpoint takes another candidate's id. Another person's record returns "not found" or "forbidden". | Built |
| BR-2.5 | Authorization is enforced on the server for every endpoint, twice: by the API policy and again inside the handler. Frontend route guards are not security. | Built |
| BR-2.6 | Staff access may later also depend on case assignment ("where configured"). Staff count and assignment policy are not yet decided. | Open |

## 3. Accounts and sign-in

| ID | Rule | Status |
|---|---|---|
| BR-3.1 | Candidates register themselves with name, email and password, or staff can create an account for them. Emails are unique regardless of letter case. | Built |
| BR-3.2 | Passwords need at least 10 characters, with upper-case and lower-case letters and a number. | Built |
| BR-3.3 | A new candidate account is usable straight away, but the email is unconfirmed until the candidate opens the link emailed to them. The link works once and expires after 48 hours. | Built |
| BR-3.4 | Password reset links work once and expire after 1 hour. Staff invitation links expire after 72 hours. The forgot-password request gives the same answer whether or not the address has an account. | Built |
| BR-3.5 | Staff must sign in with an authenticator app (MFA) before using staff features. This is required in production; it is switched off only in the local development settings. Candidates may use MFA but are not required to. | Built |
| BR-3.6 | The access token lasts 15 minutes. A rotating refresh token lives in an HttpOnly, Secure, same-site-strict cookie. If an already-used refresh token is presented again, the whole session family is revoked. | Built |
| BR-3.7 | Five wrong passwords or codes in a row lock the account for 15 minutes. Sign-in and email-sending endpoints are rate limited. | Built |
| BR-3.8 | Disabled accounts cannot sign in. An administrator can reset a staff member's authenticator. | Built |

## 4. The candidate and the Nichotan application form

A candidate is a person. The candidate is not the same thing as an application to one institution.

| ID | Rule | Status |
|---|---|---|
| BR-4.1 | A candidate has one Nichotan application form. It is separate from any admission application. | Built |
| BR-4.2 | The form follows the paper form: personal information, contact information with next of kin, program and institution selection, academic history, and the declaration. It also records the **destination country** (Canada or Germany), which the paper form lacks. | Built |
| BR-4.3 | Program levels: High school, Bachelor's degree, Diploma, Certificate, Graduate degree (Master's), Post Graduate Diploma/Certificate. | Built |
| BR-4.4 | The applicant level comes from the program level: High school is High School; Bachelor's, Diploma and Certificate are Undergraduate; Master's and Post Graduate Diploma/Certificate are Postgraduate. | Built |
| BR-4.5 | The candidate lists up to 5 institutions (in order of preference), up to 2 program choices, a start year, and a start term (Winter Jan–Apr, Summer May–Aug, Fall Sep–Dec). | Built |
| BR-4.6 | Academic history: up to 2 high schools and up to 3 post-secondary institutions, most recent first. Dates are month and year. A completion date cannot be before its start date, and is left empty while still attending. High school grade is "less than 12" or "12 or equivalent". | Built |
| BR-4.7 | A high school or undergraduate applicant must list at least one high school. A postgraduate applicant must list at least one post-secondary institution. | Built |
| BR-4.8 | A draft can be saved incomplete, at any time. Required fields are checked only on submission: names, gender, date of birth (not in the future), citizenship, address, city, country, a valid email, phone, next of kin name and valid email, destination, program level, a start year that is not in the past, start term, at least one institution and one program. Next of kin phone and alternative phone are optional. | Built |
| BR-4.9 | New forms are pre-filled with the name, email and phone already on the candidate's account. | Built |
| BR-4.10 | Rows left completely blank are ignored, not counted as entries. | Built |

### 4a. Signing, locking and versions

| ID | Rule | Status |
|---|---|---|
| BR-4.11 | The declaration is: "By signing this Application, I understand and I certify that all statements on this application are true and complete." It has a version number. Changing the wording means a new version, so earlier signatures keep the text their signer agreed to. | Built |
| BR-4.12 | To submit, the candidate must tick the confirmation box, type their full name, and draw a signature. The signature must be a PNG image of a plausible size. | Built |
| BR-4.13 | The signature record keeps the image, the typed name, the time, the IP address, which signing method was used, the declaration version and wording, and a SHA-256 hash of the exact form content and declaration. | Built |
| BR-4.14 | The signing method is replaceable. The built-in method is a drawn signature ("Native"). A third-party e-signature provider can be added later without changing the form. Whether the built-in method is legally sufficient has not been confirmed for Zimbabwe, Canada or Germany. | Built / Open |
| BR-4.15 | Once submitted, the form is **locked**. The candidate cannot change it. | Built |
| BR-4.16 | Staff (case workers) can **reopen** a submitted form, but must give a reason. Only a submitted form can be reopened. The candidate is emailed a link, with no reason or personal details in the email, and sees the reason when they sign in. | Built |
| BR-4.17 | After a reopen the candidate edits and signs again. This creates a new version. **Every version is kept**, unchanged, with its own signature. A version is never edited or deleted. | Built |
| BR-4.18 | A signed version can be downloaded as a branded PDF by the candidate it belongs to, or by case workers. The PDF shows the answers, the drawn signature, signer name, date, and the content hash. | Built |
| BR-4.19 | Staff opening a candidate's form, version or PDF is recorded in the audit log. | Built |
| BR-4.20 | **Phone numbers include the country code.** Every phone number we ask for (registration, My details, the application form's phone, alternative phone and next-of-kin phone, and the website enquiry) must start with + and the country code, such as +263 77 123 4567 (Zimbabwe), +1 416 555 0123 (Canada) or +49 151 2345 6789 (Germany); 00 in place of + is accepted. The field shows these examples, and as the person types it says what to fix (a number starting with 0 is told to replace that 0 with the country code). Numbers are stored tidily as + and digits, so a counsellor can call or WhatsApp them from anywhere. The application form accepts anything while a draft is being saved but needs a proper number to be signed and submitted. Phone numbers are optional where they were before (registration, My details, enquiry, alternative and next-of-kin phone) but must be right if given. Numbers saved before this rule stay as they are until changed; WhatsApp links only use numbers with a country code. | Built |

## 5. Institutions, programmes and admission applications

The school's own application portal is outside our control. **Only staff submit on it**, and everything the system holds about an application's progress is entered by staff from what they did or were told there. Nothing is synced with an institution (BR-1.6).

| ID | Rule | Status |
|---|---|---|
| BR-5.1 | A candidate may have **several admission applications**. Each is tied to one institution, programme, country, intake (term and year) and applicant level, and has its own status and history. The country and level are copied when the application is created, so later edits to the directory don't rewrite it. | Built |
| BR-5.2 | **Students create their own applications**, choosing a school, programme and intake from the directory. Staff can also create one for a candidate. Candidates see their own applications with plain-language statuses and the dates, but no staff notes, staff names, or the school's reference. | Built |
| BR-5.3 | The directory holds institutions (name, country, website, notes) and programmes (name, applicant level, and the intakes it starts in). Administrators and Admissions Counsellors can add and edit them; other case workers can view them. Entries are archived, never deleted, so existing applications keep their references. An institution's country and a programme's level can't change once an application uses them. | Built |
| BR-5.4 | An application must use an intake its programme lists, and its institution and programme must not be archived. | Built |
| BR-5.5 | A candidate can't have two **open** applications for the same programme and intake. A rejected, withdrawn or closed one doesn't block a fresh start. | Built |
| BR-5.6 | Statuses: Draft, Documents Required, Ready for Review, Submitted to Institution, Awaiting Decision, Offer Received, Rejected, More Information Required, Resubmission Required, Withdrawn, Closed. | Built |
| BR-5.7 | Allowed moves: Draft to Documents Required, Ready for Review or Withdrawn. Documents Required to Ready for Review or Withdrawn. Ready for Review to Submitted to Institution, Documents Required or Withdrawn. Submitted to Institution or Awaiting Decision to Offer Received, Rejected, More Information Required, Resubmission Required or Withdrawn (Submitted may also go to Awaiting Decision). More Information Required to Submitted to Institution or Withdrawn. Resubmission Required to Ready for Review, Submitted to Institution or Withdrawn. Offer Received to Closed or Withdrawn. Rejected to Closed. Withdrawn and Closed are final. A school can decide before anyone records "awaiting decision", so a decision may follow a submission directly. | Built |
| BR-5.8 | A written note is required to move to Rejected, Withdrawn, More Information Required or Resubmission Required. | Built |
| BR-5.9 | An application can't be moved to Submitted to Institution until the candidate's Nichotan application form is signed and submitted (not a draft, and not reopened). | Built |
| BR-5.10 | Each move records **two dates**: the day it happened (at the school or in the office), which staff can set, and when staff recorded it. The day it happened can't be in the future. Monthly movement reports use the day it happened. | Built (date stored) / Decided (reports) |
| BR-5.11 | Each application keeps the school's own reference number and an optional link to it on the school's portal, plus the day it was submitted there. **The candidate's password for the school's portal is never stored.** | Built |
| BR-5.12 | Administrators and Counsellors record moves (including applying on the school's portal), edit school details, and create applications for a candidate. A student can only send or withdraw their own application, and nothing else about its status. | Built |
| BR-5.13 | Every move is added to the application's history with who and when, and the history is never edited. A wrongly recorded status is fixed by an **Administrator** with a correction: a note is required, and the original entry stays visible, with the correction shown after it. | Built |
| BR-5.14 | There is no appeal path after Rejected, and Withdrawn and Closed can't be reopened by normal moves. An Administrator's correction can fix a mistaken Withdrawn or Rejected. | Decided (defaults, not confirmed) |
| BR-5.15 | The screens say that statuses are entered by staff and aren't synced with any institution. | Built |
| BR-5.16 | The documents check on moving to Ready for Review or Submitted to Institution is described in BR-6.15. | Built |
| BR-5.17 | **The student's part.** A student's new application starts as Draft. When they've finished, they **send it to their counsellor**, which moves it to Ready for Review. They can't send it until their Nichotan application form is signed and submitted and every required document is **uploaded** (accepting the documents is the counsellor's job, after). The screen lists what's missing and links to it. | Built |
| BR-5.18 | **Staff's part.** Staff see sent applications on the dashboard ("Applications to review") and in the Admissions list. They check the documents, then **apply on the school's own portal themselves** and record it (Submitted to Institution, with the date and the school's reference). A student never marks anything as submitted to a school. When a student sends an application, **enquiries@nichotan.com** is emailed (the `Admissions:NotifyEmail` setting; empty turns it off): it names the student and the school and links to the application, with no contact details or documents. A failed send never undoes the student's send, and the student gets no email from this. | Built |
| BR-5.19 | A student can withdraw their own application while it is Draft, Documents Required or Ready for Review. Once staff have submitted it to the school, only staff can withdraw it, so the student asks their counsellor. | Built |
| BR-5.20 | A student can have **up to 5 open applications** at once (matching the five institutions on the Nichotan form). Rejected, withdrawn and closed ones don't count. | Built |
| BR-5.21 | If a student's form has a program level, they can only apply to programmes at that level (a student whose form says Postgraduate can't pick an undergraduate programme), and the list they choose from is narrowed to match. | Built |
| BR-5.22 | A student only sees what staff have published: schools and programmes that aren't archived, and intakes that haven't passed. Staff-only notes on institutions are never shown to students. | Built |
| BR-5.23 | If their school isn't listed, a student can **suggest** it (school, country, programme, optional intake and note). Staff see the suggestions on the Institutions page, add the school to the directory, and mark the suggestion added or declined; the student sees which, but not staff's private notes. A student can have at most 10 suggestions waiting. | Built |

## 6. Documents

| ID | Rule | Status |
|---|---|---|
| BR-6.1 | Documents belong to the **candidate**, not to each application. Passport, academic records, CV and references are uploaded once and count for every application. Application-specific items (the Statement of Purpose, written for each school) are uploaded per application. | Built |
| BR-6.2 | The document kinds are maintained by Administrators. Starting list: Passport (has an expiry date), Academic records, Nichotan application form, CV, Academic reference, Professional reference, Statement of purpose. | Built |
| BR-6.3 | The **Nichotan application form** is never uploaded. It counts as done when the candidate's signed form is submitted. | Built |
| BR-6.4 | **Checklist rules** say which documents each application needs, by country, applicant level and institution; any of the three can be left as "any". When rules overlap, the most specific wins (institution, then country, then level); a tie goes to "required". Only Administrators edit the rules. | Built |
| BR-6.5 | Starting rules (BRD 6.2): passport, academic records and the signed form are required for everyone. CV, academic reference, professional reference and Statement of Purpose are required for **Postgraduate** applicants. For Undergraduate and High School applicants they are not required unless an institution-specific rule says so. | Built |
| BR-6.6 | A new application **copies** the current rules onto its own checklist. Later rule changes do not change existing applications, so history is not rewritten. Staff can refresh one application's checklist from the current rules, add a document to it, or waive one (a reason is required to waive). A hand-made change survives a refresh. | Built |
| BR-6.7 | The final Canada and Germany checklists, and who reviews each document, have not been supplied. The rules above are the BRD defaults, to be replaced by Nichotan's lists through the Checklists screen. | Open |
| BR-6.8 | **Uploading and reviewing are separate.** A document is Missing (nothing uploaded), Uploaded, Under review, Accepted, or Needs resubmission. An upload alone is never "verified". The reviewer and time of each decision are recorded. | Built |
| BR-6.9 | Administrators and Counsellors review documents and may upload for a student. Sending a document back needs a note saying what to fix, and the student is emailed a link only. | Built |
| BR-6.10 | A candidate can replace a document until it is accepted. After that only staff can add a new version. Replacing creates a **new version**; every earlier version and decision is kept. | Built |
| BR-6.11 | Allowed files: PDF, JPG and PNG, up to 10 MB. Files are checked by their contents, not their name or what the browser says. Anything else is refused. | Built |
| BR-6.12 | Passports can carry an expiry date. A date in the past is refused, and other document kinds cannot carry one. | Built |
| BR-6.13 | Files are stored privately and never publicly. They are served only through the API after a permission check: a candidate sees only their own, and any case worker can view and download. Every download is written to the audit log. Candidates do not see which staff member uploaded or reviewed a document. | Built |
| BR-6.14 | A candidate can only upload documents that are on their own list. | Built |
| BR-6.15 | Moving an application to **Ready for Review** with required documents still missing, or to **Submitted to Institution** with required documents not yet accepted, needs staff to confirm they want to continue. The move is allowed, and the history notes that it went ahead with documents outstanding. | Built |
| BR-6.17 | A candidate's Documents page follows the program level (and destination) chosen on their own application form, even before staff add an application: a postgraduate sees the CV and references straight away, and a rule added for High School applicants shows for them. If the level changes, the list changes; anything already uploaded stays visible. Rules tied to a single institution, and documents belonging to one application (the Statement of Purpose), appear once an application to that institution exists. | Built |
| BR-6.18 | **A document can be several files.** An administrator marks a document type "can be several files" on the Checklists page (Academic records is on by default). The student adds files one after another, or many at once, up to 10. Each file can be replaced (the old one stays on record) or taken out (it stays on record but stops counting) until the document is accepted; staff can do either at any time. The set is **reviewed as one**: one decision for all the files, and "needs resubmission" carries a note saying what to fix. Taking out every file makes the document missing again. Other document types stay one file. Visa documents work the same way when marked. | Built |
| BR-6.19 | **Staff don't miss a resubmission.** When a student uploads to a document staff sent back for fixes, the staff address gets an email (the student's name, the kind of document and a link only; what to fix stays behind the sign-in). It goes out once per round, however many files follow. The document also appears on every staff member's Today list until someone decides (BR-10.7). | Built |
| BR-6.16 | Malware scanning of uploads is not in place yet and should be added before go-live. | Open |

## 7. Offers and acceptance

The offer and the formal acceptance are separate records. Neither is worked out from the other, and both are entered by staff from what the school sends. Staff see them in the "Offer and acceptance" panel on the application. **Deposits are not tracked:** paying a deposit is between the student and the school, so there is no amount, proof of payment or verification step in the system (removed on request).

| ID | Rule | Status |
|---|---|---|
| BR-7.1 | The institution's **offer** and its **formal acceptance letter** are separate events with separate records. | Built |
| BR-7.2 | **Offer.** The student uploads the offer letter themselves, as soon as staff have applied to the school on their behalf (the school may write back to the student directly). Staff are emailed a link when they do. Staff then record the application as Offer Received (with the day it arrived) and add the offer's details: conditions and an optional reply-by date. The student is emailed a link when those details are recorded, and can read them and the letter on their Applications page. Staff can also upload or replace the letter. Only Administrators and Admissions Counsellors record the details, and only on an application at Offer Received. | Built |
| BR-7.3 | *Removed.* There is no deposit step: it is between the student and the school. | Removed |
| BR-7.4 | *Removed* along with BR-7.3. Existing deposit records and proof-of-payment uploads were deleted by a migration. | Removed |
| BR-7.5 | Payments are evidence and status tracking only: no payment gateway. | Open (assumption, to confirm) |
| BR-7.6 | **Formal acceptance.** Staff record the day the school's formal acceptance letter arrived (not in the future) and upload the letter. This needs the offer to be recorded first. The student can see it and download the letter. | Built |
| BR-7.7 | **PAL.** A school's acceptance can come with a Provincial Attestation Letter (PAL) number. It is **optional**, and applies to **Canada undergraduate** applications only. It is refused for Germany and for other levels. Staff capture the PAL **number** (stored in capitals) and can also upload the PAL **letter**. The student sees both. Whether High School applicants should count has not been stated, so they follow the rule and don't get one. | Built / Open for High School |
| BR-7.8 | **Who uploads what.** The student uploads the **offer letter**. The **acceptance letter** and **PAL letter** are added by staff only. None of these are checklist documents: they don't appear in the review queue and can't be reviewed as documents. | Built |
| BR-7.9 | The dashboard's **Acceptance letters** counts the applications whose formal acceptance has been recorded. **Offers issued** counts applications that have ever received an offer. | Built |
| BR-7.10 | **PAL follow-up reminder.** For a Canadian undergraduate, once the school's acceptance letter is recorded and neither a PAL number nor a PAL letter is, the application appears in "PAL letters to follow up" on the staff dashboard (longest wait first) and shows a banner on its offer and acceptance card. Three days after the acceptance letter's date with no PAL it is marked Overdue. It disappears once the PAL number or letter is recorded. Overdue PALs are also counted in the daily staff digest email (BR-10.4). | Built |
| BR-7.11 | A visa case can be opened once the business-defined prerequisite is met (to be defined). | Open |

## 8. Visa

| ID | Rule | Status |
|---|---|---|
| BR-8.1 | **Opening a case.** A visa case hangs off one admission application and takes its country, school and programme from it. It can be opened once the school's formal acceptance is recorded on that application (nothing about a deposit is needed: that is between the student and the school). Administrators, Admissions Counsellors and Visa Officers open and progress cases; Document Reviewers can only read them. One case per application. | Built |
| BR-8.2 | **Visa checklist.** The checklist template has two checklists: Admission (applying to the school) and Visa (applying for the visa). Each template line is tied to one of them for good. Opening a visa case copies the visa lines for the school's country, level and institution onto that case (most specific line wins, as for admissions), so later template edits never change an open case. Staff refresh, waive (with a reason) or add documents case by case. Candidates upload the documents on their Documents page, where visa items are labelled "Visa: …"; documents they have already given (a passport) count again without a second upload. The starting visa list in the system (Canada: passport, photo, proof of funds, police clearance, medical exam, study plan, fee receipt; Germany: passport, photo, blocked account, health insurance, fee receipt) is a **draft for Nichotan to correct before go-live**. | Built (list to confirm) |
| BR-8.2a | **Visa documents are collected, not reviewed here.** The immigration authority (its visa officers) reviews on its own platform, so nothing in this system accepts or sends back a visa document: uploaded is enough, and they never appear in the document review queue. Students or staff upload them so staff can submit the application on the visa platform. Staff can record the visa as Submitted while required documents are still missing, but only after confirming, and the history notes it. | Built |
| BR-8.2b | **Letters from the authority.** The student (or staff for them) can add the biometrics letter once the visa is Submitted, until it is decided. The decision letter is added by staff only, after the decision is recorded. Neither is a checklist document and neither appears in the reviewers' queue. | Built |
| BR-8.3 | **What is recorded.** The authority's reference number, the submission date, the biometrics appointment (date and place) and the decision date. Each status move records the day it happened and who entered it. Requests for more information carry a note; the biometrics and decision letters are kept as files (BR-8.2b). Deadlines for more information aren't built yet. | Partly built |
| BR-8.4 | **Statuses.** Preparing → Submitted → Biometrics requested → Biometrics completed → Approved / Refused, with More information requested (back to Submitted once the information is sent) and Withdrawn. Refused, Approved and Withdrawn are final. Statuses are entered by staff from what the authority tells them and are never synced with a government system. Notes are required for More information requested, Refused and Withdrawn. Only an Administrator can correct a wrong status; the original entry stays in the history. | Built |
| BR-8.4a | **Dashboard counts.** "Visa applications in process" counts cases Submitted and not yet decided (a case still being prepared isn't counted); "Approved visas" counts cases currently Approved. Both are current pipeline counts, not monthly movement. | Built |
| BR-8.4b | **What the student sees.** Their own visa status and timeline, and the biometrics appointment while one is pending. Staff notes and corrections are not shown. | Built |
| BR-8.6 | **Biometrics reminder.** Staff record the visa as Submitted on our system once they have submitted it on the visa platform. That emails the student (what comes next: biometrics) and puts the case in "Biometrics to follow up" on the staff dashboard until biometrics are marked done. It is marked Overdue 5 days after submission, or once a recorded appointment date has passed. The student is also reminded by email a few days later, up to three times, and overdue cases are counted in the daily staff digest (BR-10.4). | Built |
| BR-8.7 | **Biometrics done.** The student can say their biometrics are done (with the day), or staff can record it. Either moves the case to Biometrics completed, stops the reminder, and the history says who flagged it. When the student does it, staff are emailed a link. "Biometrics requested" (with appointment date and place) is optional and can be skipped. | Built |
| BR-8.5 | The system supports **administrative tracking and assistance**. It does not decide eligibility and never says a visa is guaranteed. Country-specific requirements and wording must be verified by Nichotan before go-live. | Decided (BRD 6.4) |

## 9. Pre-departure

| ID | Rule | Status |
|---|---|---|
| BR-9.1 | **Opening the list.** Once a visa case is Approved, staff (Administrators, Admissions Counsellors, Visa Officers) open the student's pre-departure list. It is copied from that country's template, so later template edits never change a list already open, and the student is emailed a link. Canada and Germany each have their own list; one country's list is never assumed for another. Canada's starting list: passport with visa, study permit approval letter, original academic certificates, valid acceptance letter, proof of fees paid, proof of accommodation, proof of funds, medical information sheet, police clearance, and warm-clothing advice. Germany has a shorter draft. **Both are drafts for Nichotan to approve and keep current.** | Built (lists to confirm) |
| BR-9.2 | **Working through it.** Each item is Required or Recommended (warm clothing is advice, not an immigration requirement). The student ticks "I have this"; staff then check it, which completes the item and records who and when. A student can untick until staff have checked it. Staff can add a note the student sees, add an item for one student, or switch an item off for them. The list is **Ready to travel** when every required item that applies has been checked. | Built |
| BR-9.3 | **Wording and departure.** An administrator keeps the wording shown with each country's list (it says the list is a guide, not legal advice, and rules change); Nichotan must approve it. The student or staff can record a planned departure date (not in the past). | Built |
| BR-9.4 | Administrators edit each country's template under Checklists (items, importance, order, on/off). Changes apply to lists opened afterwards only. | Built |

## 10. Tasks, timeline and reporting

| ID | Rule | Status |
|---|---|---|
| BR-10.1 | **Tasks.** A task has a title, notes, priority (Low, Normal, High), an optional due day, one assignee (an active administrator or counsellor; it defaults to the person creating it), and optional links to a student, an admission application or a visa case (a case brings its student with it, and a link to someone else's case is refused). A new task can't be due in the past. Anyone on the staff can edit, reassign, finish, cancel or reopen any task: there is no case-assignment rule yet. A finished or cancelled task must be reopened before it is changed, and the record shows who closed it. Lists put overdue tasks first, then the earliest due day, then priority. Students never see tasks. | Built |
| BR-10.1a | **Timeline.** A student's record has a timeline of everything on it, newest first: registration, the application form signed or reopened, each admission move with the offer and acceptance records, each visa move and the pre-departure list, documents uploaded and reviewed (and by whom), and tasks. It is read from the records already kept, so nothing is stored twice; the moves keep the day staff dated them to. Opening it is audited. Messages will join it when messaging exists. | Built |
| BR-10.2 | **Current pipeline vs monthly movement.** The monthly report keeps them apart and labels each. *Current pipeline* = how many admission applications / visa cases were in each status as at the end of the month (or today, for the month still running). *Movement* = how many moved **into** each status on a day inside the month, each counted once per status; corrections aren't movement. Both are rebuilt from the recorded moves, by the day each thing happened (not the day it was typed in), so any past month can be reported. The order of moves is the order they were recorded in, because a record is added to the system on the day staff add it even if it started elsewhere earlier. | Built |
| BR-10.2a | **Conversion denominators.** Conversions are cohort based: of the applications (or visa cases) **submitted during the month**, how many, as at the end of the month, had received an offer / been approved (ever), were rejected / refused (currently), or were still waiting. The denominator is always that month's submissions, never everything open. | Built |
| BR-10.2b | Reports show counts only. Administrators and Counsellors can read them; students can't. A CSV download has the same figures and labels as the screen. Candidates registered and enquiries received ignore the filters (they have no country or school yet). | Built |
| BR-10.3 | Report filters built: month, country, programme level, institution, intake term and year. **Not built:** staff owner (there is no case assignment yet) and a date range other than a calendar month. | Partly built |
| BR-10.4 | **Scheduled reminders.** A background worker inside the API checks every 15 minutes (setting `Reminders:RunEveryMinutes`) and sends what is due. **Students:** when a visa application is submitted and biometrics aren't marked done, they are reminded 3 days after submission, then every 5 days, at most 3 times; the emails are a link only. **Staff:** once a day, from 08:00 in the business time zone, one digest email to the admissions mailbox says how many biometrics follow-ups, PAL letters and tasks are overdue (counts and a link, no names); nothing is sent if nothing is overdue. All of these numbers and the time are settings. | Built |
| BR-10.5 | **Never twice, never lost.** Each reminder is recorded in a log with a unique key (kind and round) before it is sent, so it can't go out twice, even if the worker runs twice at once or the server restarts. If sending fails the record is removed and the next run tries again. A run that fails is logged and doesn't stop the app. The worker can be switched off with `Reminders:Enabled`. | Built |
| BR-10.6 | The time zone for the daily digest is **UTC**, decided by Nichotan (setting `Reminders:TimeZone`, so it can change later without code). | Decided |
| BR-10.7 | **Today's briefing, by fixed rules, no AI.** Each staff member sees a "Today" list at the top of their dashboard. It holds (a) **their own** open tasks that are overdue, due today, or due in the next 3 days, and (b) **shared** case alerts everyone sees: biometrics overdue (5 days since submitted, or the appointment has passed), biometrics with an appointment today or tomorrow that isn't marked done, PAL letters past the 3-day follow-up, and documents the student has **sent again after staff sent them back** and nobody has decided yet (shown as due today, and overdue after 2 days). Order: overdue, then today, then coming up; within a tier the one latest by the most days first, then biometrics, PAL, documents, tasks, then title. The headline counts each tier in plain words. Tasks assigned to a colleague never appear. The same data always gives the same list, and nothing leaves the system, so the AI briefing (BR-11.4) is not needed for it. The list shows up to 30 items; the counts cover all. | Built |

## 11. Messaging and AI

| ID | Rule | Status |
|---|---|---|
| BR-11.1 | Emails the system sends contain links only. They carry no documents, passport or financial details. | Built |
| BR-11.2 | WhatsApp uses an official provider and approved templates, with consent and opt-out handling. Sensitive documents and detailed personal data are never put in message bodies. Provider, templates and consent rules are not chosen yet. | Open |
| BR-11.2a | **WhatsApp for now: share by link, nothing sent by the system.** Once a list is ready, staff can click "Send on WhatsApp" and their own WhatsApp opens with the message already written, to the student's number if it was saved with a country code (otherwise WhatsApp asks who to send it to). It is offered for the pre-departure list (once opened), and for the document lists (what the student still has to upload, for the school application and for the visa). The message names the sender, says what the list is, names the items, and gives a link to sign in and see the detail: item names only, nothing personal. Staff press send themselves. Each time WhatsApp is opened for a student's list, the audit log records who, which student and which list (the system can't know it was sent). This is free and needs no WhatsApp account or provider; automatic, template-based WhatsApp (BR-11.2) is not built. | Built (interim) |
| BR-11.3 | Every message attempt is logged (kind, recipient, time, whether it went, and the error if not). **Built for email**: each send is recorded as Sent or Failed in its own save, so the record survives whatever the caller does next, and a failure to log never blocks the email. The recipient is partly hidden (j***@example.com) and the message text is never kept. Administrators read it under Messages. A provider id, related case and delivery state arrive with the WhatsApp provider. | Built (email) |
| BR-11.4 | The AI daily briefing runs on a configurable schedule and timezone. Deadlines and priorities come from deterministic rules; the AI only summarizes minimized data. It separates facts from suggestions, never invents deadlines, never claims an outcome that isn't recorded, and never makes immigration decisions. If the AI provider fails, case management carries on. The AI provider is not chosen yet. | Decided / Open |
| BR-11.5 | Full passports, bank statements and whole case files are not sent to an AI provider by default. | Decided |

## 12. Security, audit and privacy

| ID | Rule | Status |
|---|---|---|
| BR-12.1 | Security-relevant and material actions are written to an append-only audit log: who, what, which record, when, from where, and the outcome. Audit entries never hold passwords, tokens, document contents, or passport or financial data. Denied access attempts are logged too. | Built |
| BR-12.2 | Audited today: registration, sign-in success and failure, lockout, MFA, logout, refresh-token reuse, email verification, password reset, staff invitation and role changes, disable/enable, candidate profile updates and views, access denied, form submission, reopening and viewing. | Built |
| BR-12.3 | Secrets, tokens and real candidate data are never committed. Passwords, tokens, passport numbers, financial details and message bodies are never logged. | Built (policy) |
| BR-12.4 | Data is encrypted in transit and at rest, and database and storage are backed up with tested restores. | Decided (production) |
| BR-12.5 | Timestamps are stored in UTC and shown in the viewer's or business timezone. The time zone for the daily reminder digest is UTC (decided, BR-10.6). The time zone for the AI briefing and for showing deadlines to people is not yet confirmed. | Built (UTC) / Open (briefing and display) |
| BR-12.6 | Retention periods, deletion, candidate data export, hosting provider, data residency and recovery targets (RPO and RTO) are not decided. Records are not permanently deleted until a retention policy is approved. | Open |

## 12a. Enquiries

| ID | Rule | Status |
|---|---|---|
| BR-E.1 | Anyone can send an enquiry from the landing page, with no account. The form asks for: name, email or phone (at least one), highest qualification, the year they graduated (or expect to), preferred programme (or "I'd like guidance"), and whether they're open to Canada and Germany (both, Canada only, Germany only, or another country). A free-text message is optional. | Built |
| BR-E.2 | The page tells people that all programs are self funded, that there are opportunities for part-time work while studying, and that there are opportunities to work after graduation with a possibility of obtaining permanent residency. It says these opportunities depend on each country's rules and aren't guaranteed. Phone +263 71 612 4255 and email enquiries@nichotan.com are shown. | Built |
| BR-E.3 | An enquiry is kept apart from candidates until a counsellor follows it up. It stores only what the person typed. Submissions are rate limited by address, and a hidden trap field quietly discards simple bots. | Built |
| BR-E.4 | All case workers can read enquiries. Administrators and Admissions Counsellors set the status (New, Contacted, Closed) and add a follow-up note; each update is audit-logged with who made it. | Built |
| BR-E.4a | Every enquiry shows whether it has been **viewed**. It counts as viewed the first time an Administrator or Admissions Counsellor opens it, and the first viewer's name and the time are kept; later views by anyone change nothing. Other case workers can see the state but opening an enquiry doesn't mark it. The number not yet viewed appears on the dashboard (with the newest ones listed) and beside Enquiries in the staff menu. The first view is audit-logged. | Built |
| BR-E.5 | When an enquiry arrives, **enquiries@nichotan.com** is emailed (the address is the `Enquiries:NotifyEmail` setting; empty turns it off). The email says only the sender's name and links to the Enquiries page; it carries none of the person's contact details or answers. A failed send never loses the enquiry. The person who sent the enquiry is **not** emailed a confirmation. Bot submissions caught by the hidden field send nothing. | Built |
| BR-E.6 | Turning an enquiry into a candidate account is manual: the person registers themselves, or staff invite them. | Decided (not built) |

## 13. Open decisions (summary)

1. Legal sufficiency of the built-in drawn signature for the Nichotan declaration (BR-4.14).
2. Final Canada and Germany document checklists, and who reviews each document (BR-6.7). Malware scanning of uploads before go-live (BR-6.16).
3. Whether payments are evidence-only or use a payment gateway (BR-7.5).
4. When a visa case can be opened (BR-7.8).
5. Whether PAL applies to High School applicants (BR-7.7). 
6. Whether staff access is limited by case assignment; staff size and report permissions (BR-2.6).
7. WhatsApp provider, templates, consent and opt-out rules (BR-11.2).
8. AI provider and data-processing terms (BR-11.4).
9. Hosting, data residency, recovery targets (BR-12.6).
10. Retention periods, deletion process and candidate data export (BR-12.6).
11. Time zone for the AI briefing and for deadline display (BR-12.5). The reminder digest is UTC (decided).
12. Whether there is any limit on how many applications a candidate can have, beyond no duplicates (BR-5.5), and whether Rejected should allow an appeal (BR-5.14).
13. QuestPDF's Community licence (free below US$1M annual revenue) must be confirmed to apply to whoever runs the system before go-live.
