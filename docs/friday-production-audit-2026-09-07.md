# Friday production interaction audit — 7 September 2026

## Scope and deployment boundary

Target: production on port 5000 and bix.ghafservices.com, serving commit 1f9b411 at audit time. Port 5102 was an unavailable preview, not the production target. Changes below are source changes pending deployment; this document is not an all-controls acceptance certificate.

No production report deletion, email delivery, portal fetch, credential changes or database maintenance was triggered as a click test. Such controls require fixture-based validation or a separately controlled production operation.

## Verified live

- Local and public /api/health returned HTTP 200 with matching production commit.
- Anonymous dashboard, report and portal routes redirected to login.
- Mobile navigation toggle and six top-level dropdown menus opened.
- All six report date presets responded; custom dates appeared; Reset cleared filters.
- Facility and encounter Select all/Clear worked with 14 and 4 options respectively.
- Dashboard and Claim Activity pages rendered.

## Corrected in source

- Select2 checkbox ticks synchronize when selections change while menus remain open.
- Navigation loading indicators respect modified/prevented clicks and clear after history restoration.
- Durable Processing reports no longer appear Idle when process-local progress is absent. Unknown worker counts are explicitly unavailable rather than fabricated.
- Missing user identity fails closed; report listing, download, delete and clear enforce complete facility scope.
- Pending/processing reports are protected from bulk Clear; Processing deletion is blocked even without a local worker snapshot.
- Report file containment uses a directory boundary rather than an ambiguous string prefix.
- Six dashboard KPI scans consolidated into one database aggregate.
- Nonfunctional report-ID links replaced with text; actual download actions retained.

Combined regression run: 125 passed, 0 failed, 0 skipped (.NET 10 Release). Two additional SQLite-backed dashboard tests passed separately, exercising the actual service with empty and populated data, aggregate translation, decimal totals, period comparison and scope exclusions.

## Outstanding findings

- RCM Submissions navigation exceeded 40 seconds; aggregate consolidation has not yet been timed in production.
- Receiver, payer, clinician and department lookup controls have zero available options. Parsing does not populate those lookup tables; repair needs verified mappings and a bounded backfill.
- Department selections are not applied to report queries. Do not treat department-filtered outputs as validated.
- Deep /healthz exceeded 12 seconds while lightweight /api/health succeeded.
- Portal synchronization is stale for most facilities; this audit did not fetch external records.
- SMTP is unconfigured, so email delivery cannot pass acceptance.
- Navigation timeouts prevented complete route coverage. Write actions, export contents, all error paths and every responsive viewport are not fully validated.

## Rollout gate

Review focused diffs and tests, verify worker activity before restart, preserve deployed configuration and data, stage recoverable application artifacts, then validate the actual deployed version and browser interactions. Do not infer production verification from a passing source test suite.

## Follow-up remediation

- Receiver/payer/clinician code lookups are maintained during parsing. Explicit `--repair-report-lookups` runs separately from the web host with 500-row primary-key pages, a fixed high watermark, a 250ms pause and durable replay-safe checkpoints. It does not migrate or replace the database.
- Department UI is explicitly unavailable until an authoritative mapping exists; forged and legacy department-filtered requests are rejected rather than silently broadened.
- Duplicate suppression compares complete normalized multi-selection sets, template, format, recipients and requester.
- RCM results have a scope-specific 30-second cache and serialized cold builds; query cancellation propagates and a retry page replaces an unhandled timeout. Dropdown option caps were removed. Cold database scans still require live timing.
- `/healthz` now reports process liveness without database scans; `/readyz` reports dependency status without exposing exception details. Unavailable sync checks are Degraded, never falsely Healthy. Ledger-wide counts were removed from health checks.
- SMTP/attachment failures propagate and reports retain a durable `CompletedEmailFailed` status with downloadable output. SMTP configuration is still required for actual delivery.
- 143 regression tests passed before the final queue-progress ownership correction; the release build and final test run validate that correction as well.
- Rollback trigger: startup/health failure, login or report rendering regression, or new database exceptions. Restore the prior DLL/PDB/static files; do not roll back or replace the live database.
