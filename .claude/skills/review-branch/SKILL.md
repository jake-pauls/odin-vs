---
name: review-branch
description: Review the current task branch with a read-only review agent, fix trivial findings, bring non-trivial ones back to the user, and open the PR only when nothing needs a decision. Use when the user says "review", "review and open a PR", or "run the review process".
---

# Review a task branch, then open its PR

Follow these steps in order. The git rules in `CLAUDE.md` ("Git workflow") still apply: commits on the task branch are pre-approved, pushes only to `git@github.com:jake-pauls/`, never merge into `main`.

## 1. Review with an agent
Spawn one `general-purpose` agent. It is read-only: it must not edit, commit, build, install, or launch Visual Studio. Give it the branch name, the base (`main`), the instruction to read `CLAUDE.md`, `docs/pitfalls.md` and the relevant `docs/milestones.md` section first, and the scope (what changed, `git diff main...<branch>`). Ask for a concise report in two groups:
- (A) trivial and safe to fix without discussion, each with file:line and the exact fix;
- (B) non-trivial or needing a decision, each with the question that needs answering,
ranked by severity, with "unsure" stated where it is unsure and no padding. Ask it to look for correctness bugs (lifetime, races, exceptions that could hide output or crash VS), `CLAUDE.md` convention violations, dead code, weak or missing tests, doc claims not backed by evidence, and script robustness.

Read the report yourself and sanity-check claims against the code before acting; the agent can be wrong.

## 2. Fix the trivial findings
Trivial means the behaviour does not change and the right answer is not a choice: style and convention nits, `using`/ordering, naming, comment or doc corrections, small refactors that are covered by existing tests, stale statements in docs. Fix them, noting which ones (if any) meet the "Please check" bar in step 4, then run the unit tests (`dotnet test tests/OdinVS.Core.Tests --filter "TestCategory!=RequiresToolchain"`) and the VSIX build, and commit on the task branch.

These are **not** trivial, even when small: anything that changes runtime behaviour or concurrency, public or VS-visible surface, new dependencies, deleting files, doc claims that need new verification, and anything where there is more than one reasonable answer.

## 3. Circle back for the non-trivial findings
Stop and give the user the group (B) items, each as: the issue, file (line), your recommendation, and the one decision needed. Do not open the PR yet and do not guess. After the user answers, make the agreed changes, re-verify what they touch (Exp via `scripts\exp-reinstall.ps1` when VS behaviour changed), and commit.

## 4. Open the PR (only if nothing is left to decide)
1. Restore any sample or fixture files the Exp runs edited (`git status`; never commit them).
2. `git remote get-url origin` must start with `git@github.com:jake-pauls/`; stop if not. `git fetch` from PowerShell with `$env:GIT_SSH_COMMAND = 'C:/Windows/System32/OpenSSH/ssh.exe'` and, if `main` moved, merge it into the branch first.
3. Push from PowerShell, then `gh pr create --base main`. The description says what changed, which `docs/milestones.md` items it satisfies (ticks are proposals until the user merges), what was run (tests, builds, Exp checks) and what was not, and ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
4. Under "Please check", list only the changes that deserve the user's eyes, as `Description of issue. File (line)`. For each change you made during the review, ask: how trivial is it (a typo, a stale comment, a rename, a small fix with the same behaviour), or does it modify functionality directly? Is the change ambiguous or complex, such that a reasonable reviewer could disagree with it? Flag it only if it has some real complexity or a functional change the user may not expect: behaviour-adjacent fixes, a judgement call between options, anything verified only by reading the code, anything you could not re-run. Do not flag trivial changes (typos, wording, formatting, ordering, stale doc statements); the commit list covers those. If nothing qualifies, say so. Give the same list in chat when handing the PR over.
5. After opening the PR, bind it with the ccd_pr tools (`get_status`, then `bind_pr` if it is not reported).

End with the standard report: files changed, commands run, dependencies added, network access used.
