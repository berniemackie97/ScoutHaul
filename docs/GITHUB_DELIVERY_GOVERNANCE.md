# ScoutHaul — GitHub Delivery Governance

Baseline: \`main\` at e5780def1ebb9c1f74ac4b822107a621078eaeb4 (2026-11-06). This is an audit-informed **planning** contract, not proof of production readiness.

## Canonical tracking

- [Production-readiness roadmap #276](https://github.com/berniemackie97/ScoutHaul/issues/276) owns existing architecture/security/performance/quality/operations work (#277–#309).
- [Product-delivery roadmap #317](https://github.com/berniemackie97/ScoutHaul/issues/317) owns new, separately scoped capabilities (#318–#339).
- One GitHub Project, **ScoutHaul Delivery**, is the execution portfolio. Use one project field as the canonical status, horizon, priority and workstream. Do not replicate those values across titles and multiple labels.
- Hierarchy: program roadmap → epic → executable feature/story/bug/task/spike. Actual GitHub parent/sub-issue links are preferred to text references. A PR is not a substitute for an issue or a completed acceptance gate.
- Link a blocking issue through a native dependency when available; otherwise cite the dependency explicitly in the issue description.
- Personal repositories do not have organization-level custom issue types. Use the \`type:\` labels and Issue Forms instead; do not claim native issue types were configured.

## Release horizons (not arbitrary calendar deadlines)

| Horizon | Meaning | Exit |
| --- | --- | --- |
| G0 | Trust and safe operations | Critical security/data-integrity risks closed; reproducible CI, observability and smoke evidence |
| G1 | Stock and financial operations | Sourcing→stock→channels→sale, reconciliation and financial controls proven end to end |
| G2 | Customer and service commerce | Direct checkout, shipping, customer communication, repair/consignment acceptance proven |
| G3 | Measured scale and operator efficiency | Operational KPIs, stock tooling and latency/UX budgets based on measured use |
| Parked | Explicit non-goals or unapproved expansion | Not scheduled, not a release gate |

\`P0\`–\`P6\` in #276 are **historical workstream phases**, not priority values. Do not infer priority from that prefix.

## Project fields

| Field | Type | Allowed values |
| --- | --- | --- |
| Status | Built-in single select | Todo, In Progress, Done (do not move Done before closure evidence) |
| Horizon | Single select | G0, G1, G2, G3, Parked |
| Priority | Single select | P0, P1, P2, P3 |
| Workstream | Single select | engineering, security, finance, performance, architecture, ux, operations, sourcing, inventory, channels, commerce, services, mobile |
| Evidence | Single select | Needs baseline, Reproduced, Independently tested, Accepted |
| Size | Single select | S, M, L (only after issue decomposition; never guess story points) |

\`Priority\`: P0 is active exploit/data exposure/double-charge/data-loss or similarly urgent incident; P1 blocks an accepted release gate; P2 is normal scheduled business value; P3 is optional/deferred. A security issue can be P0 while its workstream is P1 in the old hardening sequence.

\`Evidence\` describes verification, not project delivery status. Enter it only after actual evidence exists. No false Done states from a merged partial PR.

Labels are for repository-wide classification: \`type:roadmap|epic|feature|story|bug|task|spike\`, \`program:hardening|product\`, \`area:...\`, \`scope:parked\` and \`risk:critical|high|medium\` where supported by the finding. Avoid separate priority labels that can drift from the project field.

## Useful project views

1. **Triage**: open issues, grouped by Horizon, ordered by Priority; unprioritized first.
2. **Current delivery**: board by Status, limited to G0/G1 and explicitly active G2 work.
3. **Product roadmap**: table grouped by Horizon with parent epics and child progress.
4. **Release gates**: hardening issues with evidence/priority and open blockers visible.
5. **Security and defects**: filtered security and bug issues, sorted by priority.
6. **Parked**: only the Parked horizon; none scheduled by default.
7. **Closed evidence**: Done issues and their PR/test proof, retained for audit.

Use GitHub built-in auto-add for new repo issues/PRs if it can be set without hiding triage. Auto-close/Done workflows must follow **acceptance completion**, not merely any merged PR.

## Issue workflow

1. **Audit** the current implementation, recent PRs, tests, migrations, specs, affected users, and duplicate issues. Record verified evidence and unknowns. Do not guess defects from filenames.
2. **Classify** the work as bug, feature/story, task or spike; connect it to the existing epic and horizon. Split only where a deliverable can be independently accepted. Do not create an issue for every helper or screen.
3. **Plan** an end-to-end user outcome, failure boundaries, data/permission effects, real dependencies and non-goals. Security/financial blocks can interrupt release sequencing.
4. **Implement** a coherent PR with tests grounded in real behavior, a migration/rollback strategy if applicable, and documentation that matches shipped behavior.
5. **Re-audit** correctness, layering, duplication, concurrency, tenant isolation, secrets/PII, money/tax/time, performance, error disclosure, partial failure and accessibility. Fix review findings rather than adding speculative wrappers.
6. **Verify** exact commands and results. Record skipped environments, browser/device tests and production constraints. Do not probe live marketplaces to fabricate fixture evidence.
7. **Review and merge** only after required CI and review gates pass. Resolve review threads after the finding is addressed, without review-thread chatter.
8. **Close** the issue only when all acceptance checks are demonstrated. Update native sub-issue progress, epic gates and the release portfolio.

## Release and recovery rules

- No invented milestone due dates. Milestones label exit checkpoints; dates appear only after release planning and capacity are confirmed.
- Money/unique-item changes require golden examples, idempotency/retry/partial failure tests and a traceable correction path. Actual marketplace prices and sales remain distinct from estimates and asking prices.
- Every marketplace write/automation must be explicit opt-in, default off, with honest supported-capability reporting.
- Tenant, customer and extension agent permissions are independent; test cross-tenant read/write and access to photos, imports, sync payloads and reports.
- Database migration and deployment are separate audited actions; every release defines restore and rollback behavior. Backup existence alone is not a restore test.
- Documentation and issue status never overstate test, staging, production or customer validation.

## Account and integration limitations

The repository is private under a personal account. Its \`main\` branch was not protected at the audited commit, and GitHub returned a plan restriction when rulesets were queried. Enforced review/status-check restrictions require eligible GitHub account features; governance docs and PR checkboxes are **not** an enforcement substitute.

The connected GitHub integration supports issues, labels and pull requests, but not creating GitHub Projects, custom fields, milestones, native sub-issues, workflow configurations or repository rules. Those steps require a GitHub Project-capable user token/CLI or manual GitHub settings. Verify completion individually rather than marking unavailable features as created.

## Maintenance

Review the board after each merged delivery PR and before every release decision. Deprecate obsolete plans by linking the replacement issue; avoid parallel roadmaps. Reassess priorities using current production risk and product need, not old issue numbering.
