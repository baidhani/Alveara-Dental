# Changed files - ALV-007-C01 R02

Implementation commit `37d2b08420d1b24837052aafddb8994bd186d6da`:
- `M src/Alveara.Api/Architecture/Billing/BillingService.Ledger.cs` - `LoadLedgerAsync` brackets its three queries with the account row version (new private `AccountVersionAsync`, constant `LedgerReadAttempts = 4`, one `using` for the conflict exception). Nothing else in `src/Alveara.Api`.
- `A src/Alveara.Api.Tests/BillingLedgerReadConsistencyTests.cs` - the 5 deterministic tests.

No schema, migration, endpoint, permission, response shape or client file changed.
