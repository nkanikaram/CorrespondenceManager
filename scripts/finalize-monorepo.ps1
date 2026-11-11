# finalize-monorepo.ps1
<#
Idempotent helper to finalize ECCO-CommunicationManager monorepo structure.

What it does:
- Ensures top-level folders: APIM, Build, CustomerCorrespondence.Functions, CustomerCorrespondence.Orchestrator, CustomerCorrespondence.TestProjects.
- Ensures `CustomerCorrespondence.Functions\Functions` exists and moves any local folders matching "*Function*" into it (prompt before moving).
- Creates `ECCO-CommunicationManager.sln` at repo root if missing and adds all `.csproj` files (excluding anything under `.git`).
- Creates basic `.gitignore` and `README.md` if missing.
- Detects nested `.git` folders (lists them) and prompts to remove each if you confirm (removal irreversible locally).
- Shows final top-level listing and summary.

Run:
powershell -ExecutionPolicy Bypass -File "C:\ECCO\Repos\ECCO-CommunicationManager\scripts\finalize-monorepo.ps1"
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# === Config ===
$RepoRoot = "C:\ECCO\Repos\ECCO-CommunicationManager"
$RequiredTopLevel = @(
    "APIM",
    "Build",
    "CustomerCorrespondence.Functions",
    "CustomerCorrespondence.Orchestrator",
    "CustomerCorrespondence.TestProjects"
)
$FunctionsSubPath = Join-Path $RepoRoot "CustomerCorrespondence.Functions\Functions"
$SolutionName = "ECCO-CommunicationManager.sln"
$SolutionPath = Join-Path $RepoRoot $SolutionName

# === Preconditions ===
Write-Host "Repository root: $RepoRoot"
if (-not (Test-Path $RepoRoot)) {
    Write-Host "Repository root does not exist. Creating: $RepoRoot"
    New-Item -ItemType Directory -Path $RepoRoot | Out-Null
}

# Ensure script runs from repo root context for dotnet sln commands
Push-Location $RepoRoot
try {
    # Create top-level folders if missing
    foreach ($f in $RequiredTopLevel) {
        if (-not (Test-Path $f)) {
            New-Item -ItemType Directory -Path $f | Out-Null
            Write-Host "Created top-level folder: $f"
        } else {
            Write-Host "Exists: $f"
        }
    }

    # Ensure Functions\Functions exists
    if (-not (Test-Path $FunctionsSubPath)) {
        New-Item -ItemType Directory -Force -Path $FunctionsSubPath | Out-Null
        Write-Host "Created: $FunctionsSubPath"
    } else {
        Write-Host "Exists: $FunctionsSubPath"
    }

    # Find candidate Function folders to move into CustomerCorrespondence.Functions\Functions
    $candidates = Get-ChildItem -Path $RepoRoot -Directory -Force | Where-Object {
        ($_.Name -like '*Function*' -or $_.Name -like '*Functions*') -and
        ($_.Name -ne 'CustomerCorrespondence.Functions') -and
        ($_.Name -ne 'CustomerCorrespondence.Orchestrator') -and
        ($_.Name -ne 'CustomerCorrespondence.TestProjects')
    }

    # Also search inside orchestrator folder for stray function folders
    $orchPath = Join-Path $RepoRoot "CustomerCorrespondence.Orchestrator"
    if (Test-Path $orchPath) {
        $orchCandidates = Get-ChildItem -Path $orchPath -Directory -Force -ErrorAction SilentlyContinue | Where-Object {
            ($_.Name -like '*Function*' -or $_.Name -like '*Functions*')
        }
        $candidates += $orchCandidates
    }

    if ($candidates.Count -gt 0) {
        Write-Host "`nDetected the following candidate Function folders to move into `"$FunctionsSubPath`":"
        $candidates | ForEach-Object { Write-Host " - $($_.FullName)" }
        $confirm = Read-Host "`nMove these into CustomerCorrespondence.Functions\Functions? Type Y to proceed"
        if ($confirm -eq 'Y') {
            foreach ($c in $candidates) {
                $dest = Join-Path $FunctionsSubPath $c.Name
                if (-not (Test-Path $dest)) {
                    Move-Item -Path $c.FullName -Destination $FunctionsSubPath -Force
                    Write-Host "Moved: $($c.FullName) -> $dest"
                } else {
                    Write-Host "Target exists, skipping: $dest"
                }
            }
        } else {
            Write-Host "Skipping moving function folders."
        }
    } else {
        Write-Host "No stray Function folders detected at repo root or inside orchestrator."
    }

    # Create or update root solution and add projects
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        if (-not (Test-Path $SolutionPath)) {
            dotnet new sln -n "ECCO-CommunicationManager" | Out-Null
            Write-Host "Created solution: $SolutionPath"
        } else {
            Write-Host "Solution exists: $SolutionPath"
        }

        # Discover .csproj files (exclude any under .git)
        $csprojFiles = Get-ChildItem -Path $RepoRoot -Recurse -Include *.csproj -File -ErrorAction SilentlyContinue | Where-Object {
            $_.FullName -notmatch "\\.git\\"
        }

        if ($csprojFiles.Count -eq 0) {
            Write-Host "No .csproj files found to add to solution."
        } else {
            foreach ($proj in $csprojFiles) {
                # Use relative paths to avoid accidental absolute references
                $relative = Resolve-Path -Path $proj.FullName
                try {
                    dotnet sln $SolutionPath add $relative | Out-Null
                    Write-Host "Added to solution: $($proj.FullName)"
                } catch {
                    Write-Warning "Failed to add project to solution: $($proj.FullName) - $_"
                }
            }
        }
    } else {
        Write-Host "dotnet CLI not found; skipping solution creation and project addition. Install .NET SDK and re-run to add projects to solution."
    }

    # Create minimal .gitignore and README if missing (won't overwrite)
    if (-not (Test-Path ".gitignore")) {
        @"
# Build
bin/
obj/

# Visual Studio
.vs/
*.user
*.suo

# NuGet
*.nupkg
packages/
"@ | Out-File -FilePath ".gitignore" -Encoding utf8
        Write-Host "Created .gitignore"
    } else {
        Write-Host ".gitignore exists"
    }

    if (-not (Test-Path "README.md")) {
        @"
# ECCO Communication Manager

Top-level:
- APIM
- Build
- CustomerCorrespondence.Functions
  - Functions
- CustomerCorrespondence.Orchestrator
- CustomerCorrespondence.TestProjects
"@ | Out-File -FilePath "README.md" -Encoding utf8
        Write-Host "Created README.md"
    } else {
        Write-Host "README.md exists"
    }

    # Detect nested .git directories (excluding repo root .git)
    $nestedGit = Get-ChildItem -Path $RepoRoot -Recurse -Force -Directory -ErrorAction SilentlyContinue | Where-Object {
        Test-Path (Join-Path $_.FullName ".git")
    } | Select-Object -Unique FullName

    if ($nestedGit.Count -gt 0) {
        Write-Host "`nWARNING: Found nested Git repositories (folders that contain their own .git):"
        $i = 0
        foreach ($n in $nestedGit) {
            $i++; Write-Host "[$i] $($n.FullName)"
        }
        Write-Host "`nThese indicate prior independent git repos were present inside the monorepo. Leaving them will create nested repos."
        $resp = Read-Host "Do you want to remove the nested .git directories (this will remove the nested repo metadata and merge them into the monorepo working tree)? Type Y to remove, or press Enter to skip"
        if ($resp -eq 'Y') {
            foreach ($n in $nestedGit) {
                $gitDir = Join-Path $n.FullName ".git"
                if (Test-Path $gitDir) {
                    Remove-Item -Recurse -Force -Path $gitDir
                    Write-Host "Removed: $gitDir"
                }
            }
            Write-Host "Nested .git directories removed. If you needed to preserve per-repo history, that should have been done earlier via subtree/filter-repo."
        } else {
            Write-Host "Skipped removing nested .git directories. You may keep them or remove later manually."
        }
    } else {
        Write-Host "No nested .git repositories detected."
    }

    # Final summary - show top-level listing
    Write-Host "`nFinal top-level listing for $RepoRoot:"
    Get-ChildItem -Path $RepoRoot -Force | Select-Object Name,Mode,LastWriteTime
    Write-Host "`nIf dotnet CLI was available, run 'dotnet build $SolutionName' to confirm everything builds."
} finally {
    Pop-Location
}