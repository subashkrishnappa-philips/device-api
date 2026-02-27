# Contract Change Runbook

This runbook describes what to do when the DeviceApi swagger contract changes.

---

## When is this runbook needed?

The `SwaggerContractValidatorTests` test in `tests/DeviceApi.Provider.Tests/` FAILS when
`swagger.json` differs from the committed baseline (`contracts/swagger-baseline.json`).

---

## Roles

| Role | Responsibility |
|---|---|
| **API Developer** | Made the change in `src/DeviceApi/`. Must follow steps 1–3 below. |
| **Consumer Team** | Must be notified and update their pact tests (steps 4–7). |
| **CI Pipeline** | Enforces that all steps are complete before merging. |

---

## Steps

### 1. Understand what changed

Run the provider tests and read the diff printed by `SwaggerContractValidatorTests`:

```powershell
cd tests\DeviceApi.Provider.Tests
dotnet test --filter "Category=SwaggerContractValidation"
```

The test output shows a line-by-line diff: `BASE` = committed baseline, `LIVE` = your change.

---

### 2. Run the generator to create consumer stubs

```powershell
.\run-pact-tests.ps1 --generate-only
```

Or run manually:

```powershell
dotnet run --project tools\SwaggerPactGenerator \
  -- --swagger-url http://localhost:5000/swagger/v1/swagger.json \
     --pact-file contracts\DeviceApi-Consumer-DeviceApi.json \
     --consumer-output tests\DeviceApi.Consumer.Tests\Generated \
     --notification contracts\consumer-notification.md
```

This produces:
- Skeleton `*.Generated.cs` files in `tests/DeviceApi.Consumer.Tests/Generated/`
- `contracts/consumer-notification.md` listing teams to notify

---

### 3. Notify consumer teams

Read `contracts/consumer-notification.md` (or the test output) for the list of teams.

Send a communication to each team (email + Slack) with:
- What changed (paste the diff)
- Path to their generated stub: `tests/DeviceApi.Consumer.Tests/Generated/<StubName>.Generated.cs`
- Deadline for them to update their contract tests

Consumer teams are registered in `contracts/consumers.json`. Add new teams there.

---

### 4. Consumer team: review and complete the stub

The generated stub contains `TODO` comments. The consumer team must:

1. Open `tests/DeviceApi.Consumer.Tests/Generated/<StubName>.Generated.cs`
2. Fill in:
   - Request body payload
   - Expected response body assertions (use `Match.Type` for resilience)
3. Move the completed file to `tests/DeviceApi.Consumer.Tests/Contracts/`

---

### 5. Consumer team: regenerate the pact file

```powershell
cd tests\DeviceApi.Consumer.Tests
dotnet test
```

This writes a new `contracts/DeviceApi-Consumer-DeviceApi.json`.

---

### 6. Verify the provider still satisfies all consumer contracts

```powershell
.\run-pact-tests.ps1
```

All three test suites must pass:
- `DeviceApiProviderTests` (pact replay)
- `SwaggerContractValidatorTests` (swagger drift — will still fail until step 7)
- `SwaggerCoverageTests` (all endpoints covered)

---

### 7. Update the swagger baseline

After verifying all tests pass:

```powershell
.\run-pact-tests.ps1 --update-baseline
```

This deletes `contracts/swagger-baseline.json` so it is regenerated from the
live API on the next test run — which will then match and pass.

---

## FAQ

**Q: Can I skip notifying consumers?**
A: No. `SwaggerContractValidatorTests` will fail in CI until the baseline is updated,
which requires all consumer pact tests to pass first.

**Q: What if a consumer is deprecated?**
A: Remove their entry from `contracts/consumers.json` and delete their pact file.

**Q: What if the change is backwards-compatible (additive)?**
A: Additive changes (new endpoints, new optional fields) should not break existing
consumer pacts. However, you must still update the swagger baseline and notify
consumers so they can write tests for the new endpoints.

---

*This runbook lives at `docs/contract-change-runbook.md`.*
