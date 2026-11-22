// ALV-N026 S7: a treatment plan's patient copy from its issue: the recorded revision, the procedure states at the recorded cut-off, the diagnosis labels as of the issue, and the envelope.
// Nothing here is computed from current data. Withdrawn procedures and staff-only text are never in the input.
#let data = json("input.json")
#let opt(d, key) = d.at(key, default: none)

#set document(title: "Treatment plan patient copy")
#set page(paper: "us-letter", margin: (x: 18mm, top: 16mm, bottom: 22mm),
  footer: context [
    #set text(size: 7.5pt, fill: luma(90))
    Patient copy · issue #data.issueId · template version #data.templateVersion · issued #data.issued · produced #data.produced
    #h(1fr) page #counter(page).display() of #counter(page).final().first()
  ])
#set text(font: "Noto Sans", size: 10pt)

#let practice = opt(data, "practice")
#if practice != none [
  #text(weight: "bold", size: 12pt)[#practice.name] \
  #if opt(practice, "addressLine") != none [#practice.addressLine \ ]
  #if opt(practice, "phone") != none [Phone: #practice.phone]
]

#v(6pt)
#text(weight: "bold", size: 14pt)[#data.title]

#data.prepared \
MRN: #data.mrn

#data.explanation

#for g in data.groups [
  #if g.title != "" [
    #v(4pt)
    #text(weight: "bold", size: 11pt)[#g.title]
    #if g.when != "" [ \ #g.when]
  ]
  #if g.items.len() == 0 [
    Nothing in this stage yet.
  ] else [
    #for i in g.items [
      #block(inset: (left: 8pt), below: 8pt)[
        *#i.description*#if opt(i, "site") != none [ (#i.site)] \
        For: #i.diagnosis \
        Estimated fee: #i.fee · #i.answer
      ]
    ]
  ]
]

#v(4pt)
*Estimated total: #data.total*
#if opt(data, "accepted") != none [ \ Of that, the part you have accepted so far: #data.accepted]

#data.estimateNote

#v(8pt)
#text(size: 8.5pt, fill: luma(60))[#data.foot]
