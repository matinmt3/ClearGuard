param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$taskExe = Join-Path ([IO.Path]::GetFullPath($BuildDirectory)) 'ClearGuard.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { throw 'Run Build.ps1 first.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    $taskFixtureRoot = Join-Path $PSScriptRoot 'work\ClearGuardTests'
    New-Item -ItemType Directory -Path $taskFixtureRoot -Force | Out-Null
    $taskRawReport = Join-Path $BuildDirectory 'Safety-Test-Report-raw.json'
    $taskBeforeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--self-test',('"'+$taskFixtureRoot+'"'),('"'+([IO.Path]::GetFullPath($taskRawReport))+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($taskProcess.ExitCode -ne 0) { throw 'Safety tests failed; inspect the local raw report.' }
    $taskReport = Get-Content -LiteralPath $taskRawReport -Raw | ConvertFrom-Json
    if ($taskReport.Passed -ne 33 -or $taskReport.Failed -ne 0 -or $taskReport.Skipped -ne 0 -or $taskReport.Tests.Count -ne 33 -or @($taskReport.Tests | Where-Object Status -ne 'PASS').Count -ne 0) { throw 'All 33 safety tests must pass.' }
    $taskAfterHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash
    if ($taskBeforeHash -ne $taskAfterHash) { throw 'Executable changed during testing.' }
    $taskPublic = [ordered]@{
        Product='ClearGuard'; Version='1.0.0'; ExeSHA256=$taskAfterHash.ToLowerInvariant()
        Passed=$taskReport.Passed; Failed=$taskReport.Failed; Skipped=$taskReport.Skipped
        RealUserFilesModified=$taskReport.RealUserFilesModified
        TestScope='Newly generated isolated fixtures only; no cleanup of real user data.'
        Tests=@($taskReport.Tests | ForEach-Object { [ordered]@{Name=$_.Name;Status=$_.Status} })
        Gaps=@($taskReport.Gaps)
    }
    $taskPublic | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BuildDirectory 'Safety-Test-Report.json') -Encoding UTF8
    $taskUiDir = Join-Path $PSScriptRoot 'work\UiSmoke'
    $taskUi = Start-Process -FilePath $taskExe -ArgumentList @('--ui-smoke',('"'+$taskUiDir+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($taskUi.ExitCode -ne 0) { throw 'Native UI rendering failed.' }
    Copy-Item -LiteralPath (Join-Path $taskUiDir 'ui-smoke.json') -Destination (Join-Path $BuildDirectory 'UI-Test-Report.json') -Force
    [pscustomobject]$taskPublic | Select-Object Product,Version,ExeSHA256,Passed,Failed,Skipped,RealUserFilesModified
} finally { Pop-Location }
