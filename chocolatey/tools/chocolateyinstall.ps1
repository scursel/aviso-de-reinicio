$ErrorActionPreference = 'Stop'
$url      = 'https://github.com/scursel/aviso-de-reinicio/releases/download/v1.0.2/Instalador-AvisoDeReinicio-v1.0.2.exe'
$checksum = 'a1b8299c9f2814e474ebd1f216a8ab0daeda286830d3c08b915b661756652128'
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