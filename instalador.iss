; ============================================================================
;  Instalador do Aviso de Reinicio
;  Desenvolvido por Scursel - projeto open source (licenca MIT)
;  Compilar com o Inno Setup: ISCC.exe instalador.iss
; ============================================================================

#define MyAppName "Aviso de Reinício"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Scursel"
#define MyAppExeName "AvisoDeReinicio.exe"

[Setup]
AppId={{993A5A15-A7C4-460D-A695-4A6190922B4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Desenvolvido por Scursel (licenca MIT)
DefaultDirName={localappdata}\Programs\AvisoDeReinicio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=saida
OutputBaseFilename=Instalador-AvisoDeReinicio-v{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "portuguesebr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na &Área de Trabalho"; GroupDescription: "Atalhos:"
Name: "autostart"; Description: "&Iniciar automaticamente com o Windows (recomendado)"; GroupDescription: "Opções:"; Flags: checkedonce

[Files]
Source: "AvisoDeReinicio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "registrar-tarefa.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AvisoDeReinicio"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\registrar-tarefa.ps1"""; Flags: runhidden nowait
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar o {#MyAppName} agora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM AvisoDeReinicio.exe /F"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "powershell.exe"; Parameters: "-NoProfile -WindowStyle Hidden -Command ""Unregister-ScheduledTask -TaskName 'AvisoDeReinicioFallback' -Confirm:$false -ErrorAction SilentlyContinue"""; Flags: runhidden; RunOnceId: "RemoveTask"