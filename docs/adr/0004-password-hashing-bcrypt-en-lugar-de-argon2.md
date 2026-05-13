# ADR 0004 — Hashing de contraseñas con BCrypt (no Argon2id)

- Fecha: 2026-05-13
- Estado: Aceptada

## Contexto

El servidor debe almacenar credenciales de usuarios con hashing
adversarial-resistente. El threat model relevante es **DB exfiltrada** /
backup robado: un atacante obtiene la tabla `Users` y quiere recuperar
contraseñas en claro mediante fuerza bruta / dictionary attacks.

Algoritmos vigentes y candidatos:

- **Argon2id** (PHC winner 2015): estado del arte. Memory-hard, resistente
  a GPU/ASIC. Recomendado por OWASP como primera elección.
- **BCrypt** (Niels Provos, 1999): work-factor configurable, sin
  memory-hardness pero ampliamente desplegado y auditado.
- **PBKDF2** / **SHA*-only**: insuficientes hoy; descartados.
- **scrypt**: válido, menos común en ecosistema .NET.

## Decisión

Se elige **BCrypt.Net-Next** con factor de coste 12 como punto de partida.

## Justificación

1. **Estado del ecosistema .NET**: Argon2 no tiene implementación oficial
   de Microsoft. La opción más usada es
   `Konscious.Security.Cryptography.Argon2`, con mantenimiento irregular
   y menos auditoría externa que las bibliotecas BCrypt para .NET. En un
   producto que se va a desplegar on-premise en pymes, prefiero una
   biblioteca con muchos ojos encima.
2. **BCrypt sigue siendo suficiente para el threat model**: con factor
   de coste 12, ~250 ms por verificación en hardware moderno; un ataque
   GPU optimizado tarda años contra contraseñas no triviales. La ventaja
   memory-hard de Argon2 importa más cuando el atacante tiene tiempo y
   recursos prácticamente ilimitados (servicios masivos consumer); no
   es el caso aquí.
3. **Future-proof por diseño**: el server hashea via interfaz
   `IPasswordHasher`. El formato BCrypt incluye prefijo (`$2a$12$…`) que
   permite detección automática; cuando consideremos migrar (Argon2id o
   factor de coste mayor) re-hasheamos en el siguiente login exitoso sin
   romper sesiones existentes.

## Consecuencias

Positivas:

- Sólo dependemos de una librería ampliamente auditada y mantenida.
- Verificación rápida pero costosa para el atacante (cost factor 12).
- Posibilidad clara de migración futura sin migración masiva.

Negativas:

- BCrypt no es memory-hard; si en años futuros aparece hardware ASIC
  optimizado contra BCrypt a escala, habrá que migrar.
- Argon2id daría algo más de margen frente a ataques masivos.

## Plan de revisión

- Revisar factor de coste cada 18 meses contra hardware contemporáneo.
- Reevaluar Argon2id si la implementación .NET madura o si llega una
  recomendación oficial de Microsoft / OWASP que cambie el equilibrio.
