Unicode true
Name "Wireless Battery Tray"
OutFile "WirelessBatteryTray-Setup.exe"
InstallDir "$LOCALAPPDATA\WirelessBatteryTray"
RequestExecutionLevel user
!ifndef VERSION
  !define VERSION "dev"
!endif
!define EXE "WirelessBatteryTray-${VERSION}.exe"
!define UNINST "Software\Microsoft\Windows\CurrentVersion\Uninstall\WirelessBatteryTray"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

!macro CloseApp
  nsExec::Exec `powershell -NoProfile -Command "Get-Process WirelessBatteryTray*,BateriaEnBandeja* -EA 0 | ? { $$_.Path -notlike '*Setup*' -and $$_.ProcessName -notlike '*Setup*' } | Stop-Process -Force; Get-CimInstance Win32_Process -Filter \"Name='powershell.exe'\" | ? CommandLine -like '*BateriaBandeja*' | % { Stop-Process -Id $$_.ProcessId -Force }"`
  Sleep 800
!macroend

Section "Install"
  !insertmacro CloseApp
  SetOutPath "$INSTDIR"

  ; migrate from the old Spanish-named install
  IfFileExists "$LOCALAPPDATA\BateriaEnBandeja\settings.ini" 0 +2
    CopyFiles /SILENT "$LOCALAPPDATA\BateriaEnBandeja\settings.ini" "$INSTDIR"
  IfFileExists "$LOCALAPPDATA\BateriaEnBandeja\iCUESDK.x64_2019.dll" 0 +2
    CopyFiles /SILENT "$LOCALAPPDATA\BateriaEnBandeja\iCUESDK.x64_2019.dll" "$INSTDIR"
  RMDir /r "$LOCALAPPDATA\BateriaEnBandeja"
  Delete "$SMSTARTUP\Bateria en Bandeja.lnk"
  RMDir /r "$SMPROGRAMS\Bateria en Bandeja"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BateriaEnBandeja"

  ; if the exe is somehow still locked, move it aside
  ClearErrors
  Delete "$INSTDIR\${EXE}"
  IfErrors 0 +2
    Rename "$INSTDIR\${EXE}" "$INSTDIR\${EXE}.old"

  File "${EXE}"
  SetOverwrite off
  File /nonfatal "iCUESDK.x64_2019.dll"
  SetOverwrite on
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$SMSTARTUP\Wireless Battery Tray.lnk" "$INSTDIR\${EXE}"
  CreateDirectory "$SMPROGRAMS\Wireless Battery Tray"
  CreateShortcut "$SMPROGRAMS\Wireless Battery Tray\Wireless Battery Tray.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$SMPROGRAMS\Wireless Battery Tray\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINST}" "DisplayName" "Wireless Battery Tray"
  WriteRegStr HKCU "${UNINST}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST}" "Publisher" "lDelia43"
  WriteRegStr HKCU "${UNINST}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "${UNINST}" "UninstallString" '"$INSTDIR\Uninstall.exe"'

  SetOutPath "$INSTDIR"
  ExecShell "open" "$INSTDIR\${EXE}"

  ; remove older versions
  Sleep 1500
  FindFirst $0 $1 "$INSTDIR\WirelessBatteryTray-*.exe*"
  loop:
    StrCmp $1 "" done
    StrCmp $1 "${EXE}" next
    Delete "$INSTDIR\$1"
  next:
    FindNext $0 $1
    Goto loop
  done:
  FindClose $0
SectionEnd

Section "Uninstall"
  !insertmacro CloseApp
  Delete "$SMSTARTUP\Wireless Battery Tray.lnk"
  RMDir /r "$SMPROGRAMS\Wireless Battery Tray"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINST}"
SectionEnd
