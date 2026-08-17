# Monta um release a partir da versao do assembly em AvisoDeReinicio.cs
# (fonte unica da verdade). Injeta em instalador.iss, compila o exe,
# roda o Inno Setup, grava saida\SHA256SUMS.txt e cria a tag vX.Y.Z.
#
# Uso:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\release.ps1
#   powershell -File .\release.ps1 -SkipTag
#   powershell -File .\release.ps1 -SkipInno
#   powershell -File .\release.ps1 -PfxPath .\cert.pfx -PfxPassword $pwd
param(
    [switch] $SkipBuild,
    [switch] $SkipInno,
    [switch] $SkipTag,
    [string] $PfxPath,
    [string] $PfxPassword
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Get-AssemblyVersion3 {
    $cs = Get-Content -LiteralPath (Join-Path $root 'AvisoDeReinicio.cs') -Raw
    $m = [regex]::Match($cs, '\[assembly:\s*AssemblyVersion\("([0-9]+)\.([0-9]+)\.([0-9]+)(?:\.[0-9]+)?"\)\]')
    if (-not $m.Success) { throw 'AssemblyVersion nao encontrado em AvisoDeReinicio.cs' }
    return '{0}.{1}.{2}' -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value
}

function Set-TextReplace([string] $path, [string] $pattern, [string] $replacement, [string] $label) {
    $raw = [System.IO.File]::ReadAllText($path)
    $next = [regex]::Replace($raw, $pattern, $replacement, 1)
    if ($next -eq $raw -and $raw -notmatch [regex]::Escape($replacement)) {
        throw ("nao achei {0} em {1}" -f $label, $path)
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, $next, $utf8)
}

$ver = Get-AssemblyVersion3
Write-Output ("versao {0} (lida do assembly)" -f $ver)

$issPath = Join-Path $root 'instalador.iss'
Set-TextReplace $issPath '#define MyAppVersion "[^"]+"' ('#define MyAppVersion "{0}"' -f $ver) 'MyAppVersion'

$nuspec = Join-Path $root 'chocolatey\aviso-de-reinicio.nuspec'
if (Test-Path -LiteralPath $nuspec) {
    Set-TextReplace $nuspec '<version>[^<]+</version>' ('<version>{0}</version>' -f $ver) 'nuspec version'
} else {
    Write-Output 'chocolatey/ ausente - pulando nuspec (removido no item 0.6)'
}

if (-not $SkipBuild) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path $csc)) { throw 'csc.exe nao encontrado' }
    if (-not (Test-Path (Join-Path $root 'app.ico'))) {
        powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'make-icon.ps1')
    }
    & $csc /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /out:AvisoDeReinicio.exe AvisoDeReinicio.cs
    if ($LASTEXITCODE -ne 0) { throw 'falha na compilacao' }
    Write-Output 'compilado AvisoDeReinicio.exe'
}

$installerName = 'Instalador-AvisoDeReinicio-v{0}.exe' -f $ver
$saida = Join-Path $root 'saida'
$installer = Join-Path $saida $installerName

if (-not $SkipInno) {
    $iscc = $null
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
    if (-not $iscc) {
        foreach ($c in @(
                (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
                (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
            )) {
            if ($c -and (Test-Path -LiteralPath $c)) { $iscc = $c; break }
        }
    }
    if (-not $iscc) { throw 'ISCC.exe nao encontrado. Instale o Inno Setup 6.' }
    New-Item -ItemType Directory -Force -Path $saida | Out-Null
    & $iscc (Join-Path $root 'instalador.iss')
    if ($LASTEXITCODE -ne 0) { throw 'ISCC.exe falhou' }
    if (-not (Test-Path -LiteralPath $installer)) { throw ("instalador nao gerado: {0}" -f $installer) }

    if ($PfxPath) {
        if (-not (Test-Path -LiteralPath $PfxPath)) { throw ("certificado nao encontrado: {0}" -f $PfxPath) }
        $signtool = $null
        $st = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($st) { $signtool = $st.Source }
        if (-not $signtool) {
            $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
            if (Test-Path $kits) {
                $hit = Get-ChildItem -Path $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -match '\\x64\\' } |
                    Select-Object -First 1
                if ($hit) { $signtool = $hit.FullName }
            }
        }
        if (-not $signtool) { throw 'signtool.exe nao encontrado (Windows SDK)' }
        $signArgs = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/f', $PfxPath)
        if ($PfxPassword) { $signArgs += @('/p', $PfxPassword) }
        $signArgs += $installer
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) { throw 'signtool falhou' }
        Write-Output 'instalador assinado'
    } else {
        Write-Output 'sem -PfxPath: instalador nao assinado'
    }

    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums = '{0}  {1}{2}' -f $hash, $installerName, [Environment]::NewLine
    [System.IO.File]::WriteAllText((Join-Path $saida 'SHA256SUMS.txt'), $sums, (New-Object System.Text.UTF8Encoding $false))
    Write-Output ("SHA256 {0}" -f $hash)
    Write-Output ("gravado {0}" -f (Join-Path $saida 'SHA256SUMS.txt'))
}

if (-not $SkipTag) {
    $tag = 'v{0}' -f $ver
    $exists = git tag --list $tag
    if ($exists) {
        Write-Output ("tag {0} ja existe - nao recriei" -f $tag)
    } else {
        git tag -a $tag -m $tag
        Write-Output ("tag {0} criada (ainda nao enviada)" -f $tag)
    }
}

Write-Output 'release.ps1 ok'
