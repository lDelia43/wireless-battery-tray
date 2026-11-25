Unicode true
Name "Wireless Battery Tray"
OutFile "WirelessBatteryTray-Setup.exe"
InstallDir "$LOCALAPPDATA\WirelessBatteryTray"
RequestExecutionLevel user
Icon "app.ico"
UninstallIcon "app.ico"
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
  nsExec::ExecToStack '"$SYSDIR\taskkill.exe" /F /FI "IMAGENAME eq WirelessBatteryTray-1*"'
  Pop $8
  Pop $7
  !insertmacro Log "closeapp exit=$8 out=$7"
  nsExec::ExecToStack '"$SYSDIR\taskkill.exe" /F /FI "IMAGENAME eq BateriaEnBandeja*"'
  Pop $8
  Pop $7
  Sleep 800
!macroend

!macro Log TXT
  !ifdef DEBUGLOG
    IfFileExists "$PROFILE\Downloads\wbt-install-log.txt" +3 0
    FileOpen $9 "$PROFILE\Downloads\wbt-install-log.txt" w
    Goto +2
    FileOpen $9 "$PROFILE\Downloads\wbt-install-log.txt" a
    FileSeek $9 0 END
    FileWrite $9 "${TXT}$\r$\n"
    FileClose $9
  !endif
!macroend

Section "Install"
  !insertmacro Log "--- install ${VERSION} ---"
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

  ClearErrors
  File "${EXE}"
  IfErrors 0 +2
    !insertmacro Log "FILE COPY ERROR"
  IfFileExists "$INSTDIR\${EXE}" 0 +2
    !insertmacro Log "exe present: $INSTDIR\${EXE}"
  SetOverwrite off
  File /nonfatal "iCUESDK.x64_2019.dll"
  SetOverwrite on
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  FileOpen $6 "$INSTDIR\current.txt" w
  FileWrite $6 "${EXE}"
  FileClose $6

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
  ClearErrors
  ExecShell "open" "$INSTDIR\${EXE}"
  IfErrors 0 +2
    !insertmacro Log "EXECSHELL ERROR"
  !insertmacro Log "launched"

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
  !insertmacro Log "done"
SectionEnd

Section "Uninstall"
  !insertmacro CloseApp
  Delete "$SMSTARTUP\Wireless Battery Tray.lnk"
  RMDir /r "$SMPROGRAMS\Wireless Battery Tray"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINST}"
SectionEnd
