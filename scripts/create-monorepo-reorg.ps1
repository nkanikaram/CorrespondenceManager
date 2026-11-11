<#
create-monorepo-reorg.ps1

Purpose:
- Ensure top-level layout: APIM, Build, CustomerCorrespondence.Functions, CustomerCorrespondence.Orchestrator, CustomerCorrespondence.TestProjects
- Consolidate Function folders into CustomerCorrespondence.Functions\Functions
- Ensure orchestrator subfolders and move matching top-level folders into them
- Permanently remove any *.slnx files (non-interactive)
- Create root solution ECCO-CommunicationManager.sln and add all .csproj projects (excluding nested .git)
- Back up nested .git directories by renaming them to .git_bak_TIMESTAMP
Notes:
- This script modifies the working tree and deletes .slnx files permanently.
- It does NOT change Git remotes or push to any remote.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Configuration
$RepoRoot = "C:\ECCO\Repos\ECCO-CommunicationManager"
$ScriptsFolder = Join-Path $RepoRoot "scripts"
$SolutionName = "ECCO-CommunicationManager.sln"
$SolutionPath = Join-Path $RepoRoot $SolutionName

$TopLevelFolders = @(
    "APIM",
    "Build",
    "CustomerCorrespondence.Functions",
    "CustomerCorrespondence.Orchestrator",
    "CustomerCorrespondence.TestProjects"
)

$OrchestratorSubfolders = @(
    ".github",
    "CustomerCorrespondence.Application",
    "CustomerCorrespondence.Core",
    "CustomerCorrespondence.Domain",
    "CustomerCorrespondence.Exceptions",
    "CustomerCorrespondence.Infrastructure",
    "CustomerCorrespondence.Resilience",
    "ProcessRequestEndPoint"
)

$FunctionsTarget = Join-Path (Join-Path $RepoRoot "CustomerCorrespondence.Functions") "Functions"

# Helper: ensure dir exists
function Ensure-Dir([string]$path) {
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
        Write-Host ("Created: {0}" -f $path)
    } else {
        Write-Host ("Exists: {0}" -f $path)
    }
}

# Start
Write-Host ("Target repository root: {0}" -f $RepoRoot)
if (-not (Test-Path $RepoRoot)) {
    Write-Host ("Repository root does not exist. Creating: {0}" -f $RepoRoot)
    New-Item -ItemType Directory -Path $RepoRoot | Out-Null
}
Ensure-Dir -path $ScriptsFolder

Push-Location $RepoRoot
try {
    Write-Host "`n--- Ensure top-level folders ---"
    foreach ($f in $TopLevelFolders) { Ensure-Dir -path (Join-Path $RepoRoot $f) }

    Write-Host "`n--- Ensure functions target folder ---"
    Ensure-Dir -path $FunctionsTarget

    # Consolidate function folders (root and inside orchestrator)
    Write-Host "`n--- Consolidating Function folders into $FunctionsTarget ---"
    $excludeNames = $TopLevelFolders + @("scripts")
    $functionCandidates = @()
    $rootDirs = Get-ChildItem -Path $RepoRoot -Directory -Force -ErrorAction SilentlyContinue
    foreach ($d in $rootDirs) {
        if ((($d.Name -like '*Function*') -or ($d.Name -like '*Functions*')) -and (-not ($excludeNames -contains $d.Name))) {
            $functionCandidates += $d
        }
    }
    $OrchRoot = Join-Path $RepoRoot "CustomerCorrespondence.Orchestrator"
    if (Test-Path $OrchRoot) {
        $orchDirs = Get-ChildItem -Path $OrchRoot -Directory -Force -ErrorAction SilentlyContinue
        foreach ($d in $orchDirs) {
            if (($d.Name -like '*Function*') -or ($d.Name -like '*Functions*')) { $functionCandidates += $d }
        }
    }
    $functionCandidates = $functionCandidates | Select-Object -Unique

    if (@($functionCandidates).Count -gt 0) {
        foreach ($c in $functionCandidates) {
            $dest = Join-Path $FunctionsTarget $c.Name
            if ($c.FullName -ne (Get-Item $FunctionsTarget).FullName) {
                if (-not (Test-Path $dest)) {
                    Write-Host ("Moving: {0} -> {1}" -f $c.FullName, $dest)
                    Move-Item -Path $c.FullName -Destination $FunctionsTarget -Force
                } else {
                    Write-Host ("Target exists, skipping move: {0}" -f $dest)
                }
            }
        }
    } else {
        Write-Host "No function folders detected to move."
    }

    # Ensure orchestrator subfolders and relocate matches
    Write-Host "`n--- Ensuring orchestrator subfolders and relocating matches ---"
    Ensure-Dir -path $OrchRoot
    foreach ($s in $OrchestratorSubfolders) { Ensure-Dir -path (Join-Path $OrchRoot $s) }

    foreach ($s in $OrchestratorSubfolders) {
        $plain = $s -replace '^CustomerCorrespondence\.',''
        $candidates = Get-ChildItem -Path $RepoRoot -Directory -Force -ErrorAction SilentlyContinue |
                      Where-Object { $_.Name -ieq $s -or $_.Name -ieq $plain }
        foreach ($c in $candidates) {
            $targetFolder = Join-Path $OrchRoot $s
            $targetPath = Join-Path $targetFolder $c.Name
            if ($c.FullName -ne $targetPath) {
                if (-not (Test-Path $targetPath)) {
                    Write-Host ("Moving top-level folder {0} -> {1}" -f $c.FullName, $targetFolder)
                    Move-Item -Path $c.FullName -Destination $targetFolder -Force
                } else {
                    Write-Host ("Conflict: target exists, skipping {0}" -f $c.FullName)
                }
            }
        }
    }

    # Ensure test subfolders
    Write-Host "`n--- Ensuring test project subfolders ---"
    $testsRoot = Join-Path $RepoRoot "CustomerCorrespondence.TestProjects"
    Ensure-Dir -path $testsRoot
    foreach ($t in @("CoreTests","DomainTests")) { Ensure-Dir -path (Join-Path $testsRoot $t) }

    # Remove any .slnx files permanently
    Write-Host "`n--- Removing any .slnx files (permanent) ---"
    $slnxFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter *.slnx -File -ErrorAction SilentlyContinue
    if (@($slnxFiles).Count -gt 0) {
        foreach ($f in $slnxFiles) {
            try {
                Remove-Item -LiteralPath $f.FullName -Force
                Write-Host ("Deleted .slnx: {0}" -f $f.FullName)
            } catch {
                Write-Warning ("Failed to delete {0}: {1}" -f $f.FullName, $_)
            }
        }
    } else {
        Write-Host "No .slnx files found."
    }

    # Create root solution (dotnet new sln), fallback to --force
    Write-Host "`n--- Creating root solution: $SolutionName ---"
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        if (-not (Test-Path $SolutionPath)) {
            $out = & dotnet new sln -n "ECCO-CommunicationManager" 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning ("dotnet new sln returned code {0}`n{1}" -f $LASTEXITCODE, $out)
                Write-Host "Retrying with --force..."
                & dotnet new sln -n "ECCO-CommunicationManager" --force | Out-Null
            } else {
                Write-Host "Created solution."
            }
        } else {
            Write-Host ("Solution already exists: {0}" -f $SolutionName)
        }

        if (-not (Test-Path $SolutionPath)) {
            Write-Warning "Solution file not found after creation. Skipping project add step."
        } else {
            # Add projects to solution (exclude any path containing .git or _bak)
            Write-Host "`n--- Adding projects to solution ---"
            $csprojFiles = Get-ChildItem -Path $RepoRoot -Recurse -Include *.csproj -File -ErrorAction SilentlyContinue |
                           Where-Object { $_.FullName -notmatch "\\.git\\|\\.git_bak\\|_bak_" }
            if (@($csprojFiles).Count -eq 0) {
                Write-Host "No .csproj files found to add."
            } else {
                foreach ($proj in $csprojFiles) {
                    $leaf = Split-Path $proj.FullName -Leaf
                    $inSolution = (& dotnet sln $SolutionPath list) -join "`n"
                    if ($inSolution -notmatch [regex]::Escape($leaf)) {
                        try {
                            & dotnet sln $SolutionPath add $proj.FullName | Out-Null
                            Write-Host ("Added: {0}" -f $proj.FullName)
                        } catch {
                            Write-Warning ("Failed to add project {0}: {1}" -f $proj.FullName, $_)
                        }
                    } else {
                        Write-Host ("Already in solution: {0}" -f $leaf)
                    }
                }
            }
        }
    } else {
        Write-Host "dotnet CLI not available; skipped solution creation and project addition."
    }

    # Create minimal .gitignore and README.md if missing
    Write-Host "`n--- Ensuring .gitignore and README.md ---"
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
"@ | Out-File -FilePath ".gitignore" -Encoding UTF8
        Write-Host "Created .gitignore"
    } else { Write-Host ".gitignore exists" }

    if (-not (Test-Path "README.md")) {
        @"
# ECCO Communication Manager

Top-level layout:
- APIM
- Build
- CustomerCorrespondence.Functions (Functions)
- CustomerCorrespondence.Orchestrator
- CustomerCorrespondence.TestProjects
"@ | Out-File -FilePath "README.md" -Encoding UTF8
        Write-Host "Created README.md"
    } else { Write-Host "README.md exists" }

    # Back up nested .git directories (excluding repo root)
    Write-Host "`n--- Detecting nested .git repositories and backing up ---"
    $nestedGitDirs = Get-ChildItem -Path $RepoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
                     Where-Object { Test-Path (Join-Path $_.FullName ".git") } |
                     ForEach-Object { $_.FullName } | Select-Object -Unique

    # Exclude the repo root itself if present
    $nestedGitDirs = $nestedGitDirs | Where-Object { $_ -ne $RepoRoot }

    if (@($nestedGitDirs).Count -gt 0) {
        foreach ($d in $nestedGitDirs) {
            $gitDir = Join-Path $d ".git"
            $bak = ("{0}_bak_{1}" -f $gitDir, (Get-Date -Format 'yyyyMMddHHmmss'))
            try {
                Move-Item -Path $gitDir -Destination $bak -Force
                Write-Host ("Backed up: {0} -> {1}" -f $gitDir, $bak)
            } catch {
                Write-Warning ("Failed to back up {0}: {1}" -f $gitDir, $_)
            }
        }
        Write-Host "Nested .git directories backed up (renamed with _bak timestamp)."
    } else {
        Write-Host "No nested .git directories detected."
    }

    Write-Host "`n--- Reorganization complete ---"
    Get-ChildItem -Path $RepoRoot -Force | Select-Object Name,Mode,LastWriteTime
}
finally {
    Pop-Location
}