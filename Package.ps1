param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'build'), [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist-1.2.0'))
$ErrorActionPreference = 'Stop'
$taskVersion = '1.2.0'
$taskBuild = [IO.Path]::GetFullPath($BuildDirectory)
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
$taskExe = Join-Path $taskBuild 'ClearGuard.exe'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).ProductVersion -notmatch '^1\.2\.0(?:\.0)?$') { throw 'Package only the v1.2.0 executable.' }
$taskReport = Get-Content -LiteralPath (Join-Path $taskBuild 'Safety-Test-Report.json') -Raw | ConvertFrom-Json
$taskExeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
$taskTestNames = @($taskReport.Tests.Name)
$taskRequiredAppsNames = @('Installed apps: registry sizes use KiB and remain explicitly estimated','Installed apps: mirrored registry registrations deduplicate but versions remain','Installed apps: Store JSON handles empty one and many without command execution','Installed apps: app folder sizes read lengths only and do not change files')
if ($taskReport.Version -ne $taskVersion -or $taskReport.Tests.Count -le 33 -or $taskReport.Passed -ne $taskReport.Tests.Count -or $taskReport.LegacySafetyTestsPassed -ne 33 -or $taskReport.AddedSafetyTestsPassed -ne ($taskReport.Tests.Count-33) -or $taskReport.InstalledAppsRequiredCasesPassed -ne $taskRequiredAppsNames.Count -or $taskReport.Failed -ne 0 -or $taskReport.Skipped -ne 0 -or $taskReport.RealUserFilesModified -ne $false -or $taskReport.ExeSHA256 -ne $taskExeHash -or @($taskReport.Tests | Where-Object Status -ne 'PASS').Count -ne 0 -or @($taskTestNames | Sort-Object -Unique).Count -ne $taskTestNames.Count) { throw 'Run Test.ps1 against this exact binary; every legacy and added safety test must pass.' }
foreach ($taskRequired in $taskRequiredAppsNames) { if ($taskRequired -cnotin $taskTestNames) { throw 'A required installed-application regression is missing.' } }
if ($taskReport.Tests.Count -lt 171 -or $taskReport.V12RequiredCasesPassed -ne 8) { throw 'The complete v1.2 safety suite must pass.' }
foreach ($taskRequired in @('Protected folders: tilde and short-path aliases fail closed','Disk dashboard: corrupted history disables automatic exact comparison','Disk dashboard: tilde components cannot alias scan roots or owned snapshot stores','Disk dashboard: cancellation before stage write flush or commit leaves no snapshot','Disk dashboard: cancellation after atomic commit returns saved fact','Disk dashboard: duplicate decoded root JSON keys cannot manufacture exact comparison','Disk dashboard: duplicate escaped row bytes and completion keys fail closed','Disk dashboard: mapped network unknown optical and unavailable drive classes are refused')) { if ($taskRequired -cnotin $taskTestNames) { throw 'A required v1.2 regression is missing.' } }
foreach ($taskFamily in @('Update checker:','Disk dashboard:','Protected folders:','Journal persistence:')) { if (@($taskReport.Tests | Where-Object { $_.Name.StartsWith($taskFamily) }).Count -lt 10) { throw 'A v1.2 test family is incomplete.' } }
$taskUi = Get-Content -LiteralPath (Join-Path $taskBuild 'UI-Test-Report.json') -Raw | ConvertFrom-Json
$taskRequiredUiNames = @('Explicit Exit button is present and reachable','Installed applications grid is read-only and cannot delete or uninstall','Idle Exit button closes the window','Busy Exit cancels and waits for worker completion','Dashboard page is read-only with an explicit scan action','Updates page checks only on request and has no automatic installation','Protected folders page provides additive local protection controls','Two dashboard scan interactions persist local baselines and show actual growth','Receipt restore rejects a newly protected original destination before mutation')
if ($taskUi.Version -ne $taskVersion -or $taskUi.ExeSHA256 -ne $taskExeHash -or $taskUi.Pages.Count -ne 11 -or @($taskUi.Pages | Where-Object {$_ -notlike '*:PASS'}).Count -ne 0 -or $taskUi.DestructiveOperations -ne 0 -or $taskUi.RealUserFilesModified -ne $false -or $taskUi.UiChecks.Count -lt $taskRequiredUiNames.Count -or $taskUi.Passed -ne $taskUi.UiChecks.Count -or $taskUi.Failed -ne 0 -or $taskUi.Skipped -ne 0 -or @($taskUi.UiChecks | Where-Object Status -ne 'PASS').Count -ne 0) { throw 'All eleven native renders and every interaction check must pass for this exact binary.' }
foreach ($taskRequired in $taskRequiredUiNames) { if ($taskRequired -cnotin @($taskUi.UiChecks.Name)) { throw 'A required Exit or installed-application check is missing.' } }
if ($taskUi.UiChecks.Count -lt 36) { throw 'The complete native v1.2 interaction suite must pass.' }
if (@($taskUi.UiChecks.Name | Sort-Object -Unique).Count -ne $taskUi.UiChecks.Count) { throw 'Interaction-check names must be unique.' }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a fresh output directory. Existing releases are never overwritten.' }
$taskPortable = Join-Path $taskOutput 'ClearGuard'
$taskSource = Join-Path $taskOutput 'ClearGuard-Source'
New-Item -ItemType Directory -Path $taskPortable,$taskSource -Force | Out-Null
foreach ($taskName in @('ClearGuard.exe','ClearGuard.exe.config','Safety-Test-Report.json','UI-Test-Report.json')) { Copy-Item -LiteralPath (Join-Path $taskBuild $taskName) -Destination $taskPortable }
foreach ($taskName in @('README.md','README.fa.md','CHANGELOG.md','RELEASE_NOTES.md','SECURITY.md','LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskPortable }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\Preview.png') -Destination (Join-Path $taskPortable 'Preview.png')
New-Item -ItemType Directory -Path (Join-Path $taskPortable 'docs') | Out-Null
foreach ($taskName in @('Preview.png','InstalledApps.png','Dashboard.png','Updates.png','README.md')) {
    $taskPublicDoc = Join-Path $PSScriptRoot ('docs\'+$taskName)
    if (Test-Path -LiteralPath $taskPublicDoc -PathType Leaf) { Copy-Item -LiteralPath $taskPublicDoc -Destination (Join-Path $taskPortable 'docs') }
}
foreach ($taskName in @('Safety-Test-Report.json','UI-Test-Report.json')) { Copy-Item -LiteralPath (Join-Path $taskBuild $taskName) -Destination (Join-Path $taskPortable 'docs') }
# Do not archive ignored raw diagnostics, credentials, local inventories, or arbitrary support files.
foreach ($taskFile in (Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cs')) {
    if (($taskFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Source files must not be links.' }
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskSource
}
foreach ($taskName in @('MainWindow.xaml','app.manifest','ClearGuard.exe.config','README.md','README.fa.md','CHANGELOG.md','RELEASE_NOTES.md','SECURITY.md','LICENSE.txt','Build.ps1','Test.ps1','Package.ps1','.gitignore','.gitattributes')) {
    $taskPublicFile = Get-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Force
    if (($taskPublicFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Public support files must not be links.' }
    Copy-Item -LiteralPath $taskPublicFile.FullName -Destination $taskSource
}
New-Item -ItemType Directory -Path (Join-Path $taskSource 'docs') | Out-Null
foreach ($taskName in @('Preview.png','InstalledApps.png','Dashboard.png','Updates.png','README.md')) {
    $taskPublicDoc = Join-Path $PSScriptRoot ('docs\'+$taskName)
    if (Test-Path -LiteralPath $taskPublicDoc -PathType Leaf) { Copy-Item -LiteralPath $taskPublicDoc -Destination (Join-Path $taskSource 'docs') }
}
foreach ($taskName in @('Safety-Test-Report.json','UI-Test-Report.json')) { Copy-Item -LiteralPath (Join-Path $taskBuild $taskName) -Destination (Join-Path $taskSource 'docs') }
$taskWorkflow = Join-Path $PSScriptRoot '.github\workflows\build.yml'
if (Test-Path -LiteralPath $taskWorkflow -PathType Leaf) {
    New-Item -ItemType Directory -Path (Join-Path $taskSource '.github\workflows') | Out-Null
    Copy-Item -LiteralPath $taskWorkflow -Destination (Join-Path $taskSource '.github\workflows')
}
$taskPortableSums = Get-ChildItem -LiteralPath $taskPortable -File -Recurse | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($taskPortable.Length+1).Replace('\','/') }
$taskPortableSums | Set-Content -LiteralPath (Join-Path $taskPortable 'SHA256SUMS.txt') -Encoding ASCII
Compress-Archive -LiteralPath $taskPortable -DestinationPath (Join-Path $taskOutput "ClearGuard-$taskVersion-Windows-Portable.zip") -CompressionLevel Optimal
Compress-Archive -LiteralPath $taskSource -DestinationPath (Join-Path $taskOutput "ClearGuard-$taskVersion-Source.zip") -CompressionLevel Optimal
$taskFiles = @(Get-ChildItem -LiteralPath $taskOutput -File -Filter '*.zip' | Sort-Object Name | ForEach-Object { [ordered]@{Name=$_.Name;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
$taskFiles | ForEach-Object { $_.SHA256+'  '+$_.Name } | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ASCII
[ordered]@{Product='ClearGuard';Version=$taskVersion;ExeSHA256=$taskExeHash;TestsPassed=$taskReport.Passed;TestsFailed=0;TestsSkipped=0;LegacySafetyTestsPassed=$taskReport.LegacySafetyTestsPassed;UiPagesPassed=$taskUi.Pages.Count;UiChecksPassed=$taskUi.Passed;UiChecksFailed=0;UiChecksSkipped=0;RealUserFilesModified=$false;Files=$taskFiles} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskOutput 'Release-Manifest.json') -Encoding UTF8
Get-ChildItem -LiteralPath $taskOutput -File | Select-Object Name,Length
