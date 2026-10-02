# Enterprise Prompt Library

A collection of ready-to-use prompts for common business scenarios. Each entry explains what the prompt is for, when to use it, gives a fill-in template, and shows a worked example.

## How to use this library

1. Pick the scenario closest to your task.
2. Copy the **Template** and replace every `[bracketed field]` with your own details.
3. Read the AI's answer critically. You are accountable for anything you send, publish or approve.
4. Never paste confidential, personal or regulated data into a tool your organisation has not approved.

### The anatomy of a strong prompt

| Part | What it does | Example |
|---|---|---|
| Role | Sets the perspective and expertise | "You are a senior financial analyst." |
| Task | States exactly what to produce | "Summarise the attached report in 5 bullets." |
| Context | Gives the background the AI cannot guess | "Audience is the board; the topic is Q3 churn." |
| Constraints | Sets length, tone, format, exclusions | "Under 200 words, plain language, no jargon." |
| Output format | Defines the shape of the answer | "A table with columns: risk, impact, owner." |

### Contents

1. [Executive and Strategy](#1-executive-and-strategy)
2. [Sales](#2-sales)
3. [Marketing](#3-marketing)
4. [Customer Support](#4-customer-support)
5. [Human Resources](#5-human-resources)
6. [Finance](#6-finance)
7. [Legal and Compliance](#7-legal-and-compliance)
8. [Operations and Project Management](#8-operations-and-project-management)
9. [IT and Engineering](#9-it-and-engineering)
10. [Data and Analytics](#10-data-and-analytics)
11. [Quality Assurance](#11-quality-assurance)
12. [Reviewing and Improving Any Prompt](#12-reviewing-and-improving-any-prompt)

---

## 1. Executive and Strategy

### 1.1 Board briefing summary

**Purpose:** Turns long material into a short briefing a busy executive can act on.

**Context:** Use before board meetings, steering committees or leadership reviews, when you have a long report, deck or thread and little time.

**Template**

```text
You are a chief of staff preparing a briefing for [audience].
Summarise the material below into:
1. The decision or question at hand (one sentence)
2. Three key facts that matter most
3. Risks and open questions
4. A recommended next step with an owner and a date
Keep it under [word count] words. Use plain language. Flag any claim that is not supported by the material.

Material:
[paste text]
```

**Example**

```text
You are a chief of staff preparing a briefing for the executive committee.
Summarise the material below into: the decision at hand, three key facts, risks and open questions, and a recommended next step with an owner and a date.
Keep it under 250 words. Use plain language. Flag any claim that is not supported by the material.

Material:
[Q3 customer retention analysis, 12 pages]
```

**Tip:** Ask "What did you leave out that I might want to know?" as a follow-up.

### 1.2 Options and trade-off analysis

**Purpose:** Compares choices fairly so a decision can be made on evidence rather than preference.

**Context:** Use when choosing between vendors, strategies, investments or approaches.

**Template**

```text
Compare these options for [decision]: [Option A], [Option B], [Option C].
Criteria, in order of importance: [criteria].
Produce a table scoring each option from 1 to 5 per criterion with a one-line reason, then recommend one option and state the main condition under which your recommendation would change.
State your assumptions explicitly.
```

**Example**

```text
Compare these options for our customer data platform: build in-house, buy Vendor X, buy Vendor Y.
Criteria, in order of importance: total cost over 3 years, time to launch, security and compliance, flexibility.
Produce a table scoring each option from 1 to 5 per criterion with a one-line reason, then recommend one option and state the main condition under which your recommendation would change.
```

### 1.3 Pre-mortem

**Purpose:** Imagines the project has failed and works backwards to find the causes while there is still time to prevent them.

**Context:** Use at the start of a major initiative or before a launch decision.

**Template**

```text
It is [date, 12 months from now] and [project] has failed. List the 10 most likely reasons, grouped as people, process, technology and external factors. For each, give an early warning sign and one preventive action.
```

---

## 2. Sales

### 2.1 Personalised outreach email

**Purpose:** Drafts a short, relevant first message to a prospect.

**Context:** Use after researching a prospect, when you know their role, a trigger event and one relevant benefit.

**Template**

```text
Write a cold email of no more than [120] words to [name], [title] at [company].
Trigger: [recent news or event].
Our relevant value: [one specific benefit with a number if possible].
Tone: [warm, direct, no hype]. End with one low-pressure question.
Do not use phrases like "I hope this finds you well" or "revolutionary".
```

**Example**

```text
Write a cold email of no more than 120 words to Dana Ortiz, VP Operations at Brightline Logistics.
Trigger: they announced a second distribution centre in Ohio.
Our relevant value: our routing software cut delivery planning time by 30% for similar fleets.
Tone: warm, direct, no hype. End with one low-pressure question.
```

### 2.2 Objection handling

**Purpose:** Prepares honest, specific responses to common objections.

**Context:** Use when preparing for a call or training new sales staff.

**Template**

```text
A prospect says: "[objection]". Give three possible responses: one that asks a clarifying question, one that reframes with evidence, and one that acknowledges a real limitation honestly. Keep each under 60 words.
```

### 2.3 Call summary and next steps

**Purpose:** Converts rough call notes into a clean record for the CRM.

**Template**

```text
From these notes, produce: a three-sentence summary, the customer's stated needs, objections raised, decision makers and their roles, agreed next steps with dates, and risks to the deal. Mark anything inferred rather than stated as "(inferred)".

Notes:
[paste notes]
```

---

## 3. Marketing

### 3.1 Campaign brief

**Purpose:** Creates a clear brief that aligns teams and agencies.

**Template**

```text
Draft a campaign brief for [product or offer].
Include: objective with one measurable target, target audience and their main problem, key message, three supporting proof points, channels, budget range [amount], timeline [dates], success metrics, and risks.
Use headings and keep the whole brief to one page.
```

### 3.2 Content repurposing

**Purpose:** Turns one asset into several formats without starting again.

**Context:** Use after producing a webinar, whitepaper or long article.

**Template**

```text
Turn the article below into: (1) a LinkedIn post of under 150 words, (2) five short social posts, (3) an email newsletter blurb of 80 words, (4) three questions for an FAQ. Keep the original facts accurate and keep the tone [tone].

Article:
[paste text]
```

### 3.3 Message testing

**Purpose:** Generates variations to test, with a hypothesis for each.

**Template**

```text
Write five versions of a headline for [offer], each using a different angle: benefit, urgency, curiosity, social proof, and plain description. For each, state the hypothesis about why it might win.
```

---

## 4. Customer Support

### 4.1 Empathetic reply to a complaint

**Purpose:** Produces a calm, accountable reply that solves the problem.

**Context:** Use for upset customers. A human should review before sending.

**Template**

```text
Write a reply to this customer message. Acknowledge the specific problem in the first sentence, apologise once without excuses, explain what we will do and by when, and offer one clear next step. Tone: [calm, respectful]. Under [150] words. Do not promise anything not listed under "What we can offer".

Customer message:
[paste]

What we can offer:
[refund, replacement, escalation, etc.]
```

### 4.2 Knowledge base article from tickets

**Purpose:** Turns repeated tickets into a self-service article.

**Template**

```text
Using these resolved tickets, write a help article titled "[title]". Include: symptoms, cause, step-by-step fix, how to confirm it worked, and when to contact support. Write at an 8th-grade reading level.

Tickets:
[paste]
```

### 4.3 Ticket triage

**Purpose:** Classifies incoming tickets consistently.

**Template**

```text
Classify the ticket below. Return: category from [list], severity from [low, medium, high, critical] with a one-line reason, sentiment, and whether it needs human escalation (yes or no). If information is missing, say what to ask the customer.

Ticket:
[paste]
```

---

## 5. Human Resources

### 5.1 Job description

**Purpose:** Writes a clear, inclusive job posting.

**Template**

```text
Write a job description for a [role] in [team]. Include: a two-sentence summary, six responsibilities, required versus preferred qualifications (keep required to five), the pay range [range], and our working arrangement [details]. Use inclusive language and avoid unnecessary degree requirements and jargon.
```

### 5.2 Structured interview questions

**Purpose:** Creates fair, comparable interviews tied to the role.

**Template**

```text
Create 8 structured interview questions for a [role]. For each give the competency tested, what a strong answer includes, and a red flag. Include two behavioural questions and one scenario question. Do not ask about protected characteristics.
```

### 5.3 Performance feedback

**Purpose:** Helps managers write specific, balanced feedback.

**Context:** Use as a drafting aid only. Managers remain responsible for accuracy and fairness.

**Template**

```text
Turn these observations into feedback for [role] using the format: situation, behaviour, impact, and suggested next step. Keep it factual, specific, and respectful. Do not guess at motives.

Observations:
[paste]
```

---

## 6. Finance

### 6.1 Variance explanation

**Purpose:** Explains why actuals differ from budget in plain language.

**Template**

```text
Explain the variances in the table below for [period]. For each line over [5%] or [amount], give the likely drivers, whether it is one-off or recurring, and a recommended action. Separate facts from assumptions and list questions I should ask the budget owner.

Data:
[paste table]
```

### 6.2 Business case outline

**Purpose:** Structures an investment request for approval.

**Template**

```text
Draft a business case for [initiative]. Sections: problem, proposed solution, costs (one-time and recurring), benefits with how each will be measured, payback period, risks and mitigations, alternatives considered, and decision requested. Use placeholders where numbers are missing. Do not invent figures.
```

### 6.3 Policy question check

**Purpose:** Finds where a transaction might conflict with an internal policy.

**Template**

```text
Using only the policy text below, answer: "[question]". Quote the relevant clause. If the policy is silent or ambiguous, say so and suggest who should decide.

Policy:
[paste]
```

---

## 7. Legal and Compliance

> These prompts support review. They do not replace qualified legal advice.

### 7.1 Contract risk scan

**Purpose:** Flags clauses that deserve a lawyer's attention.

**Template**

```text
Review the contract below from the perspective of [our company, role]. List clauses relating to: liability caps, indemnification, termination, auto-renewal, data protection, intellectual property, and governing law. For each, summarise it in plain language, rate the risk (low, medium, high) and suggest a question to raise with counsel. Do not give legal advice; flag items for review.

Contract:
[paste]
```

### 7.2 Policy plain-language rewrite

**Purpose:** Makes a dense policy readable without changing its meaning.

**Template**

```text
Rewrite the policy below in plain language at an 8th-grade reading level. Preserve every obligation and exception. Then list any terms whose meaning might have changed so a reviewer can check.

Policy:
[paste]
```

### 7.3 Regulatory change impact

**Purpose:** Translates a new regulation into actions for the business.

**Template**

```text
Summarise the regulatory change below. List: who is affected in our organisation, what must change, deadlines, evidence we will need to show compliance, and open questions for counsel.

Text:
[paste]
```

---

## 8. Operations and Project Management

### 8.1 Meeting notes to action items

**Purpose:** Converts a transcript into decisions and owned actions.

**Template**

```text
From the transcript below, list: decisions made, action items (owner, task, due date), unresolved questions, and risks raised. If an owner or date was not stated, write "unassigned" rather than guessing.

Transcript:
[paste]
```

### 8.2 Project risk register

**Purpose:** Produces a first-draft risk register to refine with the team.

**Template**

```text
Create a risk register for [project] with 12 risks. Columns: ID, risk, cause, impact (1-5), likelihood (1-5), score, mitigation, owner role, early warning sign. Sort by score, highest first.
```

### 8.3 Process documentation

**Purpose:** Documents a process as clear, numbered steps.

**Template**

```text
Turn this description into a standard operating procedure: purpose, scope, roles, numbered steps, decision points, exceptions, and a checklist for completion. Highlight any step that is ambiguous.

Description:
[paste]
```

---

## 9. IT and Engineering

### 9.1 Code review

**Purpose:** Gets a structured second opinion on a change.

**Template**

```text
Review this [language] code. Check for: correctness bugs, security issues, error handling, performance, readability, and missing tests. Report findings ordered by severity with the line, the problem, and a suggested fix. Do not rewrite code that is fine.

Code:
[paste]
```

### 9.2 Incident post-incident review

**Purpose:** Builds a blameless timeline and learning points.

**Template**

```text
Using the logs and notes below, write a blameless post-incident review: summary, impact, timeline, root cause, contributing factors, what went well, what went poorly, and action items with owners. Separate confirmed facts from hypotheses.

Notes:
[paste]
```

### 9.3 Technical explanation for non-technical readers

**Purpose:** Translates technical detail for decision makers.

**Template**

```text
Explain [technical topic] to [audience] in under [200] words. Use one analogy, avoid acronyms unless defined, and end with what this means for their decision.
```

---

## 10. Data and Analytics

### 10.1 Insight summary from a table

**Purpose:** Finds the story in a data set and states its limits.

**Template**

```text
Analyse the data below. Report: the three most important patterns, one surprising finding, data quality issues you notice, and what you cannot conclude from this data. Provide numbers for every claim. Suggest two follow-up analyses.

Data:
[paste]
```

### 10.2 Metric definition

**Purpose:** Creates an unambiguous definition so teams report the same number.

**Template**

```text
Define the metric "[name]" with: business purpose, exact formula, inclusions and exclusions, data source, refresh frequency, owner role, known pitfalls, and one worked example with numbers.
```

### 10.3 Dashboard requirements

**Purpose:** Clarifies what a dashboard should show before building it.

**Template**

```text
For a dashboard used by [role] to decide [decision], propose: five key metrics, the chart type for each and why, filters needed, targets or thresholds, and what the viewer should do when a metric is off target.
```

---

## 11. Quality Assurance

### 11.1 Acceptance tests from requirements

**Purpose:** Produces testable Given / When / Then cases that cover more than the happy path.

**Context:** Use when you want an AI to draft tests for human review. Every case must trace to a requirement.

**Template**

```text
For each requirement below, write acceptance tests in Given / When / Then form. For every requirement include at least one positive, one negative, and one boundary case, and add an idempotency case wherever a request can be repeated or retried. Each Then clause must contain exact, measurable values such as status codes, counts or durations. Label every test with the requirement ID it verifies. Do not write a test that does not trace to a requirement.

Requirements:
[paste with IDs]
```

**Example**

```text
Requirement R-04: Loss description must be between 20 and 2,000 characters inclusive.
-> Write tests for 19, 20, 2,000 and 2,001 characters, and a test with only whitespace.
```

### 11.2 Review of AI-generated tests

**Purpose:** Checks a set of tests for gaps before anyone relies on it.

**Template**

```text
Act as a sceptical test reviewer. For the requirements and test cases below, report: requirements with no test, tests with no requirement, requirements lacking negative, boundary or idempotency cases, expected results that are vague, and non-functional tests without numeric targets. Finish with a verdict: approve, approve with changes, or reject, and the reason.

Requirements:
[paste]

Test cases:
[paste]
```

### 11.3 Defect report

**Purpose:** Turns a rough bug note into a complete, reproducible report.

**Template**

```text
Write a defect report from these notes. Include: title (symptom and place), summary, environment and build, preconditions, numbered steps, expected result, actual result, frequency, evidence needed, suggested severity with reason, suggested priority with reason, workaround, and verification criteria. Mark anything you inferred.

Notes:
[paste]
```

### 11.4 Test summary with recommendation

**Purpose:** Converts results into a decision.

**Template**

```text
Using the results below, write a test summary report ending with an explicit SHIP or NO-SHIP recommendation. Include: execution counts, pass rate, open defects by severity and priority, exit criteria met versus not met, residual risks, and conditions for re-evaluation. Base every statement on the data provided.

Results:
[paste]
```

---

## 12. Reviewing and Improving Any Prompt

### 12.1 Prompt critique

**Purpose:** Improves a prompt you have already written.

**Template**

```text
Critique this prompt. Identify what is ambiguous, what context is missing, and what constraints or output format would improve it. Then rewrite it. Keep the rewrite under [length].

Prompt:
[paste]
```

### 12.2 Self-check before sending

**Purpose:** Catches errors in a draft answer.

**Template**

```text
Re-read your previous answer. List any claims that may be inaccurate, anything stated with more certainty than the evidence supports, and anything I should verify independently. Then give a corrected version.
```

### 12.3 Ask clarifying questions first

**Purpose:** Prevents wasted effort on vague requests.

**Template**

```text
Before you answer, ask me up to five questions you need answered to do this well. Wait for my replies. Task: [describe task]
```

---

## Good practice checklist

- State the audience and the purpose.
- Supply the facts; do not rely on the AI to know your business.
- Ask for sources, assumptions or confidence where accuracy matters.
- Request a specific format.
- Check numbers, names, quotations and legal or financial claims.
- Keep confidential and personal data out of unapproved tools.
- Save prompts that work and note who owns them, so the library improves over time.

## Maintenance

| Item | Guidance |
|---|---|
| Owner | Assign one owner per scenario section |
| Review cycle | Review each prompt every six months or when the tool changes |
| Versioning | Record the date and change when a prompt is edited |
| Feedback | Collect examples of good and bad outputs to refine templates |
