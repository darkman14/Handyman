---
name: plan
description: Plan a build slice or idea into epics, features and user stories with the product-owner agent.
argument-hint: "<slice number from workflow.md section 7, or an idea>"
disable-model-invocation: true
---

Plan: $ARGUMENTS

1. Use the product-owner subagent. Tell it what to plan and remind it to follow `docs/backlog/README.md` and the templates.
2. When it returns, show me a table of the created or changed items (id, title, status) and its open questions.
3. Stop. Don't start any development. I'll review the stories and set them to `ready`.
