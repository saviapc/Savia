@echo off
:: Compila Savia.exe con el compilador de C# que trae Windows (.NET Framework 4.x). No hace falta instalar nada.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%CSC%" (
    echo No se encontro csc.exe de .NET Framework 4.
    exit /b 1
)
if not exist "%~dp0dist" mkdir "%~dp0dist"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ^
 /out:"%~dp0dist\Savia.exe" ^
 /win32manifest:"%~dp0src\app.manifest" /win32icon:"%~dp0src\icono.ico" ^
 /resource:"%~dp0src\Ventana.xaml",Savia.Ventana.xaml ^
 /resource:"%~dp0src\icono.ico",Savia.icono.ico ^
 /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
 /r:"%NET%\System.Xaml.dll" /r:System.Management.dll /r:System.ServiceProcess.dll ^
 /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll /r:System.Core.dll ^
 "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
echo.
echo Listo: dist\Savia.exe
certutil -hashfile "%~dp0dist\Savia.exe" SHA256 | findstr /v ":"
