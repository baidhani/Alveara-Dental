# Fail-Closed Policy
## Customer Support Assistant (Sample)

**Version:** 1.0  
**Date:** October 3, 2026  
**Owner:** Security & Governance Team  
**Classification:** Internal  
**Next Review:** January 3, 2027

---

## Executive Summary

A **fail-closed policy** is a security principle that says: *When in doubt, say no.*

Instead of defaulting to allowing an action, our system defaults to **denying it** until we're certain it's safe and authorized. This protects customers, the company, and builds trust. When systems are uncertain, they err on the side of caution—closing doors rather than opening them.

---

## The Core Principle

### What Does "Fail-Closed" Mean?

**Fail-Closed = Deny by Default, Allow by Exception**

When the Customer Support Assistant encounters a situation where:
- It's not 100% sure an action is safe
- Policies are unclear
- Authorization is ambiguous
- Risk is uncertain

**The system closes the door.** It denies the action and escalates to a human for review.

### Why This Matters

Consider two approaches:

| Approach | Default | Risk | Trust |
|----------|---------|------|-------|
| **Fail-Open** | "Allow unless we know it's wrong" | Mistakes leak out; wrongful access happens | Low—customers wonder if controls exist |
| **Fail-Closed** | "Deny unless we know it's safe" | Some legitimate requests need review | High—customers know we're careful |

**We use Fail-Closed** because customer data protection matters more than convenience.

---

## Core Principles

### Principle 1: Deny by Default

When the system encounters any access request or action, the default answer is **"No"** until proven safe.

**Example:**
```
Customer requests: "Download my support history as a CSV file"

System logic:
1. Is customer identity verified? ✓ Yes
2. Do they own this account? ✓ Yes
3. Is this data sensitive? ✓ Yes (chat history)
4. Is export allowed for this data type? ✗ Not explicitly approved
5. DEFAULT: DENY

Result: "Request blocked. Need manager approval for export."
```

### Principle 2: Explicit Approval Required

An action is only allowed if:
1. It matches a policy
2. The actor has authorization
3. The data classification permits it
4. Risk assessment says "go"

Assumptions or guesses don't count.

**Example:**
```
Support agent thinks: "I should probably review this customer's payment info"

System response:
- Agent has permission to VIEW customer data? ✓ Yes
- Agent has permission to ACCESS PAYMENT INFO? ✗ Not explicitly
- Agent's role has PCI certification? ✗ No
- DEFAULT: DENY

Result: "Access denied. Only PCI-certified staff can view payments."
```

### Principle 3: Uncertainty = Escalation

When the system cannot confidently make a decision, it **closes the door** and asks a human.

**Example:**
```
AI analyzing a refund request: "This customer wants a $1,500 refund"

AI confidence check:
- Is this within my authority? ✗ Unclear (threshold is $500)
- Is the reason valid? ✓ Product defect (confidence 85%)
- Are there red flags? ~ Maybe (customer has 3 refunds in 30 days)
- CONFIDENCE: 60% (below 75% threshold)

DEFAULT: DENY and ESCALATE

Result: "Refund request flagged for human review. Awaiting supervisor approval."
```

### Principle 4: Specific > General

More specific policies override general ones, and both default to "no" if there's any conflict.

**Example:**
```
Policy A: "Support staff can view customer contact info"
Policy B: "Payment info cannot be exported to personal email"
Policy C: "Contractors cannot access customer data"

Request: Contractor wants to export customer email list

Resolution:
- Policy A says: Allowed
- Policy C says: Denied
- CONFLICT: More specific rule (Policy C) wins
- Result: DENY. Contractors cannot access customer data, even if staff can.
```

---

## How Fail-Closed Applies to the Customer Support Assistant

### 1. Access Control: "Can this person see this data?"

**Fail-Closed Rule:** If permission is not explicitly granted, deny access.

#### ✅ Examples of Denial

| Scenario | Why Denied | Resolution |
|----------|-----------|-----------|
| Support agent views customer's recent payment info | Agent lacks PCI certification | Request training & recertification |
| New hire accesses knowledge base | Not yet verified/trained | Complete onboarding first |
| Contractor exports customer email list | Policy forbids contractor data access | Use approved data service instead |
| External auditor requests system logs | Not authenticated to this system | Formal audit request with IT approval |
| Support agent searches for specific customer by SSN | SSN searches are restricted (fraud risk) | Use approved customer ID lookup |

#### ✅ Example of Approval

```
Support agent views customer's recent ticket history

System check:
✓ Agent verified and authenticated
✓ Agent role: Support Tier 1
✓ Data requested: Ticket history (low sensitivity)
✓ Time of access: Business hours, office location
✓ Pattern: Normal (not unusual spike)
✓ All checks passed

Result: ALLOW. "Displaying ticket history..."
```

---

### 2. Action Approval: "Should we do this action?"

**Fail-Closed Rule:** If the action's safety cannot be confirmed, deny and escalate.

#### ✅ Refund Request Examples

| Amount | Reason | Confidence | Decision | Path |
|--------|--------|-----------|----------|------|
| $50 | Duplicate charge | 95% | ALLOW | AI approves automatically |
| $200 | Product defect | 80% | ALLOW | AI approves (within authority) |
| $500 | Customer unhappy | 65% | DENY | Escalate to supervisor |
| $1,000+ | Claim fraud | 40% | DENY | Escalate to manager + legal |

#### ✅ Escalation Request Examples

| Situation | AI Confidence | Decision |
|-----------|---------------|----------|
| Customer very angry, threatening to sue | 40% | DENY and ESCALATE (high risk) |
| Complex technical issue, multiple possible causes | 55% | DENY and ESCALATE (uncertain) |
| Customer requesting custom feature outside scope | 70% | DENY and ESCALATE (judgment call) |
| Clear duplicate invoice error, standard refund | 95% | ALLOW (high confidence, within authority) |

---

### 3. Data Deletion: "Should we delete this data?"

**Fail-Closed Rule:** If we're not 100% certain deletion won't cause problems, require human approval.

#### ✅ Deletion Request Scenarios

| Request | Status | Why |
|---------|--------|-----|
| Customer GDPR right to be forgotten | ESCALATE | Legal requirement; must verify identity; affects billing, support history, analytics |
| Customer wants to delete chat history | ESCALATE | Need to confirm: audit trail impact? Legal holds? Active disputes? |
| Support agent deletes customer note | DENY | Agents cannot delete (destroys audit trail); must archive instead |
| System auto-delete old logs after 7 years | ALLOW | Matches policy and retention schedule |

**Key principle:** When in doubt about deletion impact, keep the data and ask a human. Deleting data is irreversible.

---

### 4. Escalation Decisions: "Does this need human review?"

**Fail-Closed Rule:** When uncertainty is high, escalate. When risk is present, escalate.

#### ✅ Escalation Examples

| Signal | Confidence | Action |
|--------|-----------|--------|
| Customer payment method invalid | 95% confidence | ALLOW (standard retry notification) |
| Customer account shows suspicious activity pattern | 70% confidence | ESCALATE (security risk) |
| AI cannot determine if issue is product bug or user error | 50% confidence | ESCALATE (uncertain) |
| Potential data breach detected | 30% confidence | ESCALATE IMMEDIATELY (critical risk) |

---

### 5. Data Export: "Should we download this data?"

**Fail-Closed Rule:** Exporting data is high-risk. Requires explicit authorization and approval.

#### ✅ Export Decision Matrix

| Export Type | Status | Reason |
|-------------|--------|--------|
| Agent exports single ticket to share with customer | DENY | No export without approval (data leak risk) |
| Manager exports team performance metrics (anonymized) | ALLOW | Pre-approved export for reporting |
| Contractor requests all customer emails in CSV | DENY | Contractors cannot access customer PII |
| Compliance officer exports audit trail for external audit | ALLOW | Authorized export, requires encryption & watermarking |
| Support agent emails customer summary to customer | DENY | Email = unencrypted export (use secure portal instead) |

---

### 6. Configuration Changes: "Should we change system settings?"

**Fail-Closed Rule:** Any configuration change that affects governance, policy, or security requires approval.

#### ✅ Configuration Examples

| Change | Current Setting | Proposed | Decision |
|--------|-----------------|----------|----------|
| Increase refund approval threshold | $500 | $1,000 | DENY without approval (impacts controls) |
| Add new user to "Admin" role | None | john@company.com | DENY unless explicitly approved by manager |
| Change escalation timeout from 2hrs to 4hrs | 2 hours | 4 hours | DENY (might violate SLA) |
| Disable HITL escalation for refunds < $100 | Enabled | Disabled | DENY (removes human oversight) |
| Enable audit trail exports to external system | Disabled | Enabled | DENY (must go through formal vendor assessment) |

---

## What Fail-Closed Looks Like in Practice

### Real-World Scenario: A Customer Support Request

**Day 1: Customer submits request**
```
Customer: "I'd like a refund for order #54847. The product was defective."

System Actions:
1. Verify customer identity ✓
2. Locate order #54847 ✓
3. Confirm defect claim (review photos) ✓ Quality confirmed
4. Calculate refund amount: $287
5. Check authorization threshold: $500 (within limit) ✓
6. Check refund rate: 2 refunds in 90 days (normal) ✓
7. Check for fraud indicators: None ✓
8. Calculate confidence: 92% (HIGH)

Decision: APPROVE
Result: Refund processed automatically with customer notification
Timeline: 5 minutes
```

**Same scenario if product was more expensive:**

```
Customer: "I'd like a refund for order #98734. The product was defective."

System Actions:
1. Verify customer identity ✓
2. Locate order #98734 ✓
3. Confirm defect claim: Customer provides 3 photos ✓
4. Calculate refund amount: $1,850
5. Check authorization threshold: $500 (EXCEEDS limit) ✗
6. Check previous refunds: 1 refund in 90 days (normal) ✓
7. Check for fraud indicators: None detected ✓
8. Calculate confidence: 78% (meets threshold BUT exceeds authority)

Decision: ESCALATE (not because of risk, but because amount exceeds authority)
Result: Escalated to manager for approval
Timeline: Awaiting manager review (SLA: 2 hours)

Manager Review:
- Checks order history: Long-time customer, good reputation ✓
- Reviews defect photos: Clearly defective ✓
- Approves refund ✓

Result: Refund approved and processed
Timeline: 45 minutes total
```

---

## Benefits of Fail-Closed

### ✅ For Customers
- **Data Protection:** Their information isn't accessed unless truly authorized
- **Trust:** They know the company is careful and cautious
- **Clarity:** They understand why certain requests need review
- **Fairness:** Everyone follows the same strict rules

### ✅ For the Company
- **Compliance:** Meets regulatory requirements (GDPR, SOX, PCI)
- **Liability Protection:** Documented caution in the face of uncertainty
- **Risk Reduction:** Fewer "oops" moments where bad decisions leak out
- **Audit Success:** Regulators see evidence of strong controls
- **Employee Clarity:** Staff know exactly when to say "no"

### ✅ For the AI System
- **Measurable:** Clear policies (allow/deny) are easier to enforce
- **Auditable:** Every denial is logged for review
- **Improvable:** Denials show where policies need clarification
- **Trustworthy:** Makes AI safer by preventing autonomous mistakes

---

## When Fail-Closed Creates Friction

Fail-closed means some legitimate requests require human approval. **This is intentional.**

### Example: Frustrated Customer

```
Customer: "I just need my password reset. Why do I need a manager approval?"

Answer: We don't require manager approval for password resets.
Your request was flagged because:
- You're accessing from a new device
- You're in a different country than usual
- You requested access 3 times in 10 minutes

We escalate to prevent account takeover. Once verified, it takes 5 minutes.
This friction protects you from hackers.
```

### How to Minimize Friction

1. **Clear policies:** Document what IS allowed (most things)
2. **Efficient escalation:** Escalated requests reviewed in minutes, not hours
3. **Customer education:** Explain WHY we're cautious
4. **Feedback loops:** Adjust thresholds if too many legitimate denials
5. **Automated fast-track:** Pre-approve common low-risk scenarios

---

## Exceptions to Fail-Closed

**Rare situations** where fail-closed doesn't apply:

| Exception | Why | Example |
|-----------|-----|---------|
| **Life Safety** | Protect human life first | Customer expresses self-harm risk → immediate escalation to crisis team |
| **Emergency Override** | Authorized personnel in genuine emergencies | Data center fire → unlock customer data access for recovery team |
| **Regulatory Compliance** | When law requires action | Law enforcement warrant for data → provide data (fail-closed doesn't override law) |

**Rule:** Exceptions require explicit approval and are logged/audited immediately.

---

## Failure Scenarios: What Happens When Policies Break

### Scenario 1: "Fail-Closed Failed" (Access Denied Wrongly)

```
Employee: "I can't access the reports I need for my job"

Diagnosis:
- Policy was too restrictive
- System denied legitimate access
- Request sat in escalation queue too long

Resolution:
- Temporary: Manual approval by manager
- Permanent: Policy reviewed and adjusted
- Learning: Update guidance to prevent recurrence
```

**Lesson:** Deny conservatively, but review denials to improve accuracy.

---

### Scenario 2: "Fail-Closed Worked" (Access Denied Correctly)

```
Attacker: Attempts to access customer payment data using stolen credentials

System:
1. Detects unusual access pattern (multiple failed attempts)
2. Applies fail-closed: DENY even though credentials are valid
3. Escalates to security team
4. Security team investigates and discovers account compromise

Result: Attack blocked. Fail-closed saved us.
```

---

## Implementation Checklist

- [ ] Define explicit permission lists (not deny lists)
- [ ] Document what IS allowed (more useful than what's forbidden)
- [ ] Set up escalation queue for borderline decisions
- [ ] Train staff on "When in doubt, escalate"
- [ ] Establish SLA for escalation review (target: < 1 hour)
- [ ] Monitor deny rates; adjust policies if >20% legitimate denials
- [ ] Audit all denials monthly to find false negatives
- [ ] Document exceptions and approval chains
- [ ] Create fast-track approvals for common safe scenarios
- [ ] Communicate policy to customers (transparency)

---

## Key Takeaways

1. **Fail-Closed = Deny by Default:** When uncertain, close the door
2. **Explicit > Implicit:** Only allow what's explicitly approved
3. **Escalate Early:** Doubt means human review, not AI guessing
4. **Audit Everything:** Log denials; they teach us what policies need adjusting
5. **Protect First, Convenience Second:** It's okay if some requests take 5 minutes to approve
6. **Transparent:** Tell customers why we're cautious; most appreciate it

---

## Governance Framework Reference

This fail-closed policy supports the broader governance framework:
- **ABAC Policy:** Granular attribute-based access decisions
- **HITL Escalation Framework:** Uncertain cases go to humans
- **Audit Trail Specification:** All allow/deny decisions logged
- **Governance Scoring Matrix:** Fail-closed is core to our 3.4/5 governance score

---

## Document Control

| Version | Date | Changes | Owner |
|---------|------|---------|-------|
| 1.0 | Oct 3, 2026 | Initial policy | Security & Governance |
| TBD | Jan 3, 2027 | Quarterly review | Governance Team |

**Last Updated:** October 3, 2026  
**Status:** Active  
**Distribution:** Internal (all staff)
