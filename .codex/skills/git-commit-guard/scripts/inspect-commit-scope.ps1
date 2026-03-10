$ErrorActionPreference = "Stop"

$insideRepo = git rev-parse --is-inside-work-tree 2>$null
if ($LASTEXITCODE -ne 0 -or $insideRepo -ne "true") {
    Write-Error "Not inside a git repository."
}

Write-Host "Repository status:"
git status --short

Write-Host ""
Write-Host "Staged diff summary:"
git diff --staged --stat

Write-Host ""
Write-Host "Unstaged diff summary:"
git diff --stat
