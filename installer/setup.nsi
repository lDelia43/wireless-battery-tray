Unicode true
Name "Wireless Battery Tray"
OutFile "WirelessBatteryTray-Setup.exe"
InstallDir "$LOCALAPPDATA\BateriaEnBandeja"
RequestExecutionLevel user
!ifndef VERSION
  !define VERSION "dev"
!endif
!define EXE "BateriaEnBandeja-${VERSION}.exe"

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Instalar"
  SetOutPath "$INSTDIR"
  Delete "$SMSTARTUP\Bateria en Bandeja.lnk"
  RMDir /r "$SMPROGRAMS\Bateria en Bandeja"
  Delete "$INSTDIR\BateriaBandeja.ps1"
  Delete "$INSTDIR\IniciarBateria.vbs"
  Delete "$INSTDIR\debug.txt"
  Delete "$INSTDIR\posicion*.txt"
  File "${EXE}"
  SetOverwrite off
  File /nonfatal "iCUESDK.x64_2019.dll"
  SetOverwrite on
  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  CreateShortcut "$SMSTARTUP\Wireless Battery Tray.lnk" "$INSTDIR\${EXE}"
  CreateDirectory "$SMPROGRAMS\Wireless Battery Tray"
  CreateShortcut "$SMPROGRAMS\Wireless Battery Tray\Wireless Battery Tray.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$SMPROGRAMS\Wireless Battery Tray\Uninstall.lnk" "$INSTDIR\Desinstalar.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BateriaEnBandeja" "DisplayName" "Wireless Battery Tray"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BateriaEnBandeja" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BateriaEnBandeja" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  ; the new instance closes the old one on startup
  Exec '"$INSTDIR\${EXE}"'
  ; remove old versions
  Sleep 2500
  FindFirst $0 $1 "$INSTDIR\BateriaEnBandeja*.exe"
  loop:
    StrCmp $1 "" fin
    StrCmp $1 "${EXE}" sig
    Delete "$INSTDIR\$1"
  sig:
    FindNext $0 $1
    Goto loop
  fin:
  FindClose $0
  nsExec::Exec `powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='powershell.exe'\" | ? CommandLine -like '*BateriaBandeja*' | % { Stop-Process -Id $$_.ProcessId -Force }"`
SectionEnd

Section "Uninstall"
  nsExec::Exec 'powershell -NoProfile -Command "Get-Process BateriaEnBandeja* | Stop-Process -Force"'
  Sleep 1000
  Delete "$SMSTARTUP\Wireless Battery Tray.lnk"
  RMDir /r "$SMPROGRAMS\Wireless Battery Tray"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BateriaEnBandeja"
SectionEnd
