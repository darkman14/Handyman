---
id: BUG-0001
title: Referral code uniqueness is bypassed by tab, newline or non-breaking-space padding
story: US-0012
criterion: AC4
severity: low
status: fixed
found-by: qa-api
---

## Steps to reproduce
1. Migrate a Postgres to UniqueConstraints.
2. Insert a store with referral_code `VRACAR01`.
3. Insert another store with referral_code `<TAB>vracar01`, or `vracar01<NBSP>`, or `VRACAR01<LF>`.

## Expected
Refused with unique violation (23505), like `vracar01` and ` vracar01 ` (the story's edge case: surrounding whitespace).

## Actual
Insert succeeds. The index is `lower(btrim(referral_code))`; `btrim` removes only ASCII spaces, so other whitespace makes a distinct key. A flyer code pasted from a web page or SMS can carry such characters.

## Evidence
- Failing test: `US_0012_QaExploratoryTests.Referral_code_spelling_variants_are_refused` (cases `\tvracar01`, `vracar02<NBSP>`, `VRACAR05\n`). Plain spaces, case and non-ASCII case (`VRAČAR`) pass.

## Suggested fix (optional)
Normalize (trim all whitespace, upper-case) in the write path (E09 / store seeding) and/or use `lower(regexp_replace(referral_code, '\s', '', 'g'))` in the index. Low severity because only the app writes codes, and a US-0012 criterion only names case; an app-level normalizer would close it. Alternatively drop the whitespace cases from the test if the team decides spaces only are in scope.
