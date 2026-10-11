// Fase 3 - App movil (React Native + Expo) que consume la API de la tienda con autenticacion Cognito.
// Login con Amazon Cognito (SRP) + el access token (JWT) se manda como "Bearer" a la API /v1.
// El grupo construye encima su propia integracion (manejo de tokens, refresh, pantallas).
// Polyfill de numeros aleatorios seguros: amazon-cognito-identity-js lo necesita en React Native/Expo (va primero).
import "react-native-get-random-values";
import React, { useEffect, useState } from "react";
import { SafeAreaView, View, Text, TextInput, Button, FlatList, StyleSheet } from "react-native";
import {
  CognitoUserPool, CognitoUser, AuthenticationDetails,
} from "amazon-cognito-identity-js";
import { API_URL, COGNITO } from "./config";

// Lo que devuelve GET /v1/products (ProductsApiController de la tienda).
type Producto = { id: number; name: string; description: string; price: number; stock: number; category: string };

const pool = new CognitoUserPool({ UserPoolId: COGNITO.UserPoolId, ClientId: COGNITO.ClientId });

export default function App() {
  const [token, setToken] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [productos, setProductos] = useState<Producto[]>([]);

  // Login con Cognito: devuelve el JWT que luego autoriza a la API.
  const login = () => {
    setError("");
    const user = new CognitoUser({ Username: email, Pool: pool });
    const details = new AuthenticationDetails({ Username: email, Password: password });
    user.authenticateUser(details, {
      onSuccess: (session) => setToken(session.getAccessToken().getJwtToken()),
      onFailure: (err) => setError(err.message || "No se pudo iniciar sesion"),
    });
  };

  // Catalogo desde la API gestionada (API_URL ya termina en /v1), con el token en Authorization.
  // La app maneja el corte sin cerrarse.
  useEffect(() => {
    if (!token) return;
    fetch(`${API_URL}/products`, { headers: { Authorization: `Bearer ${token}` } })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error("API " + r.status))))
      .then((data) => setProductos(Array.isArray(data) ? data : []))
      .catch((e) => setError("Sin conexion con la API: " + e.message)); // caos suave: no se cae
  }, [token]);

  if (!token) {
    return (
      <SafeAreaView style={s.c}>
        <Text style={s.h}>URPOnline — iniciar sesion</Text>
        <TextInput style={s.in} placeholder="correo" autoCapitalize="none" value={email} onChangeText={setEmail} />
        <TextInput style={s.in} placeholder="contrasena" secureTextEntry value={password} onChangeText={setPassword} />
        <Button title="Entrar" onPress={login} />
        {!!error && <Text style={s.err}>{error}</Text>}
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={s.c}>
      <Text style={s.h}>Catalogo</Text>
      {!!error && <Text style={s.err}>{error}</Text>}
      <FlatList
        data={productos}
        keyExtractor={(it) => String(it.id)}
        renderItem={({ item }) => (
          <View style={s.row}>
            <Text>{item.name}</Text>
            <Text>S/ {item.price}</Text>
          </View>
        )}
      />
    </SafeAreaView>
  );
}

const s = StyleSheet.create({
  c: { flex: 1, padding: 20, gap: 10 },
  h: { fontSize: 22, fontWeight: "bold" },
  in: { borderWidth: 1, borderColor: "#ccc", borderRadius: 6, padding: 10 },
  row: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 8, borderBottomWidth: 1, borderColor: "#eee" },
  err: { color: "#b42318" },
});
