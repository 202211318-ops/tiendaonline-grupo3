# Fase 3 — App móvil (React Native + Expo)

Consume la API de la tienda (`/v1`) con autenticación **Amazon Cognito**. **No lleva secretos** (una app
instalada se puede inspeccionar): solo la URL de la API y los IDs públicos de Cognito.

```bash
cd mobile
npm install
# Edita config.ts con los Outputs de infra/fase3-contenedores.yaml (ApiUrl, UserPoolId, UserPoolClientId)
npm start           # abre Expo; escanea el QR con Expo Go o usa un emulador
```

El grupo construye encima su propia integración: manejo del token y refresh, más pantallas (carrito,
pedido, chat con el agente de soporte). El login (Cognito SRP) está en `App.tsx`.
