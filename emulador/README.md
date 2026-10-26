# Emulador de intrusión (INOFENSIVO) — Fase 2, Categoría 3

Herramienta de laboratorio para **defensa proactiva**. Cada grupo la ejecuta **contra su propia
infraestructura** para comprobar que su monitoreo **detecta** una intrusión. **No ataca a terceros,
no explota nada, no exfiltra datos.** Solo genera las señales que una defensa bien montada debe cazar,
y las imprime en claro (no es sigiloso: la idea es que se vea).

## Qué hace (todo inofensivo)

1. **Consume CPU** unos 5 segundos (simula el minero) → pico de CPU en CloudWatch.
2. **Intenta conectar** a `203.0.113.10:3333` y al NAT → intento de salida en VPC Flow Logs.
   `203.0.113.0/24` es una red **reservada para documentación** (RFC 5737): no enruta a ningún sitio real.
3. **Lee los NOMBRES** de variables de entorno de credenciales (no sus valores, y no las envía).
4. Deja un **archivo marcador** ("control tomado"), sin romper nada.

**Variante B (Mythos):** además simula una **anomalía de certificado/TLS** (indicador de "descifrado
cuántico"). **No rompe ningún cifrado** — eso es solo teoría para la sustentación: *cosechar ahora,
descifrar después*; RSA/ECC serían el blanco teórico, AES-256 y SHA-256 resisten.

## Uso

```bash
# Variante A (troyano/minero), por defecto:
dotnet run --project emulador -- A

# Variante B (Mythos):
dotnet run --project emulador -- B
```

> Ejecútalo **solo en tu propio entorno** (tu EC2/laboratorio). Después, revisa tus **alarmas de
> CloudWatch**, **VPC Flow Logs**, **CloudTrail** y **Reachability Analyzer**: tu defensa proactiva
> debería haberlo detectado, alertado y, si la configuraste, contenido automáticamente.

## Verificador de datos personales

El verificador automático de datos personales en los logs está en
`scripts/verificadores/verificar_datos_personales.py` (CUMPLE / NO CUMPLE sobre los logs de CloudWatch).
