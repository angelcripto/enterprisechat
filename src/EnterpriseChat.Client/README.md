# EnterpriseChat.Client

Cliente de escritorio Windows para EnterpriseChat. WPF + `WPF-UI` (Fluent),
`CommunityToolkit.Mvvm` para MVVM, `Microsoft.AspNetCore.SignalR.Client`
para el transporte.

## Estado

Fase 0: la ventana por defecto generada por `dotnet new wpf` todavía es
la placeholder. Login y main window llegan en Fase 2.

## Ejecutar en desarrollo

```powershell
dotnet run --project src/EnterpriseChat.Client
```

Asegúrate de tener el servidor escuchando antes de conectar. Por defecto
intenta `http://localhost:5080`.

## DPI y manifest

`app.manifest` declara `PerMonitorV2` para que la UI se vea nítida en
monitores 4K y soporta rutas largas en Windows 10/11.
