// ALV-N026 S5: a statement or a receipt, from its issued content (rebuilt from the ledger and checked against its hash before this runs) and display values the server worked out.
// Nothing here is computed from current data. Amounts are the content's own two-place text.
#let data = json("input.json")
#let doc = if data.statement != none { data.statement } else { data.receipt }
#let opt(d, key) = d.at(key, default: none)
#let money(v) = if v == none { "—" } else if v.starts-with("-") { "-$" + v.slice(1) } else { "$" + v }

#set document(title: data.kind + " " + str(data.documentNumber))
#set page(paper: "us-letter", margin: (x: 18mm, top: 16mm, bottom: 20mm), footer: context [
  #set text(size: 7.5pt, fill: luma(90))
  #data.kind #data.documentNumber · template version #data.templateVersion · issued #data.issued · produced #data.produced
  #h(1fr) page #counter(page).display() of #counter(page).final().first()
])
#set text(font: "Noto Sans", size: 9.5pt)
#set table(stroke: 0.4pt + luma(170), inset: 4pt)

#if opt(data, "legacyLabel") != none [
  #block(width: 100%, inset: 6pt, stroke: 1pt + black)[#text(weight: "bold")[#data.legacyLabel]]
]

#let practice = opt(doc, "practice")
#grid(columns: (1fr, auto), gutter: 8pt,
  [
    #if practice != none [
      #text(weight: "bold", size: 12pt)[#practice.name] \
      #if opt(practice, "addressLine") != none [#practice.addressLine \ ]
      #if opt(practice, "phone") != none [Phone: #practice.phone]
    ]
  ],
  align(right)[
    #text(weight: "bold", size: 15pt)[#data.kind #data.documentNumber] \
    As of #data.dates.asOf
    #if opt(data, "replacesNumber") != none [\ Replaces receipt #data.replacesNumber]
  ],
)

#v(6pt)
#let patient = doc.patient
Patient: *#patient.name*#if opt(patient, "mrn") != none [ · MRN #patient.mrn]
#if data.statement != none and not doc.addressee.isSelf [ \ Statement for: *#doc.addressee.name* (responsible party)]
#if data.receipt != none [ \ Received from: *#doc.receivedFrom.name*]
#v(8pt)

#if data.statement != none [
  #let item(l) = {
    let parts = (l.kind,)
    if opt(l, "procedureCode") != none { parts.push(l.procedureCode + " " + l.procedureDescription) }
    if opt(l, "method") != none { parts.push(l.method) }
    if opt(l, "reason") != none { parts.push(l.reason) }
    if opt(l, "reversesNumber") != none { parts.push("reverses " + str(l.reversesNumber)) }
    if opt(l, "refundsNumber") != none { parts.push("refunds " + str(l.refundsNumber)) }
    if opt(l, "replacesNumber") != none { parts.push("replaces " + str(l.replacesNumber)) }
    if opt(l, "reversedByNumber") != none { parts.push("reversed by " + str(l.reversedByNumber)) }
    parts.join(" · ")
  }
  == Account activity
  #table(columns: (auto, auto, 1fr, auto, auto),
    table.header([*No.*], [*Date*], [*Item*], [*Amount*], [*Balance*]),
    ..doc.lines.map(l => (str(l.number), data.dates.at("line" + str(l.number)), item(l), align(right, money(l.amount)), align(right, money(l.balanceAfter)))).flatten())

  #if doc.openCharges.len() > 0 [
    == Open charges
    #table(columns: (auto, auto, 1fr, auto, auto, auto, auto),
      table.header([*No.*], [*Service date*], [*Procedure*], [*Charge*], [*Paid*], [*Open*], [*Age*]),
      ..doc.openCharges.map(c => (str(c.number), data.dates.at("service" + str(c.number)),
        if opt(c, "procedureCode") != none { c.procedureCode + " " + c.procedureDescription } else { "—" },
        align(right, money(c.amount)), align(right, money(c.paid)), align(right, money(c.open)), str(c.ageDays) + " days (" + c.bucket + ")")).flatten())
  ]

  == Amount owed by age
  #table(columns: (1fr, 1fr, 1fr, 1fr),
    table.header([*Current*], [*31-60 days*], [*61-90 days*], [*Over 90 days*]),
    money(doc.aging.current), money(doc.aging.days31To60), money(doc.aging.days61To90), money(doc.aging.over90))

  #align(right)[
    #table(columns: (auto, auto), stroke: none,
      [Open charges], align(right, money(doc.openChargeTotal)),
      [Unapplied payments], align(right, money(doc.unappliedTotal)),
      ..if opt(doc, "adjustmentTotal") != none { ([Adjustments], align(right, money(doc.adjustmentTotal))) } else { () },
      [*Balance*], align(right)[*#money(doc.balance)*])
  ]
]

#if data.receipt != none [
  == Payment received
  #table(columns: (auto, 1fr), stroke: none,
    [Payment number], str(doc.paymentNumber),
    [Received], data.dates.received,
    [Amount], [*#money(doc.amount)*],
    [Method], if opt(doc, "method") != none { doc.method } else { "—" })

  #if doc.applications.len() > 0 [
    == Applied to
    #table(columns: (auto, 1fr, auto),
      table.header([*Charge*], [*Procedure*], [*Amount*]),
      ..doc.applications.map(a => (str(a.chargeNumber), if opt(a, "procedureCode") != none { a.procedureCode + " " + a.procedureDescription } else { "—" }, align(right, money(a.amount)))).flatten())
  ]

  #table(columns: (auto, auto), stroke: none,
    [Not yet applied], align(right, money(doc.unapplied)),
    [Refunded to date], align(right, money(doc.refundedToDate)),
    [*Balance owed*], align(right)[*#money(doc.balanceOwed)*])
]
