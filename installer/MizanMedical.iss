#ifndef Arch
  #error Arch must be defined by /DArch
#endif
#ifndef SourceDir
  #error SourceDir must be defined by /DSourceDir
#endif
#ifndef OutputDir
  #error OutputDir must be defined by /DOutputDir
#endif

[Setup]
AppName=ميزان الطبي
AppVersion=0.1.0
#if Arch == "win-x64"
AppId={{52D6CE88-57F7-4897-A6BD-926DB5FCB194}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
AppId={{CB195955-0A5B-48D6-87F3-1A96C0D767D7}
ArchitecturesAllowed=x86compatible
#endif
DefaultDirName={autopf}\MizanMedical-{#Arch}
DefaultGroupName=ميزان الطبي
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=MizanMedical-{#Arch}-Installer
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\MizanMedical.Desktop.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; Excludes: "mizan-client.json"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\mizan-client.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autoprograms}\ميزان الطبي"; Filename: "{app}\MizanMedical.Desktop.exe"
Name: "{autodesktop}\ميزان الطبي"; Filename: "{app}\MizanMedical.Desktop.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "خيارات إضافية:"; Flags: unchecked

[Run]
Filename: "{app}\MizanMedical.Desktop.exe"; Description: "تشغيل ميزان الطبي"; Flags: nowait postinstall skipifsilent
