// Configuracion de la app del CLIENTE. NO van secretos aqui (una app instalada se puede inspeccionar):
// solo URLs y los IDs publicos de Cognito, todos Outputs de la pila infra/fase2-delivery.yaml.
// La clave de API identifica a la app frente a su plan de uso; quien autentica al usuario es Cognito.
export const API_URL = "https://TU-API.execute-api.us-east-2.amazonaws.com/prod"; // Output ApiUrl
export const WS_URL = "wss://TU-WS.execute-api.us-east-2.amazonaws.com/prod";    // Output WebSocketUrl
export const API_KEY = "VALOR-DE-LA-CLAVE";      // aws apigateway get-api-key --api-key <ApiKeyId> --include-value
export const TIENDA_URL = "https://TU-TIENDA.cloudfront.net"; // Output ApplicationHTTPS de la pila base
export const COGNITO = {
  UserPoolId: "us-east-2_XXXXXXXXX",            // Output UserPoolId
  ClientId: "XXXXXXXXXXXXXXXXXXXXXXXXXX",       // Output UserPoolClientId (app publica, sin secreto)
};
