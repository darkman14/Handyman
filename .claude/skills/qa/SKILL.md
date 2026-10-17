---
name: qa
description: Run both QA agents in parallel on the current branch for one story.
argument-hint: "<US-id>"
disable-model-invocation: true
---

Run QA for story $ARGUMENTS on the current branch.

1. Check that Supabase, the API (port 5080) and the web app (port 8081) are running; start the API and web app in the background if needed.
2. Start qa-api and qa-app at the same time, in one message with two subagent calls, each with the story path.
3. Report a table: criterion, api verdict, app verdict, plus the bugs filed (id, severity, title) and both recommendations. Don't fix anything.
