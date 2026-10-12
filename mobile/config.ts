// Fase 3 - Configuracion de la app movil. NO van secretos aqui (una app instalada se puede inspeccionar).
// Solo la URL de la API gestionada (/v1), la clave de API y los IDs publicos de Cognito (Outputs de fase3-contenedores.yaml).
// La clave de API NO es un secreto de autenticacion: identifica a la app frente al plan de uso (limites y cuota).
// Quien autentica al usuario es Cognito (el token).
export const API_URL = "https://TU-API.execute-api.us-east-2.amazonaws.com/prod/v1"; // Output ApiUrl (ya incluye /v1)
export const API_KEY = "VALOR-DE-LA-CLAVE"; // aws apigateway get-api-key --api-key <ApiKeyId> --include-value
export const COGNITO = {
  UserPoolId: "us-east-2_XXXXXXXXX",   // Output UserPoolId
  ClientId: "XXXXXXXXXXXXXXXXXXXXXXXXXX", // Output UserPoolClientId (app publica, sin secreto)
};
