// Fase 3 - Configuracion de la app movil. NO van secretos aqui (una app instalada se puede inspeccionar).
// Solo la URL de la API gestionada (/v1) y los IDs publicos de Cognito (Outputs de fase3-contenedores.yaml).
export const API_URL = "https://TU-API.execute-api.us-east-2.amazonaws.com/v1"; // Output ApiUrl (ya incluye /v1)
export const COGNITO = {
  UserPoolId: "us-east-2_XXXXXXXXX",   // Output UserPoolId
  ClientId: "XXXXXXXXXXXXXXXXXXXXXXXXXX", // Output UserPoolClientId (app publica, sin secreto)
};
