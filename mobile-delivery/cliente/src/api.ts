// Inicio de sesion con Amazon Cognito (SRP: la contrasena no viaja) y llamadas a la API del delivery.
// El token vive solo en memoria. Renovarlo y manejarlo sin senal es parte del desafio C2.
import "react-native-get-random-values"; // numeros aleatorios seguros para SRP; debe cargarse antes que Cognito
import { AuthenticationDetails, CognitoUser, CognitoUserPool } from "amazon-cognito-identity-js";
import { API_KEY, API_URL, COGNITO } from "./config";

const pool = new CognitoUserPool({ UserPoolId: COGNITO.UserPoolId, ClientId: COGNITO.ClientId });

export function iniciarSesion(correo: string, clave: string): Promise<string> {
  return new Promise((ok, falla) => {
    const usuario = new CognitoUser({ Username: correo, Pool: pool });
    usuario.authenticateUser(new AuthenticationDetails({ Username: correo, Password: clave }), {
      onSuccess: (s) => ok(s.getIdToken().getJwtToken()), // el autorizador de Cognito de API Gateway valida el ID token
      onFailure: (e) => falla(new Error(e.message || "No se pudo iniciar sesión")),
    });
  });
}

export type Restaurante = { id: number; nombre: string; lat: number; lon: number };
export type Plato = { id: number; restauranteId: number; nombre: string; descripcion: string; precio: number;
  disponible: boolean; ingredientes: string[]; alergenos: string[]; coseno?: number };
export type Pedido = { id: number; estado: string; version: number; restauranteId: number; repartidorId: number | null;
  total: number; lat: number; lon: number; actualizadoEn: string };

export async function api<T>(token: string, ruta: string, metodo = "GET", cuerpo?: unknown): Promise<T> {
  const r = await fetch(`${API_URL}${ruta}`, {
    method: metodo,
    headers: { Authorization: token, "x-api-key": API_KEY, "Content-Type": "application/json" },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  });
  if (!r.ok) {
    const detalle = await r.json().catch(() => ({}));
    throw new Error(`${r.status}: ${detalle.title ?? detalle.message ?? "error de la API"}`);
  }
  return r.json() as Promise<T>;
}
