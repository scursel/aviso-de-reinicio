# Rede de seguranca: cria uma tarefa agendada no LOGON que relanca o
# Aviso de Reinicio caso ele tenha sido fechado/morto pelo usuario.
# Roda sem administrador (a tarefa pertence ao usuario atual).
$exe = Join-Path $PSScriptRoot 'AvisoDeReinicio.exe'
if (-not (Test-Path $exe)) { exit 0 }
try {
    $action  = New-ScheduledTaskAction -Execute $exe
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    Register-ScheduledTask -TaskName 'AvisoDeReinicioFallback' -Action $action -Trigger $trigger -Force -ErrorAction Stop | Out-Null
    Write-Output 'tarefa AvisoDeReinicioFallback criada'
} catch {
    Write-Output ('falha ao criar tarefa: ' + $_.Exception.Message)
}