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
; build-server-windows.ps1 sobreescribe MyAppVersion y MyVersionInfoVersion
; mediante `/DMyAppVersion=...` y `/DMyVersionInfoVersion=...` al invocar ISCC.
; Los #ifndef permiten compilar manualmente el .iss con valores por defecto.
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
; VersionInfoVersion necesita formato numerico estricto (major.minor.build.revision).
; Si MyAppVersion incluye suffix de pre-release (-alpha, -rc, etc), build-server-windows.ps1
; calcula el quad numerico aparte. Fallback al manual = MyAppVersion + ".0".
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
#define MyLicenseFile "LICENSE-es.txt"
#define MyLicenseFileOfficial "..\..\LICENSE"
#define MyReadmeFile  "README-INSTALL-es.txt"
#define MyIconFile    "assets\enterprisechat.ico"

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
AllowNoIcons=no

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
UninstallDisplayIcon={app}\enterprisechat.ico
SetupIconFile={#MyIconFile}

WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startservice"; Description: "Arrancar el servicio EnterpriseChat al finalizar la instalación"; GroupDescription: "Acciones tras instalar:"; Flags: checkedonce
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Iconos adicionales:"; Flags: checkedonce

[Files]
; Publish output completo (binario self-contained + wwwroot + appsettings.json default).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyLicenseFileOfficial}"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#MyIconFile}"; DestDir: "{app}"; DestName: "enterprisechat.ico"; Flags: ignoreversion

[Dirs]
Name: "{app}\data";    Permissions: users-modify
Name: "{app}\logs";    Permissions: users-modify
Name: "{app}\certs";   Permissions: users-modify

[Icons]
; Carpeta del Menú Inicio. Iconos con el .ico empaquetado para que tengan
; identidad visual en lugar del icono genérico de Windows.
Name: "{group}\Abrir panel admin EnterpriseChat"; Filename: "http://localhost:5080/"; IconFilename: "{app}\enterprisechat.ico"
Name: "{group}\Arrancar servicio"; Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; IconFilename: "{app}\enterprisechat.ico"; Comment: "Inicia el servicio Windows EnterpriseChat (requiere admin)"
Name: "{group}\Detener servicio"; Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; IconFilename: "{app}\enterprisechat.ico"; Comment: "Detiene el servicio Windows EnterpriseChat (requiere admin)"
Name: "{group}\Reiniciar servicio"; Filename: "{cmd}"; Parameters: "/C ""sc stop {#MyServiceName} & timeout /t 3 /nobreak >NUL & sc start {#MyServiceName}"""; IconFilename: "{app}\enterprisechat.ico"; Comment: "Detiene y arranca el servicio (requiere admin)"
Name: "{group}\Documentación"; Filename: "{#MyAppURL}"; IconFilename: "{app}\enterprisechat.ico"
Name: "{group}\Desinstalar EnterpriseChat Server"; Filename: "{uninstallexe}"; IconFilename: "{app}\enterprisechat.ico"

; Acceso directo en el escritorio (opcional, controlado por task).
Name: "{autodesktop}\EnterpriseChat (panel admin)"; Filename: "http://localhost:5080/"; IconFilename: "{app}\enterprisechat.ico"; Tasks: desktopicon

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

// Inno PascalScript no incluye Randomize/Random ni acceso a
// RandomNumberGenerator del CLR. Delegamos en powershell.exe (presente en
// cualquier Windows Server 2008+) que sí tiene
// System.Security.Cryptography.RandomNumberGenerator. Escribimos un .ps1
// temporal, lo ejecutamos, leemos stdout y lo borramos.

function ExecPowerShellCaptureOutput(ScriptBody: string): string;
var
  ScriptFile, OutFile: string;
  FullScript: string;
  Buf: AnsiString;
  Code: Integer;
begin
  // Nombres fijos en {tmp}. Las llamadas son secuenciales (RandomBase64 +
  // RandomAdminPassword) y limpiamos los archivos al final de cada
  // invocación, así que no hay colisión.
  ScriptFile := ExpandConstant('{tmp}\ec-randgen.ps1');
  OutFile    := ExpandConstant('{tmp}\ec-randgen.out');

  // El propio script PowerShell escribe a $OutFile para evitar el quoting
  // hell de redirigir stdout via cmd.
  FullScript :=
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    '$value = & {' + #13#10 + ScriptBody + #13#10 + '}' + #13#10 +
    'Set-Content -Path ''' + OutFile + ''' -Value $value -NoNewline -Encoding ASCII' + #13#10;

  SaveStringToFile(ScriptFile, FullScript, False);

  Exec('powershell.exe',
       '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, Code);

  Result := '';
  if FileExists(OutFile) then begin
    if LoadStringFromFile(OutFile, Buf) then
      Result := Trim(string(Buf));
    DeleteFile(OutFile);
  end;
  DeleteFile(ScriptFile);
end;

function RandomBase64(NumBytes: Integer): string;
begin
  // RandomNumberGenerator.GetBytes() es cripto-seguro en .NET Framework
  // (disponible en cualquier Windows soportado).
  Result := ExecPowerShellCaptureOutput(
    '$bytes = New-Object byte[] ' + IntToStr(NumBytes) + ';' + #13#10 +
    '[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes);' + #13#10 +
    '[Convert]::ToBase64String($bytes)'
  );
end;

function RandomAdminPassword(Len: Integer): string;
begin
  // Charset sin caracteres ambiguos (0/O, 1/l/I).
  Result := ExecPowerShellCaptureOutput(
    '$alphabet = [char[]]''ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'';' + #13#10 +
    '$bytes = New-Object byte[] ' + IntToStr(Len) + ';' + #13#10 +
    '[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes);' + #13#10 +
    '-join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })'
  );
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

function PortInUseByForeignProcess(Port: Integer): Boolean;
var
  OutFile: string;
  Buf: AnsiString;
  Cmd: string;
  Code: Integer;
begin
  // PowerShell devuelve "1" si el puerto esta ocupado por un proceso CUYA
  // ruta NO contiene EnterpriseChat (ergo, no es nuestro propio servicio
  // re-iniciandose ni nuestro publish dir). En caso de error o ausencia
  // de PowerShell devolvemos False (no bloqueamos la instalacion por
  // falta de diagnostico).
  OutFile := ExpandConstant('{tmp}\ec-portcheck.txt');
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$c = Get-NetTCPConnection -LocalPort ' + IntToStr(Port) +
    ' -State Listen -ErrorAction SilentlyContinue;' +
    'if (-not $c) { ''0'' | Set-Content -NoNewline ''' + OutFile + '''; exit }; ' +
    '$p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue;' +
    'if ($p -and $p.Path -and ($p.Path -like ''*EnterpriseChat*'')) { ''0'' } else { ''1'' } | ' +
    'Set-Content -NoNewline ''' + OutFile + '''"';
  Exec('powershell.exe', Cmd, '', SW_HIDE, ewWaitUntilTerminated, Code);

  Result := False;
  if FileExists(OutFile) then begin
    if LoadStringFromFile(OutFile, Buf) then
      Result := Trim(string(Buf)) = '1';
    DeleteFile(OutFile);
  end;
end;

// ----- Hooks Inno Setup ---------------------------------------------------

function InitializeSetup(): Boolean;
var
  Response: Integer;
begin
  Result := True;
  if PortInUseByForeignProcess(5080) then begin
    Response := MsgBox(
      'El puerto 5080 ya esta ocupado por otro proceso en este equipo.' + #13#10 + #13#10 +
      'El servicio EnterpriseChat no podra arrancar mientras ese otro proceso siga vivo (Windows Service Control Manager fallara con "Address already in use" y dejara el servicio en estado "Stopped").' + #13#10 + #13#10 +
      'Sugerencia: cierra el proceso que esta usando el puerto 5080 antes de continuar (por ejemplo un servidor de desarrollo abierto con dotnet run).' + #13#10 + #13#10 +
      '¿Quieres continuar de todas formas? (Podras arrancar el servicio mas tarde, cuando liberes el puerto, desde el Menu Inicio > EnterpriseChat > Arrancar servicio.)',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    if Response = IDNO then Result := False;
  end;
end;

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
