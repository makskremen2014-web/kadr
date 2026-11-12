Unicode true
RequestExecutionLevel admin
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Kadr"
!define WEBVIEW_KEY "Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

Name "Кадр"
OutFile "${SETUP_EXE}"
InstallDir "$PROGRAMFILES64\Kadr"
VIProductVersion "1.0.0.0"
VIAddVersionKey /LANG=1049 "ProductName" "Кадр"
VIAddVersionKey /LANG=1049 "FileDescription" "Установщик рекордера Кадр"
VIAddVersionKey /LANG=1049 "FileVersion" "1.0.0"
VIAddVersionKey /LANG=1049 "LegalCopyright" "См. лицензии компонентов."

!define MUI_ABORTWARNING
!define MUI_ICON "${KADR_ICON}"
!define MUI_UNICON "${KADR_ICON}"
!define MUI_FINISHPAGE_TEXT "Установка завершена. Запустите «Кадр» с рабочего стола или из меню «Пуск»."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
  ${IfNot} ${IsNativeAMD64}
  ${OrIfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Требуется Windows 10 или новее на компьютере x64." /SD IDOK
    SetErrorLevel 1
    Quit
  ${EndIf}
FunctionEnd

Section
  SetShellVarContext all
  SetRegView 32
  ReadRegStr $0 HKLM "${WEBVIEW_KEY}" "pv"
  ${If} $0 == ""
  ${OrIf} $0 == "0.0.0.0"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR"
    File /oname=WebView2Setup.exe "${WEBVIEW_SETUP}"
    ExecWait '"$PLUGINSDIR\WebView2Setup.exe" /silent /install' $1
    ReadRegStr $0 HKLM "${WEBVIEW_KEY}" "pv"
    ${If} $0 == ""
    ${OrIf} $0 == "0.0.0.0"
      MessageBox MB_OK|MB_ICONSTOP "Не удалось установить Microsoft Edge WebView2. Проверьте подключение к интернету и повторите установку." /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}

  SetRegView 64
  SetOverwrite try
  SetOutPath "$INSTDIR"
  ClearErrors
  File "${APP_DIR}\Kadr.exe"
  File "${APP_DIR}\PresentMon.exe"
  File "${APP_DIR}\PresentMon-LICENSE.txt"
  File "${APP_DIR}\WebView2-LICENSE.txt"
  File "${APP_DIR}\Microsoft.Web.WebView2.Core.dll"
  File "${APP_DIR}\Microsoft.Web.WebView2.WinForms.dll"
  File "${APP_DIR}\WebView2Loader.dll"
  SetOutPath "$INSTDIR\ui"
  File "${APP_DIR}\ui\Screen Recorder.html"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "Закройте «Кадр» и повторите установку." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Кадр.lnk" "$INSTDIR\Kadr.exe"
  CreateShortcut "$SMPROGRAMS\Кадр.lnk" "$INSTDIR\Kadr.exe"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "Кадр"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "1.0.0"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Kadr.exe,0"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  SetRegView 64
  Delete "$INSTDIR\Kadr.exe"
  Delete "$INSTDIR\PresentMon.exe"
  ${If} ${FileExists} "$INSTDIR\Kadr.exe"
  ${OrIf} ${FileExists} "$INSTDIR\PresentMon.exe"
    MessageBox MB_OK|MB_ICONSTOP "Закройте «Кадр» и повторите удаление." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  Delete "$INSTDIR\PresentMon-LICENSE.txt"
  Delete "$INSTDIR\WebView2-LICENSE.txt"
  Delete "$INSTDIR\Microsoft.Web.WebView2.Core.dll"
  Delete "$INSTDIR\Microsoft.Web.WebView2.WinForms.dll"
  Delete "$INSTDIR\WebView2Loader.dll"
  Delete "$INSTDIR\ui\Screen Recorder.html"
  RMDir "$INSTDIR\ui"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$DESKTOP\Кадр.lnk"
  Delete "$SMPROGRAMS\Кадр.lnk"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
SectionEnd
