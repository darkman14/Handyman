# Backlog

The product-owner agent maintains this folder; developers and QA agents read it; QA agents also add bugs and QA results. Humans review and approve.

## Structure

```
docs/backlog/
  board.md                         index of every story and bug with status
  _templates/                      epic.md, feature.md, story.md, bug.md
  E04-requests/
    README.md                      epic
    F04.1-post-request.md          feature
    US-0007-client-posts-request.md
  bugs/
    BUG-0003-bidder-sees-house-number.md
```

- Epics map 1:1 to the build slices in `docs/workflow.md` section 7 (E01 = slice 1, …).
- IDs never change and are never reused. Next free number = highest in `board.md` + 1.

## Statuses

| Status | Meaning | Set by |
| --- | --- | --- |
| draft | Written, but open questions remain | product-owner |
| ready | Meets the Definition of Ready | product-owner, approved by a human |
| in-progress | Being built | /deliver |
| in-qa | Built and integrated; QA running | /deliver |
| done | Meets the Definition of Done in `CLAUDE.md` | /deliver |
| blocked | Can't continue; reason in the story | anyone |

Bugs: `open` → `fixed` → `verified` (or `wont-fix`, human decision).

## Definition of Ready

- Story format, value clear, fits in one sitting.
- Numbered Given/When/Then acceptance criteria, including failure paths and the business rules that apply.
- UI notes for every screen state it touches; out of scope listed.
- Test notes name the seeded users to use.
- No open questions.

## Severity

| Severity | Example |
| --- | --- |
| critical | Data leak (bidder sees full address), security hole, data loss, app unusable |
| high | An acceptance criterion fails, no workaround |
| medium | Criterion passes but with a wrong state, message or edge case |
| low | Cosmetic, copy, minor UX |
