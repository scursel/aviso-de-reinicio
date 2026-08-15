@echo off
setlocal
cd /d "%~dp0"

if not exist "app.ico" (
    echo Gerando icone...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-icon.ps1"
)

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERRO] csc.exe nao encontrado. Instale o .NET Framework 4.x.
    pause
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:AvisoDeReinicio.exe AvisoDeReinicio.cs

if %errorLevel% neq 0 (
    echo.
    echo [ERRO] Falha na compilacao. Revise as mensagens acima.
    pause
    exit /b 1
)

echo.
echo [OK] Compilado: AvisoDeReinicio.exe
pause