; Modern NSIS MUI2 Installer Script for DarkTunnel Client (Offline Standalone)
; Usage: makensis /DAPP_VERSION=1.0.1 /DARCH=win-x64 /DSOURCE_DIR=..\out\win-x64 installer\setup.nsi

!ifndef APP_VERSION
  !define APP_VERSION "1.0.1"
!endif

!ifndef ARCH
  !define ARCH "win-x64"
!endif

!ifndef SOURCE_DIR
  !define SOURCE_DIR "..\out\win-x64"
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

Name "${PRODUCT_NAME} v${APP_VERSION}"
OutFile "..\out\dist\DarkTunnel-Client-Setup-v${APP_VERSION}-${ARCH}.exe"
InstallDir "$PROGRAMFILES64\DarkTunnel Client"
ShowInstDetails show
ShowUnInstDetails show

Section "MainSection" SEC01
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer
  File /r "${SOURCE_DIR}\*.*"

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

Function un.onUninstSuccess
  HideWindow
  MessageBox MB_OK "DarkTunnel Client was successfully removed from your computer."
FunctionEnd

Function un.onInit
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "Are you sure you want to completely remove DarkTunnel Client and all of its components?" IDYES +2
  Abort
FunctionEnd

Section Uninstall
  Delete "$DESKTOP\DarkTunnel Client.lnk"
  Delete "$SMPROGRAMS\DarkTunnel Client\DarkTunnel Client.lnk"
  RMDir "$SMPROGRAMS\DarkTunnel Client"

  RMDir /r "$INSTDIR"

  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  SetAutoClose true
SectionEnd
