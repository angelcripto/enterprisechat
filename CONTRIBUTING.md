# Cómo contribuir a EnterpriseChat

Gracias por interesarte en EnterpriseChat. Este documento describe cómo
proponer cambios y qué condiciones aplican a las contribuciones.

## Antes de abrir un PR

1. Comprueba que tu cambio no rompe `dotnet build` ni `dotnet test`.
2. Si tocas algo que aparece en `docs/adr/`, abre una nueva ADR antes
   de cambiar la decisión.
3. Mantén el comentario y los identificadores en español cuando aporte
   valor; los términos técnicos (SignalR, hub, claim) van en inglés.
4. No introduzcas dependencias nuevas sin abrir una issue previa.

## Estilo

- C# moderno (file-scoped namespaces, `var` cuando el tipo es evidente,
  records inmutables para DTOs).
- `Nullable` está activado en todos los proyectos. No silencies advertencias
  con `!` salvo en boundary de framework documentado.
- `Directory.Packages.props` es la única fuente de verdad de versiones de
  paquetes; no añadas `Version=` en `<PackageReference>`.

## Acuerdo de licencia del contribuidor (CLA)

Al enviar un Pull Request a este repositorio aceptas que tu contribución
queda licenciada bajo los términos siguientes:

1. Concedes al titular del proyecto (Angel Martínez Programación) una
   licencia perpetua, mundial, no exclusiva, libre de regalías para usar,
   reproducir, modificar y distribuir tu contribución como parte de
   EnterpriseChat tanto bajo la **edición Free (AGPLv3)** como bajo la
   **edición comercial**.
2. Declaras que la contribución es de autoría propia y que tienes derecho
   a otorgar esta licencia.

Este CLA es lo que permite mantener el modelo de licenciamiento dual
descrito en [COMMERCIAL-LICENSING.md](COMMERCIAL-LICENSING.md). Sin él el
proyecto no podría seguir teniendo una edición comercial.

## Reportar bugs y vulnerabilidades

- Bugs no sensibles: abre una issue pública.
- Vulnerabilidades de seguridad: envía un correo a
  `amprogramacion@gmail.com` con detalles reproducibles. No abras issues
  públicas para problemas de seguridad.
