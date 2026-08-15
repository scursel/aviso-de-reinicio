$ErrorActionPreference = 'Stop'
$url      = 'https://github.com/scursel/aviso-de-reinicio/releases/download/v1.0.1/Instalador-AvisoDeReinicio-v1.0.1.exe'
$checksum = 'e0dc42270742a05b0f2e0fc35dd6fbe885eae45e31711bfb39e8d8d8bbaad2df'
$packageArgs = @{
  packageName    = 'aviso-de-reinicio'
  fileType       = 'EXE'
  url            = $url
  checksum       = $checksum
  checksumType   = 'sha256'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="autostart"'
  validExitCodes = @(0)
}
Install-ChocolateyPackage @packageArgs