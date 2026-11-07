Unicode true
!include "MUI2.nsh"

Name "论文原始数据归档平台"
OutFile "PaperArchive_WinUI3_Setup_x64.exe"
InstallDir "$LOCALAPPDATA\Programs\PaperArchive"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetOverwrite on
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "0.6.0.0"
VIAddVersionKey "ProductName" "论文原始数据归档平台"
VIAddVersionKey "FileDescription" "论文原始数据归档平台 Windows 安装程序"
VIAddVersionKey "FileVersion" "0.6.0"
VIAddVersionKey "ProductVersion" "0.6.0"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\PaperArchive.Native.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动论文原始数据归档平台"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Section "主程序" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "native-dist\*.*"
  CreateDirectory "$SMPROGRAMS\PaperArchive"
  CreateShortCut "$SMPROGRAMS\PaperArchive\论文原始数据归档平台.lnk" "$INSTDIR\PaperArchive.Native.exe"
  CreateShortCut "$SMPROGRAMS\PaperArchive\卸载.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "DisplayName" "论文原始数据归档平台"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "DisplayVersion" "0.6.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "Publisher" "PaperArchive"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "DisplayIcon" "$INSTDIR\PaperArchive.Native.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative" "NoRepair" 1
  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\PaperArchive\论文原始数据归档平台.lnk"
  Delete "$SMPROGRAMS\PaperArchive\卸载.lnk"
  RMDir "$SMPROGRAMS\PaperArchive"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PaperArchiveNative"
  ; Auto-generated exact app file list. Never recursively delete arbitrary user files.
  !include "native-dist-uninstall.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; D:\PaperArchiveData and external archive folders are untouched.
SectionEnd
