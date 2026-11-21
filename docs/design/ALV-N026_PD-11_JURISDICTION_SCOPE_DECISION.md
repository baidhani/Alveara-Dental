# ALV-N026 — PD-11 Prescription Jurisdiction Scope Decision

**Owner decision:** 2026-11-20  
**Status:** approved scope direction for ALV-N026; no implementation change is authorized by this document alone.

## ALV-N026 scope

ALV-N026 will research and implement a **US federal/common baseline** for prescription print content. Prescriber fields must come from actual staff-profile data; the implementation must not invent identifiers or claim that the resulting output is compliant with every US state or every other country.

The delivered product must state its jurisdictional boundary accurately. Controlled-substance workflows and state-specific requirements are not assumed satisfied merely because a prescription record can be printed.

Avoid unnecessary US-only hard-coding in the data model and print-template implementation. Where practical, use neutral field concepts such as prescriber credential, issuing jurisdiction, and template field rather than permanently baking a label into storage. This does **not** authorize a generic compliance-rules engine in ALV-N026.

## Deferred follow-on: prescription jurisdiction profiles

A separate, post-N026 engineering story must be planned before the application is represented as supporting prescription baselines outside the selected US scope. That story will define versioned, reviewed jurisdiction profiles, including:

- deployment country and, where required, state/province/region;
- required and optional prescriber identifiers and credential labels;
- print-template fields and required notices;
- validation/availability rules appropriate to a specifically reviewed profile; and
- how a new or changed profile is reviewed, versioned, activated, and audited.

Administrators may eventually configure presentation text and selected template options within an approved profile. They must not be given unrestricted free-text control over legal/compliance requirements or a way to label an unreviewed configuration as compliant.

## Required ALV-N026 briefing content

The coding agent must include this decision in the ALV-N026 pre-implementation briefing, identify the precise US sources and baseline selected, and state what remains excluded. If the planned design introduces a jurisdiction profile, state-specific requirement, controlled-substance assertion, or compliance claim beyond this decision, stop for independent review before code.
