// ALV-N026 S6: a prescription (print or record copy) from its issue: the prescription exactly as recorded, the status recorded at issue and the envelope printed then.
// Nothing here is computed from current data. Optional fields are left off when absent (no empty labels).
#let data = json("input.json")
#let opt(d, key) = d.at(key, default: none)

#set document(title: data.kind)
#set page(paper: "us-letter", margin: (x: 18mm, top: 16mm, bottom: 22mm),
  background: if opt(data, "watermark") != none { rotate(-35deg, text(size: 40pt, fill: luma(200), weight: "bold")[#data.watermark]) },
  footer: context [
    #set text(size: 7.5pt, fill: luma(90))
    #data.kind · issue #data.issueId · template version #data.templateVersion · issued #data.issued · produced #data.produced
    #h(1fr) page #counter(page).display() of #counter(page).final().first()
  ])
#set text(font: "Noto Sans", size: 10pt)

#let practice = opt(data, "practice")
#let pr = data.prescriber
#grid(columns: (1fr, auto), gutter: 8pt,
  [
    #if practice != none [
      #text(weight: "bold", size: 12pt)[#practice.name] \
      #if opt(practice, "addressLine") != none [#practice.addressLine \ ]
      #if opt(practice, "phone") != none [Phone: #practice.phone]
    ]
  ],
  align(right)[
    #text(weight: "bold", size: 15pt)[#data.kind] \
    Date of issue: #data.issued.slice(0, 10) \
    Date prescribed: #data.prescribed
  ],
)

#if opt(data, "statusLine") != none [
  #v(4pt)
  #block(width: 100%, inset: 6pt, stroke: 1pt + black)[#text(weight: "bold")[#data.watermark] \ #data.statusLine]
]

#v(6pt)
== Patient
#table(columns: (auto, 1fr), stroke: none, inset: 3pt,
  [Name], [*#data.patient.name*],
  [Date of birth], data.patient.dateOfBirth,
  [MRN], str(data.patient.mrn),
  [Address], data.patient.address)

== Prescription
#table(columns: (auto, 1fr), stroke: none, inset: 3pt,
  [Medication], [*#data.medication*],
  [Dose], data.dose,
  [Frequency], data.frequency,
  [Directions], data.directions,
  [Quantity], data.quantity,
  [Refills], str(data.refills))

// ALV-N026 S8 restoration (owner decision 2026-11-22): the allergy-check outcome recorded when the prescription was written, with the established wording.
// The no-match sentence stays on one line: tests/allergy-wording-guard.test.mjs checks it says "not a clearance".
#let al = data.allergy
// Plain strings, not markup, so the wording prints exactly as established (markup would turn the apostrophes into typographic quotes).
#let allergyText = if al.outcome == "RecordedAllergyMatch" { "At the time of prescribing the medication matched a recorded allergy (" + al.matches + "); the prescriber reviewed it." } else if al.outcome == "NotEstablished" { "At the time of prescribing the patient's allergies were not established; the prescriber confirmed this in their own words." } else if al.outcome == "NoRecordedMatch" { "At the time of prescribing no recorded allergy matched this medication's name. That is a name comparison, not a clearance." } else { none }
#if allergyText != none [
  #v(2pt)
  #text(size: 9pt)[*Allergy check:* #allergyText]
]

== Prescriber
#table(columns: (auto, 1fr), stroke: none, inset: 3pt,
  [Name], [*#pr.name*],
  ..if opt(pr, "credential") != none { ([Credential], pr.credential) } else { () },
  ..if opt(pr, "licenseNumber") != none { ([Licence], pr.licenseNumber + if opt(pr, "licenseJurisdiction") != none { " (" + pr.licenseJurisdiction + ")" } else { "" }) } else { () },
  ..if opt(pr, "npi") != none { ([NPI], pr.npi) } else { () })

#v(18pt)
#line(length: 60%, stroke: 0.6pt)
Prescriber signature

#v(14pt)
#text(size: 8pt, fill: luma(60))[#data.boundary]
