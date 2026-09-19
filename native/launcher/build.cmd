@echo off
setlocal
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
if not exist build mkdir build
cl /nologo /std:c++17 /EHsc /W4 /utf-8 /MT /DUNICODE /D_UNICODE /LD launcher_tab.cpp /Fobuild\launcher_tab.obj /Febuild\hota_launcher_tab.dll /link user32.lib comctl32.lib gdi32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /utf-8 /MT /DUNICODE /D_UNICODE attach.cpp /Fobuild\attach.obj /Febuild\launcher-attach.exe /link user32.lib
exit /b %errorlevel%
