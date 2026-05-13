# ADR 0001 — Modelo open-core: monorepo público AGPLv3 + licensing como plugin runtime privado

- Fecha: 2026-05-13
- Estado: Aceptada

## Contexto

EnterpriseChat es un proyecto open source en español con modelo de negocio freemium:

- Edición **Free** (gratuita, AGPLv3): cap rígido de 10 usuarios concurrentes.
- Edición **Pro** (comercial): licencia firmada que desbloquea más usuarios y opciones empresariales.

El código de validación de licencias contiene IP relevante (algoritmo de
emisión, claves, formato del token) que el propietario no desea publicar.
Hay tres formas habituales de combinar AGPLv3 con un componente cerrado:

1. Todo en un repo privado, publicar releases binarios.
2. Un solo repo público que mezcla código AGPL y código cerrado.
3. Dos repos: público (AGPL) y privado (cerrado), unidos en build/distribución.

Opción 1 elimina el lado open source. Opción 2 mezcla en el mismo árbol
código bajo licencias incompatibles y arriesga contaminación de licencia.

## Decisión

Adoptamos el patrón **open core** clásico (GitLab CE/EE, Sentry, Sourcegraph,
HashiCorp BSL) con dos repos:

- `enterprisechat` (público, AGPLv3): Server + Client + Protocol +
  Licensing.Abstractions + `FreeLicenseValidator`. Quien clone y compile bajo
  AGPL obtiene un sistema funcional con cap de 10 usuarios.
- `enterprisechat-licensing` (privado, propietario): impl `LicenseValidator`
  contra JWT RS256 + CLI `LicenseGen`. Se distribuye únicamente como `.dll`
  junto al instalador comercial.

El acoplamiento entre los dos mundos se hace **a través de una interfaz**
(`ILicenseValidator` en `EnterpriseChat.Licensing.Abstractions`) y la
edición Pro se carga **en tiempo de ejecución** desde el directorio
`plugins/` mediante reflection (`AssemblyLoadContext.Default.LoadFromAssemblyPath`).

## Justificación AGPLv3 §13

AGPLv3 obliga a publicar el código fuente "Combined Work" cuando se
distribuye una obra derivada. El criterio relevante aquí es si el plugin
cerrado constituye una obra derivada o una "mera agregación" en el sentido
de la GPL FAQ:

> Mere aggregation of two programs means putting them side by side on the
> same CD-ROM or hard disk. We use this term in the case where they are
> separate programs, not parts of a single program.

La interpretación operativa que aplican GitLab, Sentry y otros proyectos
open-core con la misma estructura: si la edición Pro se comunica con el
servidor open source mediante una **interfaz pública estable**, vive como
ensamblado separado y se carga en runtime, se considera componente
independiente y no contamina la licencia.

Esto **no es** una opinión legal vinculante. Reduce el riesgo razonablemente,
pero un caso judicial podría discrepar. Como mitigación:

- El proyecto se publica bajo **licencia dual** (AGPLv3 + comercial). El
  titular de los derechos de autor (yo) puede relicenciar mi propio código
  bajo términos comerciales. Los compradores de la edición Pro reciben
  además una licencia comercial que regula la combinación con el plugin
  cerrado fuera del marco AGPL.

## Consecuencias

Positivas:

- Comunidad puede forkear, auditar, contribuir al producto sin restricción.
- Los términos copyleft de AGPL desincentivan que un competidor monte un
  SaaS sobre nuestro código sin contribuir mejoras.
- El componente comercial (licensing) permanece cerrado.
- Build y dependencias del repo público nunca tocan código privado;
  contributors externos no necesitan acceso al repo privado.

Negativas / a vigilar:

- Los cambios en `ILicenseValidator` son cambios de **API pública** entre
  ambos repos; cualquier breaking change obliga a sincronizar releases.
- El test de carga del plugin (`LicensingExtensions.TryLoadProPlugin`) vive
  en el repo público y debe ser robusto frente a `.dll` corruptos o de
  versión incompatible.
- Hay que mantener disciplina al revisar PRs externas para que no se
  introduzca código cuya intención sea formar parte del módulo Pro.
