# Claims API: Newman test suite

End-to-end API tests that drive a **running** API over HTTP (74 requests, ~170 assertions).
They complement the in-process xUnit API tests in `backend/tests`: this suite works against any deployed
environment, including Azure, by changing one environment file.

## Run it

```bash
# 1. database + storage, and the API (see the project README)
docker compose up -d
cd backend/src/ClaimsModule.API && dotnet run --launch-profile http

# 2. in another terminal
cd tests/postman
npm install        # first time only
npm test           # builds the collection, then runs it with the CLI reporter
npm run test:ci    # also writes results/newman.xml (JUnit) for CI
```

The suite creates its own claims and needs no cleanup or reset. It can be run repeatedly against the same database.

## What it covers

| Folder | Checks |
|---|---|
| 1. Health & authentication | Logins for each role, wrong password, missing token, `/me` |
| 2. Reference data | Cause codes, peril filter, status transitions, policy search and coverage |
| 3. FNOL intake | Dry-run validation, 422 error shape, claim number format, auto-approved initial reserve, **idempotency replay and conflict**, list filters and paging |
| 4. Claim B | Expired policy warning must be acknowledged; withdrawal needs a reason |
| 5. Claim C | A claim without a policy blocks reserves |
| 6. Lifecycle | Invalid transitions list valid next statuses; last claimant cannot be removed; closure pre-flight |
| 7. Reserve authority | Tiers (≤10k auto, ≤100k supervisor, >100k manager), 403 vs 422, self-approval ban, reject, retract, negative subrogation, adjust, manager override, balances |
| 8. Jobs & audit | Polls until Hangfire has posted every approved reserve, then checks **exactly one GL entry per approval**, ordering, attribution to System |
| 9. Documents | Upload (path traversal sanitised), blocked types, signed download link, tampered signature |
| 10. Close & reopen | Justification rule, role check on reopen, audit events |

## Run against another environment

Copy `local.postman_environment.json`, change `baseUrl` (and the credentials if they differ), then:

```bash
npx newman run claims-api.postman_collection.json -e my-env.postman_environment.json --working-dir .
```

The Hangfire server must be running in that environment, or folder 8 will time out waiting for GL postings.

## Editing the collection

`claims-api.postman_collection.json` is **generated** from `build-collection.mjs`. Edit the script and run
`npm run build:collection` rather than editing the JSON by hand. You can also import the JSON into Postman to explore it.
