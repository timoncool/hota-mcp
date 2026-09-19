@echo off
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
set "out=build\v4"
if not exist "%out%" mkdir "%out%"
cl /nologo /std:c++17 /EHsc /W4 /utf-8 /MT /DUNICODE /D_UNICODE /LD launcher_tab.cpp /Fo%out%\launcher_tab.obj /Fe%out%\hota_launcher_tab.dll /link user32.lib comctl32.lib gdi32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /utf-8 /MT /DUNICODE /D_UNICODE attach.cpp /Fo%out%\attach.obj /Fe%out%\launcher-attach.exe /link user32.lib
exit /b %errorlevel%
