---
id: BUG-0017
title: An empty Seed:Enabled value (Seed__Enabled=) stops the API from starting, in every environment
story: US-0004
criterion: AC2
severity: medium
status: fixed
found-by: qa-api
---

## Steps to reproduce
1. Start the API (Production or Development) with configuration `Seed:Enabled` set to an empty string, e.g. a compose file with `Seed__Enabled: ${SEED_ENABLED:-}` or `Seed__Enabled=`.
2. Watch startup.

## Expected
AC2: "`Seed:Enabled` false or missing" means nothing is seeded and the API starts normally. An empty value should count as missing.

## Actual
`DevSeedStartup.RunAsync` calls `config.GetValue<bool>("Seed:Enabled")`, which throws `InvalidOperationException: Failed to convert configuration value '' at 'Seed:Enabled' to type 'System.Boolean'`. The host never starts, in Production too. The same happens for `1`, `yes` and any other non-boolean (those are fail-safe and loud, which is acceptable; the empty string is the realistic accident).

## Evidence
Test `US_0004_QaExploratoryTests.KNOWN_GAP_BUG_0017_empty_seed_enabled_crashes_startup_instead_of_meaning_missing` pins the current behavior (passes today). When fixed, flip it to assert the host starts and nothing is seeded.

Nothing is ever seeded or migrated by this path, so it is an availability problem only, not a data-safety problem.

## Suggested fix (optional)
Parse tolerantly: `bool.TryParse(config["Seed:Enabled"], out var enabled) && enabled`, and log a warning for a non-empty value that is not a boolean.

## Fix
`DevSeedStartup.RunAsync` now reads `config["Seed:Enabled"]`, trims it and uses `bool.TryParse`. Empty, whitespace or any non-boolean value counts as false: the API starts and nothing is seeded or migrated by this path, in every environment. A non-empty unparsable value logs a Warning (the value itself is not logged). Tests: `Empty_seed_enabled_means_missing_and_starts_without_seeding` and `Seed_enabled_non_boolean_starts_without_seeding_and_logs_a_warning` in `US_0004_QaExploratoryTests`.
