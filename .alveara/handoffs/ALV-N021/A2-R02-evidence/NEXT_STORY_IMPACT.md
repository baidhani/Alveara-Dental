# ALV-N021 A2-R02: next-story impact

Unchanged from A2-R01-evidence/NEXT_STORY_IMPACT.md. Added: the culture guard now also scans the chain host and non-exact date parses, so any new Parse/TryParse of a date in src/Alveara.Api or src/Alveara.Api.ChainHost must name the invariant culture (or be a reviewed localized-display exception).
