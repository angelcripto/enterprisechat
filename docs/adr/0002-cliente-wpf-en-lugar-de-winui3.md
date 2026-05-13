# ADR 0002 — Cliente de escritorio en WPF (no WinUI 3)

- Fecha: 2026-05-13
- Estado: Aceptada

## Contexto

El cliente Windows debe ser una aplicación de escritorio con look moderno
(Fluent / Mica / dark mode), instalable sin Microsoft Store en máquinas
Windows 10 / 11 de pymes. .NET 8 ofrece tres caminos razonables:

- **WPF** (2006): framework maduro, distribución unpackaged trivial,
  XAML + MVVM bien establecido, tooling estable.
- **WinUI 3** (parte del Windows App SDK): apariencia Fluent oficial,
  controles modernos, ciclo de vida UWP-like.
- **Avalonia / Uno / MAUI**: alternativas multiplataforma. No son requisito
  ahora; cliente solo Windows.

## Decisión

Se elige **WPF**.

## Justificación

1. **Madurez y estabilidad**: WPF lleva 20 años en producción, su API
   apenas cambia. WinUI 3 ha tenido roadmap discontinuo (WinUI 3 Desktop
   estabilizado a finales de 2022 con WindowsAppSDK 1.2; controles y
   integraciones aún no llegan a la paridad con WPF/UWP en LOB scenarios:
   `DataGrid`, `TreeView` virtualizado, controles de timeline, etc.).
2. **Distribución a pymes sin Store**: WPF se publica como `.exe` self-
   contained o framework-dependent y se instala con MSI o Inno Setup sin
   fricción. WinUI 3 unpackaged es factible pero implica WindowsAppSDK
   runtime, MSIX como camino preferido y configuraciones de `app.manifest`
   más quisquillosas que clientes admin de pyme rechazan en la práctica.
3. **Apariencia Fluent moderna alcanzable**: la librería `WPF-UI` (Lepoco)
   aporta `FluentWindow`, controles Mica, tema dark/light automático y
   acceso a APIs del shell Windows. El resultado visual es indistinguible
   de WinUI para los flujos de un chat empresarial.
4. **Tooling y comunidad**: cualquier desarrollador .NET con experiencia
   tiene WPF cerca. WinUI 3 sigue siendo nicho. Mantenibilidad a 5+ años
   más previsible con WPF.
5. **MVVM**: usamos `CommunityToolkit.Mvvm` (source generators) con WPF
   exactamente igual que con WinUI; la inversión MVVM no se pierde si
   alguna vez migramos.

## Consecuencias

Positivas:

- Distribución y soporte en parque heterogéneo Win10/11 sin sobresaltos.
- Sin dependencia del ciclo de releases del WindowsAppSDK.
- Comunidad y stackoverflow saturados de respuestas WPF.

Negativas:

- No tendremos las últimas novedades de Windows 11 (TabView nativo, etc.)
  sin esfuerzo manual o controles de `WPF-UI`.
- Migración futura a multiplataforma (Avalonia) implicaría reescribir vistas.

## Alternativas descartadas

- **WinUI 3**: rechazada por inmadurez relativa y fricción de distribución
  unpackaged. Reconsiderar en 2-3 años si su soporte LOB mejora.
- **Avalonia**: excelente para multiplataforma pero introduce dependencia
  externa relativamente joven y nuestro requisito es solo Windows.
- **MAUI desktop**: orientada a apps híbridas móvil+desktop, no es el caso.
