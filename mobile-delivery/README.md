# 🛵 URPOnline Delivery — apps móviles (base del curso)

Dos apps **React Native con Expo (SDK 58)** y base de datos local **SQLite**:

| App | Carpeta | Qué hace la base |
|---|---|---|
| **Cliente** | `cliente/` | Inicia sesión (Cognito), ve restaurantes y su carta, busca platos, pide (con su ubicación), sigue la moto en el mapa por WebSocket y escribe al soporte. Pestañas: Tienda · Comida · Mis pedidos · Soporte |
| **Repartidor** | `repartidor/` | Inicia sesión (Cognito), envía su GPS cada 5 segundos, ve las ofertas cercanas, acepta y marca recogido / entregado |

La base hace **solo el camino feliz**. Lo que piden los desafíos lo construye el grupo: la cola de cambios sin señal (B1), la idempotencia (B3), la sincronización incremental (B4), la moto que nunca salta hacia atrás (D1/D3), la renovación del token (C2)…

## 0. Desplegar la base del delivery en AWS

1. La tienda desplegada con el código de esta rama (workflow **Desplegar en AWS**): trae la pestaña Comida y las tablas nuevas.
2. La pila de agentes `URPOnlineLab-agentes` desplegada (README, «Desplegar y probar la fase 2»): de ella sale el bus de EventBridge.
3. En el repositorio del grupo: **Settings → Secrets and variables → Actions → Variables → `ALERT_EMAIL`** = el correo que recibe las alarmas.
4. **Actions → Desplegar el delivery (fase 2) → Run workflow**. Al terminar, el resumen del workflow muestra los Outputs (ApiUrl, WebSocketUrl, UserPoolId…).
5. Confirma la suscripción del correo de alarmas que llega de AWS.
6. **Al terminar de practicar:** Actions → **Destruir AWS** (borra primero el delivery y después la base). La pila cobra por hora.

## 1. Requisitos

- **Node.js 20 LTS** o más nuevo (`node --version`).
- En el celular: **Expo Go** (Play Store o App Store), con la versión de SDK 58.
- La pila `infra/fase2-delivery.yaml` desplegada (sus Outputs van en `src/config.ts`).

## 2. Configurar

En cada app, edita `src/config.ts` con los Outputs de la pila:

```bash
out() { aws cloudformation describe-stacks --stack-name URPOnlineLab-delivery --query "Stacks[0].Outputs[?OutputKey=='$1'].OutputValue" --output text; }
out ApiUrl; out WebSocketUrl; out UserPoolId; out UserPoolClientId
aws apigateway get-api-key --api-key $(out ApiKeyId) --include-value --query value --output text
```

`src/config.ts` **no lleva secretos**: una app instalada se puede inspeccionar. La clave de API solo identifica a la app frente a su plan de uso; al usuario lo autentica Cognito.

## 3. Usuarios de prueba (Cognito)

```bash
POOL=$(out UserPoolId)
crear() { # crear correo contraseña grupo
  aws cognito-idp admin-create-user --user-pool-id $POOL --username $1 \
    --user-attributes Name=email,Value=$1 Name=email_verified,Value=true --message-action SUPPRESS
  aws cognito-idp admin-set-user-password --user-pool-id $POOL --username $1 --password "$2" --permanent
  aws cognito-idp admin-add-user-to-group --user-pool-id $POOL --username $1 --group-name $3
}
crear cliente1@ejemplo.com 'Cliente2026Segura' cliente
crear moto1@ejemplo.com 'Moto2026Segura' repartidor
crear moto2@ejemplo.com 'Moto2026Segura' repartidor
```

## 4. Correr la app en tu celular

```bash
cd mobile-delivery/cliente      # o repartidor
npm install
npx expo start                  # escanea el QR con Expo Go (Android) o con la cámara (iPhone)
```

Celular y computadora en la **misma red**. Si la red de la universidad no lo permite: `npx expo start --tunnel`.

## 5. Instalarla de verdad (sin costo)

Las opciones gratuitas están en [`FASE2_ESPECIFICACION.md` §9.8](../FASE2_ESPECIFICACION.md): EAS Build con distribución interna, Firebase App Distribution, Samsung Galaxy Store, Huawei AppGallery, GitHub Releases o la prueba gratuita de AWS Device Farm. En la presentación, **al menos una instalación se hace en vivo**.

## Dónde está cada cosa

| Parte | Cliente | Repartidor |
|---|---|---|
| Inicio de sesión (Cognito SRP) y llamadas a la API | `src/api.ts` | `src/api.ts` |
| Base local SQLite | `src/db.ts` | `src/db.ts` |
| Pantallas | `App.tsx`, `src/Comida.tsx`, `src/MisPedidos.tsx` | `App.tsx` |
| Configuración (Outputs de la pila) | `src/config.ts` | `src/config.ts` |
