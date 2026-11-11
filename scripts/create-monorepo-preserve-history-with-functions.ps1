<#
Create a single monorepo and import existing local repos using git subtree (preserve history).
This variant prompts for the Functions repo path if the default is missing; press Enter to skip importing Functions.
- Edit the top variables if you want different defaults.
- Does NOT push or change remote config.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# === Configuration (edit defaults if needed) ===
$RepoRoot = "C:\ECCO\Repos\ECCO-CommunicationManager"
$OrchRepoPath = "C:\ECCO\Repos\ECCO-CommunicationManager\CustomerCorrespondence.Orchestrator"
$TestsRepoPath = "C:\Projects\OtherProjects\CustomerCorrespondenceTests"
$FunctionsRepoPath = "C:\ECCO\Repos\CustomerCorrespondence.Functions"   # default - will prompt if missing

$OrchPrefix = "CustomerCorrespondence.Orchestrator"
$TestsPrefix = "CustomerCorrespondence.TestProjects"
$FunctionsPrefix = "CustomerCorrespondence.Functions"

# === Helper and prechecks ===
Write-Host "`nChecking prerequisites..."
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "git is required but not found in PATH. Install Git and re-run."
    exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "Note: dotnet CLI not found. Script can still run but won't create/add solution files."
}

# Validate required source paths exist
$missing = @()
if (-not (Test-Path $OrchRepoPath))    { $missing += "Orchestrator repo missing: $OrchRepoPath" }
if (-not (Test-Path $TestsRepoPath))   { $missing += "Tests repo missing: $TestsRepoPath" }

if ($missing.Count -gt 0) {
    Write-Host "`nOne or more required source repo paths are missing:`n"
    $missing | ForEach-Object { Write-Host "  - $_" }
    Write-Error "Fix the paths at the top of the script and re-run."
    exit 1
}

# Prompt for Functions repo if missing
$ImportFunctions = $true
if (-not (Test-Path $FunctionsRepoPath)) {
    Write-Host "`nFunctions repo not found at the default path: $FunctionsRepoPath"
    $userInput = Read-Host "Enter full path to local Functions repo, or press Enter to SKIP importing Functions"
    if ([string]::IsNullOrWhiteSpace($userInput)) {
        Write-Host "Skipping Functions import as requested."
        $ImportFunctions = $false
    } else {
        if (-not (Test-Path $userInput)) {
            Write-Error "Provided Functions path does not exist: $userInput. Aborting."
            exit 1
        }
        $FunctionsRepoPath = $userInput
        Write-Host "Functions repo set to: $FunctionsRepoPath"
    }
}

# === Prepare temp dirs and bare clone paths ===
$TempDir = Join-Path $env:TEMP ("monorepo_import_" + ([guid]::NewGuid().ToString()))
$OrchBare = Join-Path $TempDir "orchestrator.git"
$TestsBare = Join-Path $TempDir "tests.git"
$FunctionsBare = Join-Path $TempDir "functions.git"

$CreateFolders = @("APIM","Build",$OrchPrefix,$TestsPrefix)
if ($ImportFunctions) { $CreateFolders += $FunctionsPrefix }

# Confirm intent
Write-Host "`nThis will create a new git repo at:`n  $RepoRoot"
Write-Host "It will import (preserve history):"
Write-Host "  - Orchestrator: $OrchRepoPath -> $OrchPrefix/"
Write-Host "  - Tests:        $TestsRepoPath -> $TestsPrefix/"
if ($ImportFunctions) { Write-Host "  - Functions:    $FunctionsRepoPath -> $FunctionsPrefix/" }
else { Write-Host "  - Functions:    SKIPPED" }
$confirm = Read-Host "Proceed? Type Y to continue"
if ($confirm -ne 'Y') { Write-Host "Aborted by user."; exit 0 }

# === Create temp and bare clones ===
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
Write-Host "Created temp dir: $TempDir"

git clone --bare --no-local --quiet $OrchRepoPath $OrchBare
Write-Host "Created bare clone: $OrchBare"

git clone --bare --no-local --quiet $TestsRepoPath $TestsBare
Write-Host "Created bare clone: $TestsBare"

if ($ImportFunctions) {
    git clone --bare --no-local --quiet $FunctionsRepoPath $FunctionsBare
    Write-Host "Created bare clone: $FunctionsBare"
}

# === Initialize target repo and create folders ===
if (-not (Test-Path $RepoRoot)) { New-Item -ItemType Directory -Path $RepoRoot | Out-Null; Write-Host "Created repo root: $RepoRoot" }
Push-Location $RepoRoot
try {
    if (-not (Test-Path ".git")) {
        git init --quiet
        Write-Host "Initialized new git repo at $RepoRoot"
    } else {
        Write-Host "Git repo exists at $RepoRoot; continuing."
    }

    foreach ($f in $CreateFolders) {
        if (-not (Test-Path $f)) { New-Item -ItemType Directory -Path $f | Out-Null; Write-Host "Created folder: $f" }
        else { Write-Host "Folder exists: $f" }
    }

    # Add remotes and fetch
    git remote add orchestrator $OrchBare
    git remote add tests $TestsBare
    if ($ImportFunctions) { git remote add functions $FunctionsBare }

    git fetch --all --quiet

    function Get-DefaultBranch($bare) {
        $head = git --git-dir=$bare symbolic-ref --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $head) { return $head }
        $ls = git ls-remote --symref $bare HEAD 2>$null
        if ($ls) {
            $m = ($ls -split "`n" | Select-String 'ref: refs/heads/([^\s]+)\s+HEAD' -AllMatches)
            if ($m.Matches.Count -gt 0) { return $m.Matches[0].Groups[1].Value }
        }
        return "master"
    }

    $orchBranch = Get-DefaultBranch $OrchBare
    $testsBranch = Get-DefaultBranch $TestsBare
    if ($ImportFunctions) { $functionsBranch = Get-DefaultBranch $FunctionsBare }

    # Backup conflicting prefixes if present
    foreach ($prefix in @($OrchPrefix, $TestsPrefix) + (if ($ImportFunctions) { $FunctionsPrefix } else { @() })) {
        if (Test-Path $prefix) {
            $backup = "${prefix}_preimport_backup_$(Get-Date -Format 'yyyyMMddHHmmss')"
            Write-Host "Backing up existing folder '$prefix' -> '$backup'"
            Move-Item -Path $prefix -Destination $backup -Force
        }
    }

    # Subtree imports
    Write-Host "`nImporting Orchestrator..."
    git subtree add --prefix=$OrchPrefix orchestrator $orchBranch --no-rebase
    Write-Host "Imported Orchestrator -> $OrchPrefix"

    Write-Host "`nImporting Tests..."
    git subtree add --prefix=$TestsPrefix tests $testsBranch --no-rebase
    Write-Host "Imported Tests -> $TestsPrefix"

    if ($ImportFunctions) {
        Write-Host "`nImporting Functions..."
        git subtree add --prefix=$FunctionsPrefix functions $functionsBranch --no-rebase
        Write-Host "Imported Functions -> $FunctionsPrefix"
    }

    # Remove temporary remotes
    git remote remove orchestrator
    git remote remove tests
    if ($ImportFunctions) { git remote remove functions }

    # Create .gitignore and README if missing and stage them
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

# Others
*.log
"@ | Out-File -FilePath ".gitignore" -Encoding utf8
        git add .gitignore
    }

    if (-not (Test-Path "README.md")) {
        @"
# ECCO Communication Manager (monorepo)

Imported:
- $OrchPrefix
- $TestsPrefix
$(if ($ImportFunctions) { "- $FunctionsPrefix" } else { "" })
"@ | Out-File -FilePath "README.md" -Encoding utf8
        git add README.md
    }

    if ((git status --porcelain) -ne "") {
        git commit -m "Initialize monorepo and import repositories as subtrees (orchestrator, tests$(if ($ImportFunctions){', functions'} else {''}))" --quiet
        Write-Host "Committed initial import."
    } else {
        Write-Host "No changes to commit."
    }

} finally {
    Pop-Location
}

# Cleanup temp files
try { Remove-Item -Recurse -Force -Path $TempDir; Write-Host "Removed temp dir: $TempDir" } catch { Write-Warning "Could not remove temp dir: $TempDir" }

Write-Host "`nDone. Verify the repo at: $RepoRoot"
Write-Host "Check history for imported folders, e.g.: git log --oneline --all -- $OrchPrefix | Select-Object -First 5"