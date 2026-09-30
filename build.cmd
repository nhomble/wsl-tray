@echo off
setlocal
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist bin mkdir bin
if not exist obj mkdir obj
rem Generate wsl.ico (tiny helper tool) so the exe icon always matches the tray drawing.
"%CSC%" /nologo /target:exe /r:System.Drawing.dll /out:obj\mkico.exe tools\mkico.cs || exit /b 1
obj\mkico.exe wsl.ico || exit /b 1
rem Layering check: Core + Interop must compile with a minimal reference set. /noconfig is required: the default csc.rsp adds System.Windows.Forms and System.Drawing.
"%CSC%" /nologo /noconfig /target:library /optimize+ /r:System.dll,System.Core.dll /out:obj\coreCheck.dll src\Core\*.cs src\Interop\*.cs || (echo LAYERING CHECK FAILED: src\Core and src\Interop must not depend on WinForms/Drawing or on src\UI. & exit /b 1)
echo Core check OK (no WinForms/Drawing references in src\Core, src\Interop)
rem Version stamp: WSLTRAY_VERSION (e.g. 0.1.1; default "dev") -> obj\AssemblyInfo.g.cs. Validated inside the script.
powershell -NoProfile -ExecutionPolicy Bypass -File tools\mkver.ps1 obj\AssemblyInfo.g.cs || exit /b 1
"%CSC%" /nologo /target:winexe /optimize+ /win32manifest:app.manifest /win32icon:wsl.ico /r:System.Windows.Forms.dll,System.Drawing.dll /out:bin\WslTray.exe /recurse:src\*.cs tests\*.cs obj\AssemblyInfo.g.cs || exit /b 1
echo Built bin\WslTray.exe
exit /b 0
