# ALV-N022 A2-R02: demo evidence

`finalized-encounter-real-backend` on `66c92b9` (fresh build including the new migration, fresh database): 1/1, 27 s - the same workflow as A2-R01 (diagnosis form offers only the Draft encounter with the message, the API refuses a diagnosis on the Finalized encounter with nothing written; the completion picker lists both and completing against the Finalized encounter leaves it unchanged), now running with the database guard in place. Screens and JSON in `artifacts/walkthrough/`.
