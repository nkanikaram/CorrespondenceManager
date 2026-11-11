<#
Removes all *.slnx files under the repo root.
- Lists all found .slnx files and indicates if each file is tracked by git.
- Prompts to delete untracked files.
- Prompts to remove tracked files via 'git rm' and commits the removal locally (no push).
Usage:
  1) Save this script to: C:\ECCO\Repos\ECCO-CommunicationManager\scripts\remove-slnx-files.ps1
  2) Open PowerShell and run:
     cd "C:\ECCO\Repos\ECCO-CommunicationManager\scripts"
     Unblock-File .\remove-slnx-files.ps1
     Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
     .\remove-slnx-files.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# === Configuration ===
$RepoRoot = "C:\ECCO\Repos\ECCO-CommunicationManager"

if (-not (Test-Path $RepoRoot)) {
    Write-Error "Repo root not found: $RepoRoot"
    exit 1
}

Push-Location $RepoRoot
try {
    # Find .slnx files
    $slnxFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter *.slnx -File -ErrorAction SilentlyContinue

    if (-not $slnxFiles -or $slnxFiles.Count -eq 0) {
        Write-Host "No .slnx files found under $RepoRoot"
        return
    }

    # Separate tracked vs untracked
    $tracked = @()
    $untracked = @()

    foreach ($f in $slnxFiles) {
        # compute path relative to repo root for git commands
        $rel = $f.FullName.Substring($RepoRoot.Length).TrimStart('\','/')
        # git accepts both forward and back slashes; ensure backslashes for PowerShell on Windows
        $rel = $rel -replace '/','\'

        $isTracked = $false
        try {
            # will exit non-zero if not tracked
            & git ls-files --error-unmatch -- "$rel" > $null 2>&1
            if ($LASTEXITCODE -eq 0) { $isTracked = $true }
        } catch {
            $isTracked = $false
        }

        if ($isTracked) { $tracked += [PSCustomObject]@{ File = $f; Relative = $rel } }
        else { $untracked += [PSCustomObject]@{ File = $f; Relative = $rel } }
    }

    Write-Host "`nFound .slnx files:"
    foreach ($t in $tracked) { Write-Host (" [tracked]   {0}" -f $t.File.FullName) }
    foreach ($u in $untracked) { Write-Host (" [untracked] {0}" -f $u.File.FullName) }

    # Delete untracked files
    if (@($untracked).Count -gt 0) {
        $ans = Read-Host "`nDelete the {0} untracked .slnx file(s)? Type DELETE to proceed" -f @($untracked).Count
        if ($ans -eq 'DELETE') {
            foreach ($u in $untracked) {
                try {
                    Remove-Item -LiteralPath $u.File.FullName -Force
                    Write-Host ("Deleted untracked: {0}" -f $u.File.FullName)
                } catch {
                    Write-Warning ("Failed to delete {0}: {1}" -f $u.File.FullName, $_)
                }
            }
        } else {
            Write-Host "Skipped deleting untracked .slnx files."
        }
    } else {
        Write-Host "`nNo untracked .slnx files to delete."
    }

    # Remove tracked files via git rm and commit
    if (@($tracked).Count -gt 0) {
        Write-Host ""
        $ans2 = Read-Host "Remove the {0} tracked .slnx file(s) from git and commit locally? Type DELETE to proceed" -f @($tracked).Count
        if ($ans2 -eq 'DELETE') {
            # stage removals
            $failed = @()
            foreach ($t in $tracked) {
                try {
                    & git rm -- "$($t.Relative)" 2>&1 | Out-Null
                    Write-Host ("Staged removal: {0}" -f $t.Relative)
                } catch {
                    Write-Warning ("git rm failed for {0}: {1}" -f $t.Relative, $_)
                    $failed += $t
                }
            }
            # commit if there are staged changes
            $status = & git status --porcelain
            if ($status) {
                try {
                    & git commit -m "Remove stray .slnx files" --quiet
                    Write-Host "Committed removal of tracked .slnx files (local commit)."
                    Write-Host "NOTE: this commit was not pushed to any remote."
                } catch {
                    Write-Warning ("git commit failed: {0}" -f $_)
                }
            } else {
                Write-Host "No changes staged for commit (git rm may have failed)."
            }
        } else {
            Write-Host "Skipped removing tracked .slnx files."
        }
    } else {
        Write-Host "`nNo tracked .slnx files found."
    }

    Write-Host "`nOperation complete. Current .slnx files remaining (if any):"
    Get-ChildItem -Path $RepoRoot -Recurse -Filter *.slnx -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }

} finally {
    Pop-Location
}