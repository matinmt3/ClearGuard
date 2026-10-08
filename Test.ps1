param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$taskVersion = '1.2.0'
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$taskExe = Join-Path $BuildDirectory 'ClearGuard.exe'
if (-not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'Run Build.ps1 first.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).ProductVersion -notmatch '^1\.2\.0(?:\.0)?$') { throw 'Build the v1.2.0 source before testing this release.' }
$taskFixtureRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work\ClearGuardTests'))
if ([IO.Path]::GetPathRoot($taskFixtureRoot) -ne 'C:\') { throw 'Safety fixtures must be under this repository on C:. Run from a C-drive checkout; no tests were started.' }
$taskAncestor = $taskFixtureRoot
while ($taskAncestor) {
    if (Test-Path -LiteralPath $taskAncestor) {
        $taskItem = Get-Item -LiteralPath $taskAncestor -Force
        if (($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Fixture ancestors must not be junctions or symbolic links; no tests were started.' }
    }
    $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)
}
$taskLegacyNames = @(
    'Path boundary rejects prefix siblings and dot-dot escape'
    'Image and video extension preservation'
    'PNG header protects a disguised .dat file'
    'Media inside an opaque cache blob is protected across buffer boundaries'
    'ZIP with media and nested ZIP with media are protected'
    'Unknown nested compressed archive is fail-closed'
    'Sensitive .env entry inside a cache archive is preserved'
    'Source code, APK, AAB, and key material are preserved'
    'Desktop, Documents, Downloads, Codex and AVD are never cache deletion targets'
    'Unknown cache path and outside-user path are refused'
    'SHA revalidation detects changed content with identical length and timestamp'
    'Exclusive lock blocks deletion and leaves file intact'
    'Media created after scan is retained on revalidation'
    'Isolated approved safe-cache fixture deletion reports exact logical bytes'
    'Reparse ancestor is detected without traversing target'
    'Process guard never terminates the running test process'
    'Exact source archive duplicates keep newest and remain unselected'
    'Changed source keeper blocks duplicate recycling'
    'SYMEXVPN and proxyx copies are not source-cleanup candidates'
    'Organizer destination collision blocks move without overwrite'
    'Organizer move and restore conflict preserve both copies'
    'Organizer restore refuses a forged journal outside the actual Desktop'
    'Actual Recycle Bin receipt and SHA-verified restore of generated fixture'
    'Actual Recycle Bin restore conflict never overwrites and remains recoverable'
    'Actual Recycle Bin folder restore preserves nested files and empty directories'
    'Cancellation before cleanup preserves generated candidate'
    'Deep organized Desktop container still discovers protected originals'
    'Approved exact source ZIP recycling and recovery preserve keeper'
    'Large-file inventory and drive duplicates are read-only and SHA-proven'
    'Report exports escape HTML and neutralize spreadsheet formulas'
    'Android app version is not confused with a dependency package version'
    'ZIP trailing media outside entries is preserved fail-closed'
    'Unknown local Maven artifact remains untouched'
)
$taskRequiredAppsNames = @(
    'Installed apps: registry sizes use KiB and remain explicitly estimated'
    'Installed apps: mirrored registry registrations deduplicate but versions remain'
    'Installed apps: Store JSON handles empty one and many without command execution'
    'Installed apps: app folder sizes read lengths only and do not change files'
)
$taskRequiredUiNames = @(
    'Explicit Exit button is present and reachable'
    'Installed applications grid is read-only and cannot delete or uninstall'
    'Idle Exit button closes the window'
    'Busy Exit cancels and waits for worker completion'
    'Dashboard page is read-only with an explicit scan action'
    'Updates page checks only on request and has no automatic installation'
    'Protected folders page provides additive local protection controls'
    'Two dashboard scan interactions persist local baselines and show actual growth'
    'Receipt restore rejects a newly protected original destination before mutation'
    'Delayed update result resumes on the UI dispatcher without cross-thread access'
)
$taskRequiredV12Names = @(
    'Protected folders: tilde and short-path aliases fail closed'
    'Disk dashboard: corrupted history disables automatic exact comparison'
    'Disk dashboard: tilde components cannot alias scan roots or owned snapshot stores'
    'Disk dashboard: cancellation before stage write flush or commit leaves no snapshot'
    'Disk dashboard: cancellation after atomic commit returns saved fact'
    'Disk dashboard: duplicate decoded root JSON keys cannot manufacture exact comparison'
    'Disk dashboard: duplicate escaped row bytes and completion keys fail closed'
    'Disk dashboard: mapped network unknown optical and unavailable drive classes are refused'
)
function ConvertTo-PublicTestText([string]$Value) {
    # Omit entire diagnostics containing local paths, rather than exposing filenames.
    if ($Value -match '(?i)[A-Z]:[\\/]|\\\\|file://|(?:^|[\s"''])/(?:Users|home|tmp|var)/') { return 'A local-only diagnostic was omitted from this public report.' }
    return $Value
}
function Assert-AllPassed($Report, [int]$Minimum, [string]$Label) {
    $taskEntries = @($Report.Tests)
    if ($taskEntries.Count -lt $Minimum -or $Report.Passed -ne $taskEntries.Count -or $Report.Failed -ne 0 -or $Report.Skipped -ne 0 -or @($taskEntries | Where-Object Status -ne 'PASS').Count -ne 0 -or $Report.RealUserFilesModified -ne $false) { throw "$Label must pass completely with no skips or real-user-file changes." }
    if (@($taskEntries.Name | Sort-Object -Unique).Count -ne $taskEntries.Count) { throw "$Label test names must be unique." }
}
Push-Location -LiteralPath $PSScriptRoot
try {
    New-Item -ItemType Directory -Path $taskFixtureRoot -Force | Out-Null
    $taskRawReport = Join-Path $BuildDirectory 'Safety-Test-Report-raw.json'
    $taskBeforeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--self-test',('"'+$taskFixtureRoot+'"'),('"'+$taskRawReport+'"')) -WindowStyle Hidden -Wait -PassThru
    $taskProcess.Refresh()
    if ($taskProcess.ExitCode -ne 0) { throw 'Safety tests failed; inspect the local raw report. No release packages were generated.' }
    $taskReport = Get-Content -LiteralPath $taskRawReport -Raw | ConvertFrom-Json
    Assert-AllPassed $taskReport 171 'Safety tests'
    foreach ($taskLegacy in $taskLegacyNames) { if ($taskLegacy -cnotin @($taskReport.Tests.Name)) { throw 'A required legacy safety test is missing.' } }
    foreach ($taskRequired in $taskRequiredAppsNames) { if ($taskRequired -cnotin @($taskReport.Tests.Name)) { throw 'A required installed-application regression is missing.' } }
    foreach ($taskRequired in $taskRequiredV12Names) { if ($taskRequired -cnotin @($taskReport.Tests.Name)) { throw 'A required v1.2 protection or dashboard regression is missing.' } }
    foreach ($taskFamily in @('Update checker:','Disk dashboard:','Protected folders:','Journal persistence:')) { if (@($taskReport.Tests | Where-Object { $_.Name.StartsWith($taskFamily) }).Count -lt 10) { throw 'A required v1.2 feature test family is incomplete.' } }
    $taskAfterHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($taskBeforeHash -ne $taskAfterHash) { throw 'Executable changed during safety testing.' }
    $taskPublic = [ordered]@{
        Product='ClearGuard'; Version=$taskVersion; ExeSHA256=$taskAfterHash
        Passed=$taskReport.Passed; Failed=$taskReport.Failed; Skipped=$taskReport.Skipped
        LegacySafetyTestsPassed=$taskLegacyNames.Count; AddedSafetyTestsPassed=($taskReport.Tests.Count-$taskLegacyNames.Count)
        InstalledAppsRequiredCasesPassed=$taskRequiredAppsNames.Count
        V12RequiredCasesPassed=$taskRequiredV12Names.Count
        RealUserFilesModified=$false
        TestScope='Newly generated isolated fixtures only; no cleanup of real user data.'
        Tests=@($taskReport.Tests | ForEach-Object { [ordered]@{Name=(ConvertTo-PublicTestText $_.Name);Status=$_.Status} })
        Gaps=@($taskReport.Gaps | ForEach-Object { ConvertTo-PublicTestText $_ })
    }
    $taskPublic | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'Safety-Test-Report.json') -Encoding UTF8
    $taskUiDir = Join-Path $PSScriptRoot ('work\UiSmoke-'+[Guid]::NewGuid().ToString('N'))
    $taskUi = Start-Process -FilePath $taskExe -ArgumentList @('--ui-smoke',('"'+$taskUiDir+'"')) -WindowStyle Hidden -Wait -PassThru
    $taskUi.Refresh()
    if ($taskUi.ExitCode -ne 0) {
        Write-Output ('Native UI process exit code: '+$taskUi.ExitCode)
        $taskFailureReport = Join-Path $taskUiDir 'ui-integration.json'
        if (Test-Path -LiteralPath $taskFailureReport -PathType Leaf) {
            $taskFailureData = Get-Content -LiteralPath $taskFailureReport -Raw | ConvertFrom-Json
            foreach ($taskFailure in @($taskFailureData.Tests | Where-Object Status -ne 'PASS')) { Write-Output ((ConvertTo-PublicTestText $taskFailure.Name)+': '+(ConvertTo-PublicTestText $taskFailure.Detail)) }
            Write-Output ('UI report counts: '+$taskFailureData.Passed+' PASS / '+$taskFailureData.Failed+' FAIL / '+$taskFailureData.Skipped+' SKIP')
        }
        throw 'Native UI page or interaction checks failed; inspect the local UI reports.'
    }
    $taskPages = Get-Content -LiteralPath (Join-Path $taskUiDir 'ui-smoke.json') -Raw | ConvertFrom-Json
    $taskChecks = Get-Content -LiteralPath (Join-Path $taskUiDir 'ui-integration.json') -Raw | ConvertFrom-Json
    Assert-AllPassed $taskChecks 37 'Native UI interaction tests'
    foreach ($taskRequired in $taskRequiredUiNames) { if ($taskRequired -cnotin @($taskChecks.Tests.Name)) { throw 'A required Exit or installed-application UI check is missing.' } }
    if ($taskPages.Pages.Count -ne 11 -or @($taskPages.Pages | Where-Object {$_ -notlike '*:PASS'}).Count -ne 0 -or $taskPages.DestructiveOperations -ne 0) { throw 'All eleven native page renders must pass without real cleanup.' }
    $taskFinalHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($taskFinalHash -ne $taskAfterHash -or $taskChecks.ExecutableSha256 -ne $taskFinalHash) { throw 'UI report is not tied to this unchanged executable.' }
    $taskPublicUi = [ordered]@{
        Product='ClearGuard'; Version=$taskVersion; ExeSHA256=$taskFinalHash
        Pages=@($taskPages.Pages); DestructiveOperations=$taskPages.DestructiveOperations
        Width=$taskPages.Width; Height=$taskPages.Height
        Passed=$taskChecks.Passed; Failed=$taskChecks.Failed; Skipped=$taskChecks.Skipped; RealUserFilesModified=$false
        TestScope='Native page rendering and fixture-backed UI interaction checks only; no cleanup of real user data.'
        UiChecks=@($taskChecks.Tests | ForEach-Object { [ordered]@{Name=(ConvertTo-PublicTestText $_.Name);Status=$_.Status} })
        Gaps=@($taskChecks.Gaps | ForEach-Object { ConvertTo-PublicTestText $_ })
    }
    $taskPublicUi | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'UI-Test-Report.json') -Encoding UTF8
    [pscustomobject]$taskPublic | Select-Object Product,Version,ExeSHA256,Passed,Failed,Skipped,LegacySafetyTestsPassed,AddedSafetyTestsPassed,RealUserFilesModified
    [pscustomobject]$taskPublicUi | Select-Object Product,Version,Passed,Failed,Skipped,@{Name='PagesPassed';Expression={$_.Pages.Count}},RealUserFilesModified
} finally { Pop-Location }
