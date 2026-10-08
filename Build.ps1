param([string]$Destination = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $taskCompiler)){throw '.NET Framework compiler not found.'}
$taskFramework = Split-Path -Parent $taskCompiler
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$taskExe = Join-Path $Destination 'ClearGuard.exe'
$taskSources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object FullName)
$taskReferences = @('System.dll','System.Core.dll','System.Xaml.dll','System.Management.dll','System.Web.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$taskArguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/warnaserror+','/langversion:5','/codepage:65001',('/out:'+$taskExe),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/resource:'+(Join-Path $PSScriptRoot 'MainWindow.xaml')+',ClearGuard.MainWindow.xaml'))
foreach($taskReference in $taskReferences){$taskArguments += '/reference:'+(Join-Path $taskFramework $taskReference)}
& $taskCompiler @taskArguments @taskSources
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ClearGuard.exe.config') -Destination $Destination -Force
Get-FileHash -LiteralPath $taskExe -Algorithm SHA256
