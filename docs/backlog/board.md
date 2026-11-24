# Board

| ID | Title | Epic | Status |
| --- | --- | --- | --- |
| US-0001 | API rejects calls without a valid sign-in token | E02 | ready |
| US-0002 | API identifies the caller and refuses blocked users | E02 | ready |
| US-0003 | Seeded users can sign in locally | E02 | ready |
| US-0004 | Seeder runs only where it is allowed | E02 | ready |
| US-0005 | Seeded workers have trades, coverage and approval state | E02 | ready |
| US-0006 | Sample jobs are seeded once | E02 | ready |
| US-0007 | Request a sign-in code by phone | E02 | ready |
| US-0008 | Sign in with the SMS code | E02 | ready |
| US-0009 | Stay signed in and sign out | E02 | ready |
| US-0010 | App calls the API as the signed-in user | E02 | ready |
| US-0011 | Business tables exist in a locked-down app schema | E01 | done |
| US-0012 | The database refuses duplicates that break core rules | E01 | done |
| US-0013 | A job moves only along the allowed lifecycle | E01 | done |
| US-0014 | A job closes only when both sides have rated | E01 | done |
| US-0015 | Commands save all or nothing and push only after saving | E01 | done |
| US-0016 | Every use case has one handler and reads never write | E01 | done |
| US-0017 | Failures come back as problem details without internals | E01 | done |
| US-0018 | Reference data is in place in every environment | E01 | in-progress |
| US-0019 | Anyone can list the trades | E01 | ready |
| US-0020 | Anyone can browse areas level by level | E01 | ready |
| BUG-0001 | Referral code uniqueness bypassed by tab/nbsp/newline padding (US-0012 AC4) | E01 | fixed |
| BUG-0002 | ReadDbContext writes via ExecuteUpdate/ExecuteDelete/ExecuteSql (US-0016 AC4, medium) | E01 | fixed |
| BUG-0003 | AC2 rule misses IServiceProvider / plain DbContext in handlers (US-0016 AC2, low) | E01 | open |
| BUG-0004 | ReadDbContext can write after SELECT set_config(read_only off) on an open connection (US-0016 AC4, low) | E01 | open |
| BUG-0005 | 415, unknown route and 405 come back with an empty body, no problem+json code (US-0017 AC4, low) | E01 | open |
| BUG-0006 | [RegularExpression] validation message echoes the regex pattern (US-0017 AC4, low) | E01 | open |
| BUG-0007 | Nested request objects are not validated (US-0017 AC4, low) | E01 | open |
| BUG-0008 | Job has no concurrency token; real concurrent saves never raise a conflict (US-0017 AC2, medium) | E01 | verified |
| BUG-0010 | Parallel ratings by both parties leave the job Done, not Closed; xmin cannot detect it (US-0017 AC2, medium) | E01 | open (E07) |
| BUG-0009 | Flaky backend tests: container start timeout and a timing-sensitive US_0012 test (test reliability, low) | E01 | open |
