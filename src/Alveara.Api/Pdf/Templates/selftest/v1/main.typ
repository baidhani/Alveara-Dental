// ALV-N026 S4: the engine self-test. Proves the engine starts, reads input.json and renders with the pinned Noto Sans fonts.
#set page(width: 100mm, height: 30mm, margin: 5mm)
#set text(font: "Noto Sans", size: 10pt)
#let data = json("input.json")
DentalKeep PDF engine self-test: #data.at("text") #text(weight: "bold")[bold]
