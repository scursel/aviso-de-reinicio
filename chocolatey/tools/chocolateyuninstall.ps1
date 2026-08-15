$ErrorActionPreference = 'Stop'
$unins = Join-Path $env:LOCALAPPDATA 'Programs\AvisoDeReinicio\unins000.exe'
if (Test-Path $unins) {
  $packageArgs = @{
    packageName    = 'aviso-de-reinicio'
    fileType       = 'EXE'
    file           = $unins
    silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    validExitCodes = @(0)
  }
  Uninstall-ChocolateyPackage @packageArgs
}