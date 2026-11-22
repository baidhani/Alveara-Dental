// ALV-N026 S8: a clinical record copy, generated as of the issue time from the patient's current records (the readable-record content).
// It is never regenerated as historical; every section is a table of plain text.
#let data = json("input.json")
#let opt(d, key) = d.at(key, default: none)

#set document(title: "Clinical record copy")
#set page(paper: "us-letter", margin: (x: 16mm, top: 16mm, bottom: 22mm),
  footer: context [
    #set text(size: 7.5pt, fill: luma(90))
    Clinical record copy · issue #data.issueId · template version #data.templateVersion · produced #data.produced
    #h(1fr) page #counter(page).display() of #counter(page).final().first()
  ])
#set text(font: "Noto Sans", size: 9pt)

#text(weight: "bold", size: 14pt)[#data.title]

*#data.patientName* · MRN: #data.mrn

#table(columns: (auto, 1fr), stroke: none, inset: 3pt,
  ..data.summary.map(f => (f.label, f.value)).flatten())

#for s in data.sections [
  #v(6pt)
  #text(weight: "bold", size: 11pt)[#s.title]
  #if opt(s, "note") != none [ \ #text(size: 8pt, fill: luma(60))[#s.note]]
  #if s.rows.len() == 0 [
    \ None recorded.
  ] else [
    #table(columns: s.headers.len(), stroke: 0.4pt + luma(170), inset: 3pt,
      ..s.headers.map(h => text(weight: "bold")[#h]),
      ..s.rows.flatten())
  ]
]
