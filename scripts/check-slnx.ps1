$RepoRoot = "C:\ECCO\Repos\ECCO-CommunicationManager"
cd $RepoRoot

# List .slnx files
Get-ChildItem -Path $RepoRoot -Recurse -Filter *.slnx -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "FOUND: $($_.FullName)" }

# Check if any of them are tracked by git
git ls-files --others --exclude-standard | Select-String '\.slnx$' -Quiet
if ($LASTEXITCODE -eq 0) { Write-Host "There are untracked .slnx files." } else { Write-Host "No untracked .slnx files found via git ls-files; tracked files will be shown below (if any)." }

# Check tracked (if specific known filename)
if (Test-Path ".\ECCO-CommunicationManager.slnx") {
    git ls-files -- "ECCO-CommunicationManager.slnx" | ForEach-Object { Write-Host "Tracked by git: $_" }
}