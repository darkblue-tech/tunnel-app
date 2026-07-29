; Modern NSIS MUI2 Online Web Installer Script for DarkTunnel Client
; Usage: makensis /DAPP_VERSION=1.0.1 /DARCH=win-x64 /DDOWNLOAD_URL=https://github.com/darkblue-tech/tunnel-app/releases/download/v1.0.1/DarkTunnel-Client-v1.0.1-win-x64.zip installer\web-setup.nsi

!ifndef APP_VERSION
  !define APP_VERSION "1.0.1"
!endif

!ifndef ARCH
  !define ARCH "win-x64"
!endif

!ifndef DOWNLOAD_URL
  !define DOWNLOAD_URL "https://github.com/darkblue-tech/tunnel-app/releases/download/v1.0.1/DarkTunnel-Client-v1.0.1-win-x64.zip"
!endif

!define PRODUCT_NAME "DarkTunnel Client"
!define PRODUCT_PUBLISHER "darkblue.tech"
!define PRODUCT_WEB_SITE "https://tunnel.darkblue.tech"
!define PRODUCT_EXE "DarkTunnel Client.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

SetCompressor /SOLID lzma
RequestExecutionLevel admin

!include "MUI2.nsh"
!include "x64.nsh"

; MUI Settings
!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Header\nsis.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${NSISDIR}\Contrib\Graphics\Wizard\win.bmp"

; Modern UI Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!insertmacro MUI_PAGE_FINISH

; Uninstaller Pages
!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; Languages
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

Name "${PRODUCT_NAME} v${APP_VERSION} (Web Setup)"
OutFile "..\out\dist\DarkTunnel-Client-WebSetup-v${APP_VERSION}-${ARCH}.exe"
InstallDir "$PROGRAMFILES64\DarkTunnel Client"
ShowInstDetails show
ShowUnInstDetails show

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  InitPluginsDir
  
  DetailPrint "Downloading release payload from ${DOWNLOAD_URL}..."
  NSISdl::download "${DOWNLOAD_URL}" "$PLUGINSDIR\payload.zip"
  Pop $R0
  StrCmp $R0 "success" download_ok
  
  DetailPrint "Downloading via PowerShell..."
  nsExec::Exec 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object System.Net.WebClient).DownloadFile(\"${DOWNLOAD_URL}\", \"$PLUGINSDIR\\payload.zip\")"'
  
download_ok:
  DetailPrint "Extracting application payload..."
  nsExec::Exec 'powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path \"$PLUGINSDIR\\payload.zip\" -DestinationPath \"$INSTDIR\" -Force"'

  ; Create Shortcuts
  CreateDirectory "$SMPROGRAMS\DarkTunnel Client"
  CreateShortCut "$SMPROGRAMS\DarkTunnel Client\DarkTunnel Client.lnk" "$INSTDIR\${PRODUCT_EXE}"
  CreateShortCut "$DESKTOP\DarkTunnel Client.lnk" "$INSTDIR\${PRODUCT_EXE}"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
SectionEnd

Section Uninstall
  Delete "$DESKTOP\DarkTunnel Client.lnk"
  Delete "$SMPROGRAMS\DarkTunnel Client\DarkTunnel Client.lnk"
  RMDir "$SMPROGRAMS\DarkTunnel Client"

  RMDir /r "$INSTDIR"

  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  SetAutoClose true
SectionEnd
