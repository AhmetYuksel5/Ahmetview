@echo off
rem Her Windows'ta hazir gelen derleyiciyle Ahmetview.exe uretir; ek kurulum gerekmez.
rem GitHub'daki derleme de ayni dosyayi "/ci" ile cagiriyor; iki yol hep ayni kalsin diye.
cd /d "%~dp0"
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%NET%\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /win32icon:ahmetview.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\WindowsBase.dll" /r:"%NET%\WPF\PresentationFramework.dll" /r:"%NET%\WPF\WindowsFormsIntegration.dll" /out:Ahmetview.exe Program.cs Webp.cs Video.cs
set SONUC=%errorlevel%
if "%1"=="/ci" exit /b %SONUC%
if not "%SONUC%"=="0" (pause & exit /b 1)
echo Ahmetview.exe hazir.
pause
