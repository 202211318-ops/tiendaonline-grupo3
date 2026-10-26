// App del CLIENTE de URPOnline Delivery (base del curso, FASE2_ESPECIFICACION.md §9.9.5).
// Pestanas: Tienda · Comida · Mis pedidos · Soporte. Inicio de sesion con Amazon Cognito (grupo «cliente»).
import React, { useEffect, useState } from "react";
import { Button, Linking, Text, TextInput, TouchableOpacity, View } from "react-native";
import { api, iniciarSesion, Pedido } from "./src/api";
import { Comida } from "./src/Comida";
import { MisPedidos } from "./src/MisPedidos";
import { TIENDA_URL } from "./src/config";
import { prepararBase } from "./src/db";
import { s } from "./src/estilos";

type Pestana = "Tienda" | "Comida" | "Mis pedidos" | "Soporte";

export default function App() {
  const [token, setToken] = useState<string | null>(null);
  const [correo, setCorreo] = useState("");
  const [clave, setClave] = useState("");
  const [error, setError] = useState("");
  const [pestana, setPestana] = useState<Pestana>("Comida");
  const [ultimo, setUltimo] = useState<number | undefined>();

  useEffect(() => { prepararBase().catch((e) => setError(e.message)); }, []);

  if (!token) {
    return (
      <View style={[s.pantalla, s.col]}>
        <Text style={s.h1}>🛵 URPOnline Delivery</Text>
        <TextInput style={s.in} placeholder="correo" autoCapitalize="none" value={correo} onChangeText={setCorreo} />
        <TextInput style={s.in} placeholder="contraseña" secureTextEntry value={clave} onChangeText={setClave} />
        <Button title="Entrar" onPress={() => { setError(""); iniciarSesion(correo, clave).then(setToken).catch((e) => setError(e.message)); }} />
        {!!error && <Text style={s.err}>{error}</Text>}
      </View>
    );
  }

  return (
    <View style={s.pantalla}>
      <Text style={s.h1}>{pestana}</Text>
      <View style={s.col}>
        {pestana === "Tienda" && <Button title="Abrir la tienda URPOnline" onPress={() => Linking.openURL(TIENDA_URL)} />}
        {pestana === "Comida" && <Comida token={token} alPedir={(p: Pedido) => { setUltimo(p.id); setPestana("Mis pedidos"); }} />}
        {pestana === "Mis pedidos" && <MisPedidos token={token} inicial={ultimo} />}
        {pestana === "Soporte" && <Soporte token={token} pedidoId={ultimo} />}
      </View>
      <View style={s.pestanas}>
        {(["Tienda", "Comida", "Mis pedidos", "Soporte"] as Pestana[]).map((p) => (
          <TouchableOpacity key={p} onPress={() => setPestana(p)}>
            <Text style={[s.pestana, pestana === p && s.pestanaActiva]}>{p}</Text>
          </TouchableOpacity>))}
      </View>
    </View>
  );
}

// Soporte del delivery: la base responde con la politica (RAG). El enrutador y la traza del agente son el desafio D4.
function Soporte({ token, pedidoId }: { token: string; pedidoId?: number }) {
  const [texto, setTexto] = useState("");
  const [respuesta, setRespuesta] = useState("");
  const enviar = () => api<{ respuesta: string; seccion: string | null }>(token, "/v1/soporte", "POST", { pedidoId: pedidoId ?? 0, texto })
    .then((r) => setRespuesta(r.seccion ? `${r.respuesta}\n\n(sección: ${r.seccion})` : r.respuesta))
    .catch((e) => setRespuesta(e.message));
  return (
    <View style={s.col}>
      <TextInput style={[s.in, { minHeight: 80 }]} multiline placeholder="Escribe tu consulta: «llegó frío, ¿me reembolsan?»"
        value={texto} onChangeText={setTexto} />
      <Button title="Enviar" onPress={enviar} disabled={!texto.trim()} />
      {!!respuesta && <Text style={s.tarjeta}>{respuesta}</Text>}
    </View>
  );
}
