; HotA MCP installer. Built by tools\build-installer.ps1, which passes VERSION, STAGE and OUTFILE.
;
; Per user, no administrator rights: the add-on goes into a folder of the player's choice, the game
; folder is only read. The «HotA MCP» shortcut starts the service, the player's own HD Launcher with
; the MCP tab and then the game. Nothing is put into the Windows autostart.

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!include MUI2.nsh
!include LogicLib.nsh
!include FileFunc.nsh

!ifndef VERSION
  !error "Build with tools\build-installer.ps1"
!endif
!ifndef STAGE
  !error "Build with tools\build-installer.ps1"
!endif
!ifndef OUTFILE
  !error "Build with tools\build-installer.ps1"
!endif

!define APP "HotA MCP"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HotaMcp"
!define SETTINGS_KEY "Software\HotaMcp"

Name "${APP} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\HotaMcp"
InstallDirRegKey HKCU "${SETTINGS_KEY}" "InstallDir"
BrandingText "${APP} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "${APP}"
VIAddVersionKey /LANG=1049 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик ${APP}"
VIAddVersionKey /LANG=1049 "LegalCopyright" "MIT, timoncool"

Var GameDir

!define MUI_ICON "${__FILEDIR__}\..\..\src\HotaMcp\hota-mcp.ico"
!define MUI_UNICON "${__FILEDIR__}\..\..\src\HotaMcp\hota-mcp.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Установка ${APP} ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "MCP-сервер для Heroes of Might and Magic III: Horn of the Abyss: ИИ-агент играет в игру через мост как живой игрок.$\r$\n$\r$\nНужны установленные Heroes III Complete (GOG), HotA и HD Mod. В папке игры ничего не меняется, прав администратора не требуется.$\r$\n$\r$\nЕсли запущен HD Launcher, закройте его перед установкой."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${STAGE}\LICENSE.txt"

; The game folder: found by itself when it can be, always checked before going on.
!define MUI_PAGE_HEADER_TEXT "Папка игры"
!define MUI_PAGE_HEADER_SUBTEXT "Где установлены Heroes III Complete с HotA и HD Mod."
!define MUI_DIRECTORYPAGE_TEXT_TOP "Укажите папку игры: в ней должны лежать h3hota HD.exe, HotA.dll и HD_Launcher.exe. Мост её только читает."
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "Папка Heroes III Complete"
!define MUI_DIRECTORYPAGE_VARIABLE $GameDir
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE GameDirLeave
!insertmacro MUI_PAGE_DIRECTORY

!define MUI_PAGE_HEADER_TEXT "Папка ${APP}"
!define MUI_PAGE_HEADER_SUBTEXT "Куда поставить службу, справочник и навык агента."
!define MUI_DIRECTORYPAGE_TEXT_TOP "Служба ставится для текущего пользователя. Данные партий (планы, журналы, стоимость) хранятся отдельно, в %LOCALAPPDATA%\HotaMcp."
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE InstallDirLeave
!insertmacro MUI_PAGE_DIRECTORY

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "${APP} установлен"
!define MUI_FINISHPAGE_TEXT "Всё запускается ярлыком «${APP}» на рабочем столе и в меню Пуск: служба, ваш HD Launcher со вкладкой MCP, затем игра.$\r$\n$\r$\nMCP-клиент подключается к http://127.0.0.1:18773/mcp или через stdio: $\"$INSTDIR\app\HotaMcp.exe$\" --stdio. Подробно — timoncool.github.io/hota-mcp."
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\HotaMcp.exe"
!define MUI_FINISHPAGE_RUN_PARAMETERS "--launch"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить ${APP} (служба, лаунчер и игра)"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Russian"

; The launcher holds the tab DLL until it exits and the service lives with it: both must be closed
; for their files to be replaced or removed.
!macro RequireClosed prefix
Function ${prefix}RequireClosed
  retry:
    nsExec::ExecToStack 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "if (Get-Process HD_Launcher,HotaMcp -ErrorAction SilentlyContinue) { exit 1 }"'
    Pop $0
    Pop $1
    ${If} $0 == 1
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Запущен HD Launcher или служба ${APP}. Закройте HD Launcher (служба закроется вместе с ним) и нажмите «Повтор»." /SD IDCANCEL IDRETRY retry
      Abort
    ${ElseIf} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "Не удалось проверить запущенные процессы (PowerShell ответил: $0 $1)." /SD IDOK
      Abort
    ${EndIf}
FunctionEnd
!macroend
!insertmacro RequireClosed ""
!insertmacro RequireClosed "un."

Function IsGameDir
  ; $R0 = folder; returns 1 in $R1 when the three files of a supported installation are there.
  StrCpy $R1 0
  ${If} ${FileExists} "$R0\h3hota HD.exe"
  ${AndIf} ${FileExists} "$R0\HotA.dll"
  ${AndIf} ${FileExists} "$R0\HD_Launcher.exe"
    StrCpy $R1 1
  ${EndIf}
FunctionEnd

Function FindOnDrive
  ; ${GetDrives} callback: $9 is the drive root; pushing «StopGetDrives» ends the walk.
  StrCpy $0 ""
  ${If} $GameDir == ""
    ${ForEach} $R2 0 2 + 1
      ${Select} $R2
        ${Case} 0
          StrCpy $R0 "$9HoMM 3 Complete"
        ${Case} 1
          StrCpy $R0 "$9Games\HoMM 3 Complete"
        ${Case} 2
          StrCpy $R0 "$9GOG Games\HoMM 3 Complete"
      ${EndSelect}
      Call IsGameDir
      ${If} $R1 == 1
        StrCpy $GameDir $R0
        StrCpy $0 "StopGetDrives"
        ${Break}
      ${EndIf}
    ${Next}
  ${EndIf}
  Push $0
FunctionEnd

Function .onInit
  ; Earlier choice, then GOG's own record, then the usual folders on every fixed drive.
  ReadRegStr $GameDir HKCU "${SETTINGS_KEY}" "GameDir"
  StrCpy $R0 $GameDir
  Call IsGameDir
  ${If} $R1 != 1
    ReadRegStr $R0 HKLM "SOFTWARE\GOG.com\Games\1207658787" "path"
    Call IsGameDir
    ${If} $R1 == 1
      StrCpy $GameDir $R0
    ${Else}
      StrCpy $GameDir ""
      ${GetDrives} "HDD" "FindOnDrive"
    ${EndIf}
  ${EndIf}
FunctionEnd

Function GameDirLeave
  StrCpy $R0 $GameDir
  Call IsGameDir
  ${If} $R1 != 1
    MessageBox MB_OK|MB_ICONEXCLAMATION "В папке $\"$GameDir$\" нет h3hota HD.exe, HotA.dll и HD_Launcher.exe.$\r$\nУстановите Heroes III Complete (GOG), затем HotA, затем HD Mod и укажите папку игры."
    Abort
  ${EndIf}
FunctionEnd

Function InstallDirLeave
  Call RequireClosed
FunctionEnd

Section "${APP}" SecMain
  SectionIn RO
  ; A silent install (/S /D=<folder>) skips the pages and their checks: they are made here.
  ${If} ${Silent}
    StrCpy $R0 $GameDir
    Call IsGameDir
    ${If} $R1 != 1
      SetErrorLevel 2
      Abort "Папка игры не найдена: укажите её при обычной установке."
    ${EndIf}
    Call RequireClosed
  ${EndIf}

  ; The service, its native game adapter and the launcher tab live side by side: the tab starts
  ; the service from its own folder.
  SetOutPath "$INSTDIR"
  File /r "${STAGE}\app"
  ; The reference and the skill are served offline from next to the binaries; they are replaced
  ; whole, so a page removed upstream does not linger.
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\skills"
  File /r "${STAGE}\docs"
  File /r "${STAGE}\skills"
  File "${STAGE}\LICENSE.txt"

  ; A client connecting to a cold machine needs to know which launcher to raise.
  CreateDirectory "$LOCALAPPDATA\HotaMcp"
  FileOpen $0 "$LOCALAPPDATA\HotaMcp\install.ini" w
  FileWriteUTF16LE /BOM $0 "Launcher=$GameDir\HD_Launcher.exe"
  FileClose $0

  ; Earlier versions kept a watcher in the Windows autostart; the start is now the shortcut.
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "HotaMcp"

  SetOutPath "$INSTDIR\app"
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\app\HotaMcp.exe" "--launch" "$INSTDIR\app\HotaMcp.exe" 0 SW_SHOWMINIMIZED "" "${APP}: служба, HD Launcher со вкладкой MCP, затем игра"
  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\app\HotaMcp.exe" "--launch" "$INSTDIR\app\HotaMcp.exe" 0 SW_SHOWMINIMIZED "" "${APP}: служба, HD Launcher со вкладкой MCP, затем игра"
  CreateShortcut "$SMPROGRAMS\${APP}\Удалить ${APP}.lnk" "$INSTDIR\uninstall.exe"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKCU "${SETTINGS_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${SETTINGS_KEY}" "GameDir" "$GameDir"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "timoncool"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/timoncool/hota-mcp"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\app\HotaMcp.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $0
SectionEnd

Function un.onInit
  Call un.RequireClosed
FunctionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APP}.lnk"
  RMDir /r "$SMPROGRAMS\${APP}"

  ; Only what the installer put here; anything else the player keeps in the folder stays.
  RMDir /r "$INSTDIR\app"
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\skills"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "Удалить и данные службы — планы, журналы партий, учёт стоимости, ключ подключения — из %LOCALAPPDATA%\HotaMcp?$\r$\n$\r$\n«Нет» оставит их для следующей установки." /SD IDNO IDNO keep
    RMDir /r "$LOCALAPPDATA\HotaMcp"
  keep:

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "HotaMcp"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${SETTINGS_KEY}"
SectionEnd
