# Empurra um update do Aviso de Reinicio para ESTA maquina: baixa o
# instalador da release do GitHub, confere o SHA256 contra o
# SHA256SUMS.txt do mesmo release, fecha o app, instala em modo
# silencioso e confirma a versao instalada no disco.
#
# Uso (nao precisa de administrador):
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\atualizar-maquinas.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\atualizar-maquinas.ps1 -Tag v1.6.0
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\atualizar-maquinas.ps1 -Tag v1.6.0 -Sha256 <hash>
#
# Para uma nova release normalmente so' o -Tag muda: o hash esperado e'
# lido do SHA256SUMS.txt publicado junto. Use -Sha256 para fixar manualmente
# e -RespeitarAutostart para nao forcar a tarefa de inicio automatico.
param(
    [string]$Tag = 'v1.5.0',
    [string]$Sha256 = '',
    [switch]$RespeitarAutostart
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'scursel/aviso-de-reinicio'
$versao = $Tag.TrimStart('v', 'V')
$versaoArquivo = "$($versao).0"
$arquivo = "Instalador-AvisoDeReinicio-$($Tag).exe"
$urlExe = "https://github.com/$($repo)/releases/download/$($Tag)/$($arquivo)"
$urlSums = "https://github.com/$($repo)/releases/download/$($Tag)/SHA256SUMS.txt"
$destino = Join-Path $env:TEMP $arquivo
$exe = Join-Path $env:LOCALAPPDATA 'Programs\AvisoDeReinicio\AvisoDeReinicio.exe'

# --- 0) ja esta na versao alvo? nada a fazer (idempotente para o parque) ---
if (Test-Path $exe) {
    $atual = (Get-Item $exe).VersionInfo.FileVersion
    if ($atual -eq $versaoArquivo) {
        Write-Output "OK: ja esta na versao $($atual) - nada a fazer"
        exit 0
    }
    Write-Output "versao atual: $($atual) -> atualizando para $($versaoArquivo)"
}

# --- 1) hash esperado (SHA256SUMS.txt do release, como o updater do app) ---
if ($Sha256 -eq '') {
    Write-Output "lendo hash de $urlSums"
    $sums = Join-Path $env:TEMP "SHA256SUMS-$($Tag).txt"
    (New-Object Net.WebClient).DownloadFile($urlSums, $sums)
    foreach ($linha in (Get-Content $sums)) {
        $t = $linha.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $espaco = $t.IndexOf(' ')
        if ($espaco -le 0) { continue }
        $hash = $t.Substring(0, $espaco).Trim().ToLowerInvariant()
        $nome = $t.Substring($espaco).Trim()
        if ($nome.StartsWith('*')) { $nome = $nome.Substring(1) }
        $nome = $nome.Replace('/', '\')
        if ($nome.EndsWith($arquivo, [StringComparison]::OrdinalIgnoreCase)) { $Sha256 = $hash; break }
    }
    Remove-Item $sums -ErrorAction SilentlyContinue
    if ($Sha256 -eq '') { throw "hash de $($arquivo) nao encontrado no SHA256SUMS.txt de $($Tag)" }
}

# --- 2) download e verificacao ---
Write-Output "baixando $urlExe"
(New-Object Net.WebClient).DownloadFile($urlExe, $destino)
$real = (Get-FileHash $destino -Algorithm SHA256).Hash.ToLowerInvariant()
if ($real -ne $Sha256.ToLowerInvariant()) {
    Remove-Item $destino -ErrorAction SilentlyContinue
    throw "SHA256 divergente (esperado $($Sha256), obtido $($real))"
}
Write-Output 'sha256 conferido'

# --- 3) fecha o app (cmd /c evita as pegadinhas de redirecionamento do PS 5.1) ---
cmd /c "taskkill /IM AvisoDeReinicio.exe /F >nul 2>&1" | Out-Null
if (Get-Process AvisoDeReinicio -ErrorAction SilentlyContinue) {
    cmd /c "schtasks /End /TN AvisoDeReinicioFallback >nul 2>&1" | Out-Null
    Start-Sleep -Seconds 2
}
if (Get-Process AvisoDeReinicio -ErrorAction SilentlyContinue) {
    throw @'
Nao consegui fechar o AvisoDeReinicio (a instancia pode estar elevada).
Opcoes: encerre pelo proprio icone da bandeja (Sair) e rode de novo,
ou execute este script em um prompt de administrador.
'@
}
Write-Output 'app fechado'

# --- 4) instalacao silenciosa ---
# Nao usar Start-Process -Wait: ele pendura nos handles do app que o
# instalador relanca no final. WaitForExit + polling de versao abaixo.
$switches = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if (-not $RespeitarAutostart) { $switches = "$($switches) /TASKS=autostart" }
Write-Output 'instalando...'
$p = Start-Process $destino $switches -PassThru
$p.WaitForExit(180000) | Out-Null

# --- 5) confirma a versao gravada no disco (ate 3 minutos) ---
$limite = (Get-Date).AddMinutes(3)
$final = ''
while ((Get-Date) -lt $limite) {
    if (Test-Path $exe) {
        $final = (Get-Item $exe).VersionInfo.FileVersion
        if ($final -eq $versaoArquivo) { break }
    }
    Start-Sleep -Seconds 2
}
if ($final -ne $versaoArquivo) {
    throw "instalacao nao confirmada (no disco: '$($final)'; esperada: '$($versaoArquivo)')"
}

Remove-Item $destino -ErrorAction SilentlyContinue
Write-Output "OK: versao $($final) instalada e conferida"
