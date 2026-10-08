param([string]$BuildDirectory = (Join-Path $PSScriptRoot 'build'), [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$taskVersion = '1.0.0'
$taskBuild = [IO.Path]::GetFullPath($BuildDirectory)
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
$taskExe = Join-Path $taskBuild 'ClearGuard.exe'
$taskReport = Get-Content -LiteralPath (Join-Path $taskBuild 'Safety-Test-Report.json') -Raw | ConvertFrom-Json
$taskExeHash = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($taskReport.Passed -ne 33 -or $taskReport.Failed -ne 0 -or $taskReport.Skipped -ne 0 -or $taskReport.RealUserFilesModified -ne $false -or $taskReport.ExeSHA256 -ne $taskExeHash) { throw 'Run Test.ps1 against this exact binary before packaging.' }
$taskUi = Get-Content -LiteralPath (Join-Path $taskBuild 'UI-Test-Report.json') -Raw | ConvertFrom-Json
if ($taskUi.Pages.Count -ne 7 -or @($taskUi.Pages | Where-Object {$_ -notlike '*:PASS'}).Count -ne 0 -or $taskUi.DestructiveOperations -ne 0) { throw 'All seven native page renders must pass.' }
$taskPortable = Join-Path $taskOutput 'ClearGuard'
$taskSource = Join-Path $taskOutput 'ClearGuard-Source'
if ((Test-Path -LiteralPath $taskPortable) -or (Test-Path -LiteralPath $taskSource)) { throw 'Use a fresh output directory. Existing packages are never overwritten.' }
New-Item -ItemType Directory -Path $taskPortable,$taskSource -Force | Out-Null
foreach ($taskName in @('ClearGuard.exe','ClearGuard.exe.config','Safety-Test-Report.json','UI-Test-Report.json')) { Copy-Item -LiteralPath (Join-Path $taskBuild $taskName) -Destination $taskPortable }
foreach ($taskName in @('README.md','README.fa.md','CHANGELOG.md','SECURITY.md','LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskPortable }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\Preview.png') -Destination (Join-Path $taskPortable 'Preview.png')
New-Item -ItemType Directory -Path (Join-Path $taskPortable 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\Preview.png') -Destination (Join-Path $taskPortable 'docs\Preview.png')
foreach ($taskFile in (Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object {$_.Extension -in @('.cs','.xaml','.manifest','.config','.md','.txt','.ps1') -or $_.Name -in @('.gitignore','.gitattributes')})) { Copy-Item -LiteralPath $taskFile.FullName -Destination $taskSource }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $taskSource -Recurse
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.github')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.github') -Destination $taskSource -Recurse }
$taskPortableSums = Get-ChildItem -LiteralPath $taskPortable -File -Recurse | Sort-Object FullName | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($taskPortable.Length+1).Replace('\','/') }
$taskPortableSums | Set-Content -LiteralPath (Join-Path $taskPortable 'SHA256SUMS.txt') -Encoding ASCII
Compress-Archive -LiteralPath $taskPortable -DestinationPath (Join-Path $taskOutput "ClearGuard-$taskVersion-Windows-Portable.zip") -CompressionLevel Optimal
Compress-Archive -LiteralPath $taskSource -DestinationPath (Join-Path $taskOutput "ClearGuard-$taskVersion-Source.zip") -CompressionLevel Optimal
$taskFiles = @(Get-ChildItem -LiteralPath $taskOutput -File -Filter '*.zip' | Sort-Object Name | ForEach-Object { [ordered]@{Name=$_.Name;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
$taskFiles | ForEach-Object { $_.SHA256+'  '+$_.Name } | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ASCII
[ordered]@{Product='ClearGuard';Version=$taskVersion;ExeSHA256=$taskExeHash;TestsPassed=33;TestsFailed=0;TestsSkipped=0;RealUserFilesModified=$false;Files=$taskFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutput 'Release-Manifest.json') -Encoding UTF8
Get-ChildItem -LiteralPath $taskOutput -File | Select-Object Name,Length
