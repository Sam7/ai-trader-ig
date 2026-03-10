$ErrorActionPreference = "Stop"

$insideRepo = git rev-parse --is-inside-work-tree 2>$null
if ($LASTEXITCODE -ne 0 -or $insideRepo -ne "true") {
    Write-Error "Not inside a git repository."
}

Write-Host "Changed files:"
git status --short

Write-Host ""
Write-Host "Diff summary:"
git diff --stat

Write-Host ""
Write-Host "Staged diff summary:"
git diff --cached --stat
