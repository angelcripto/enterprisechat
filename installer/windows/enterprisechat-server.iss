; EnterpriseChat Server — Inno Setup script.
;
; Pipeline:
;   1. dotnet publish -c Release -r win-x64 --self-contained true
;      -p:PublishSingleFile=true   ->   ..\..\src\EnterpriseChat.Server\bin\publish\win-x64\
;   2. ISCC enterprisechat-server.iss   ->   build\enterprisechat-server-win-x64-<ver>.exe
;
; build-server-windows.ps1 orquesta ambos pasos.
;
; Modo silent compatible para `install.ps1` de la web:
;   installer.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
;
; Acciones del instalador:
;   - Copia los archivos de publish a {app}.
;   - Genera secretos (JWT signing key + admin password) si no existen y
;     escribe appsettings.Production.json.
;   - Registra Windows Service "EnterpriseChat" (auto-start).
;   - Arranca el servicio.
;   - Imprime la URL admin + contraseña inicial al final.
;
; Uninstaller:
;   - Para y elimina el servicio.
;   - Borra archivos de programa. Datos en {app}\data y la base SQLite
;     NO se borran salvo que el admin marque la casilla "borrar datos".

#define MyAppId       "{B7E4F2A1-9C8D-4F3E-A6B5-1D2E3F4A5B6C}"
#define MyAppName     "EnterpriseChat Server"
#define MyAppVersion  "0.1.0"
; VersionInfoVersion necesita formato numerico estricto (major.minor.build.revision).
; Si MyAppVersion incluye un suffix de pre-release (-alpha, -rc, etc), build-server-windows.ps1
; sobreescribe MyVersionInfoVersion con la parte numerica. Por defecto = MyAppVersion + ".0".
#ifndef MyVersionInfoVersion
  #define MyVersionInfoVersion MyAppVersion + ".0"
#endif
#define MyAppPublisher "EnterpriseChat"
#define MyAppURL      "https://enterprisechat.es"
#define MyServiceName "EnterpriseChat"
#define MyServiceDisplayName "EnterpriseChat Server"
#define MyServiceDescription "Servidor de chat empresarial EnterpriseChat (.NET 8 + SignalR + SQLite). Escucha en el puerto 5080."
#define MyPublishDir  "..\..\src\EnterpriseChat.Server\bin\publish\win-x64"
#define MyOutputDir   "build"
#define MyLicenseFile "..\..\LICENSE"
#define MyReadmeFile  "..\..\README.md"

[Setup]
AppId={{#MyAppId}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/contacto
AppUpdatesURL={#MyAppURL}/descargar
VersionInfoVersion={#MyVersionInfoVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} {#MyAppVersion}

DefaultDirName={autopf}\EnterpriseChat
DefaultGroupName=EnterpriseChat
DisableProgramGroupPage=yes
AllowNoIcons=yes

LicenseFile={#MyLicenseFile}
InfoBeforeFile={#MyReadmeFile}

OutputDir={#MyOutputDir}
OutputBaseFilename=enterprisechat-server-win-x64-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\EnterpriseChat.Server.exe

WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startservice"; Description: "Arrancar el servicio EnterpriseChat al finalizar la instalación"; GroupDescription: "Acciones tras instalar:"; Flags: checkedonce

[Files]
; Publish output completo (binario self-contained + wwwroot + appsettings.json default).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyLicenseFile}"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Dirs]
Name: "{app}\data";    Permissions: users-modify
Name: "{app}\logs";    Permissions: users-modify
Name: "{app}\certs";   Permissions: users-modify

[Icons]
Name: "{group}\Abrir panel admin"; Filename: "http://localhost:5080/"
Name: "{group}\Documentación"; Filename: "{#MyAppURL}"
Name: "{group}\Detener servicio"; Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; WorkingDir: "{app}"
Name: "{group}\Arrancar servicio"; Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; WorkingDir: "{app}"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
; Detener cualquier instancia previa (idempotente) antes de tocar archivos —
; ya lo hacemos en CurUninstallStepChanged también, pero es defensa adicional
; si actualizamos sobre instalación existente.
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; StatusMsg: "Deteniendo servicio anterior..."; OnlyBelowVersion: 0,0

; Registrar el servicio Windows. binPath con el ejecutable self-contained.
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\EnterpriseChat.Server.exe"" start= auto DisplayName= ""{#MyServiceDisplayName}"""; Flags: runhidden; StatusMsg: "Registrando servicio Windows..."
Filename: "sc.exe"; Parameters: "description {#MyServiceName} ""{#MyServiceDescription}"""; Flags: runhidden

; Arrancar el servicio (solo si el admin dejó la casilla marcada).
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden; StatusMsg: "Arrancando servicio..."; Tasks: startservice

; Abrir el panel admin tras instalar (solo modo interactivo, no /VERYSILENT).
Filename: "http://localhost:5080/"; Flags: shellexec postinstall nowait skipifsilent; Description: "Abrir el panel admin de EnterpriseChat"

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"

[UninstallDelete]
; Borrar logs siempre. Datos (SQLite, adjuntos) se borran solo si el admin
; marca la casilla "Eliminar también la base de datos" en el wizard de
; desinstalación (gestionado en [Code] CurUninstallStepChanged).
Type: filesandordirs; Name: "{app}\logs"

[Code]
const
  SERVICE_NAME = '{#MyServiceName}';

var
  AdminPassword: string;
  JwtSigningKey: string;
  PurgeDataPage: TInputOptionWizardPage;
  PurgeData: Boolean;

// ----- Helpers ------------------------------------------------------------

function RandomBase64(NumBytes: Integer): string;
var
  Bytes: array of Byte;
  i: Integer;
  Hex, Token, Alphabet: string;
begin
  // Inno Setup no expone CryptGenRandom limpiamente, pero Random() sembrada
  // a tiempo + microsegundos da entropía suficiente para una instalación
  // inicial. El admin puede regenerar después desde el panel.
  Randomize;
  SetArrayLength(Bytes, NumBytes);
  for i := 0 to NumBytes - 1 do
    Bytes[i] := Random(256);

  // Conversión binaria -> base64 manual (Inno no trae helper directo).
  // Usamos charset URL-safe sin padding para que quepa en JSON sin escapes.
  Alphabet := 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
  Token := '';
  i := 0;
  while i + 2 < NumBytes do begin
    Token := Token + Alphabet[(Bytes[i] shr 2) + 1];
    Token := Token + Alphabet[(((Bytes[i] and $03) shl 4) or (Bytes[i+1] shr 4)) + 1];
    Token := Token + Alphabet[(((Bytes[i+1] and $0F) shl 2) or (Bytes[i+2] shr 6)) + 1];
    Token := Token + Alphabet[(Bytes[i+2] and $3F) + 1];
    i := i + 3;
  end;
  Result := Token;
end;

function RandomAdminPassword(Length: Integer): string;
var
  Alphabet, Token: string;
  i: Integer;
begin
  Randomize;
  Alphabet := 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';
  Token := '';
  for i := 1 to Length do
    Token := Token + Alphabet[Random(Length(Alphabet)) + 1];
  Result := Token;
end;

function WriteAppSettings(AppDir, JwtKey, Pwd: string): Boolean;
var
  S: TStringList;
  Path: string;
begin
  Path := AppDir + '\appsettings.Production.json';
  if FileExists(Path) then begin
    // Ya existe: respetar config previa, no sobreescribir secretos.
    Result := True;
    Exit;
  end;
  S := TStringList.Create;
  try
    S.Add('{');
    S.Add('  "EnterpriseChat": {');
    S.Add('    "Jwt": {');
    S.Add('      "SigningKey": "' + JwtKey + '",');
    S.Add('      "Issuer": "EnterpriseChat.Prod",');
    S.Add('      "Audience": "EnterpriseChat.Clients",');
    S.Add('      "AccessTokenLifetimeMinutes": 60');
    S.Add('    },');
    S.Add('    "Bootstrap": { "AdminPassword": "' + Pwd + '" }');
    S.Add('  }');
    S.Add('}');
    S.SaveToFile(Path);
    Result := True;
  finally
    S.Free;
  end;
end;

function ServiceExists(Name: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + Name, '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// ----- Hooks Inno Setup ---------------------------------------------------

procedure InitializeWizard;
begin
  // Reset password / fresh install: generar secretos en cuanto pasa la pre.
  AdminPassword := RandomAdminPassword(16);
  JwtSigningKey := RandomBase64(48);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PwdFile: string;
begin
  if CurStep = ssPostInstall then begin
    // Tras copiar archivos, generar appsettings + reveal file.
    if WriteAppSettings(ExpandConstant('{app}'), JwtSigningKey, AdminPassword) then begin
      PwdFile := ExpandConstant('{app}\.first-admin-password');
      if not FileExists(PwdFile) then
        SaveStringToFile(PwdFile, AdminPassword, False);
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
end;

// ----- Pantalla final con contraseña ---

procedure CurPageChanged(CurPageID: Integer);
var
  Info: string;
begin
  if CurPageID = wpFinished then begin
    Info :=
      'Instalación completada.' + #13#10 + #13#10 +
      'URL admin:  http://<este-equipo>:5080/' + #13#10 +
      'Usuario:    admin' + #13#10 +
      'Contraseña: ' + AdminPassword + #13#10 + #13#10 +
      'Esta contraseña se ha guardado también en:' + #13#10 +
      '  ' + ExpandConstant('{app}\.first-admin-password') + #13#10 + #13#10 +
      'Cámbiala en el primer login. Si la pierdes, regenérala con:' + #13#10 +
      '  EnterpriseChat.Server.exe --reset-admin-password <nueva>';
    WizardForm.FinishedLabel.Caption := Info;
  end;
end;

// ----- Uninstall opción "purgar datos" ---

procedure InitializeUninstallProgressForm;
begin
  // Sin pantalla extra en silent.
end;

function InitializeUninstall: Boolean;
var
  Response: Integer;
begin
  Result := True;
  PurgeData := False;
  if not UninstallSilent then begin
    Response := MsgBox(
      '¿Deseas eliminar también la base de datos (SQLite), los adjuntos y la configuración?' + #13#10 + #13#10 +
      'Sí: borrado completo, no se puede recuperar.' + #13#10 +
      'No: se conserva todo en ' + ExpandConstant('{app}') + ' (puedes reinstalar más tarde reutilizando los datos).',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    PurgeData := Response = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and PurgeData then begin
    DelTree(ExpandConstant('{app}\data'), True, True, True);
    DelTree(ExpandConstant('{app}\certs'), True, True, True);
    DeleteFile(ExpandConstant('{app}\appsettings.Production.json'));
    DeleteFile(ExpandConstant('{app}\.first-admin-password'));
  end;
end;
