$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

codex --dangerously-bypass-approvals-and-sandbox -C $repoRoot
