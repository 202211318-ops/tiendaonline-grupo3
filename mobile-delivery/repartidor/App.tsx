// App del REPARTIDOR de URPOnline Delivery (base del curso, FASE2_ESPECIFICACION.md §9.9.5).
// Inicio de sesion con Amazon Cognito (grupo «repartidor»), envio del GPS cada 5 segundos a POST /v1/posiciones
// (la API lo deja en la cola FIFO posiciones), ofertas, aceptar y marcar recogido / entregado (POST /v1/sincronizar).
// Solo el CAMINO FELIZ: sin senal los cambios se pierden (la cola local es B1) y el GPS no se reintenta.
import React, { useEffect, useRef, useState } from "react";
import { Button, FlatList, Text, TextInput, View } from "react-native";
import * as Location from "expo-location";
import { api, iniciarSesion, Oferta, Pedido } from "./src/api";
import { cambiarEstadoLocal, enCurso, guardarEnCurso, prepararBase, terminar } from "./src/db";
import { s } from "./src/estilos";

type EnCurso = { id: number; estado: string; total: number; lat: number; lon: number };

export default function App() {
  const [token, setToken] = useState<string | null>(null);
  const [correo, setCorreo] = useState("");
  const [clave, setClave] = useState("");
  const [error, setError] = useState("");
  const [ofertas, setOfertas] = useState<Oferta[]>([]);
  const [pedido, setPedido] = useState<EnCurso | null>(null);
  const [gps, setGps] = useState("GPS apagado");
  const secuencia = useRef(0);
  const pedidoRef = useRef<EnCurso | null>(null);
  pedidoRef.current = pedido;

  useEffect(() => { prepararBase().then(enCurso).then(setPedido).catch((e) => setError(e.message)); }, []);

  // GPS real: cada 5 segundos (o 10 metros) la posicion va a la API, con un numero de secuencia que solo crece.
  useEffect(() => {
    if (!token) return;
    let sub: Location.LocationSubscription | undefined;
    (async () => {
      const permiso = await Location.requestForegroundPermissionsAsync();
      if (permiso.status !== "granted") { setGps("Sin permiso de ubicación: no recibirás pedidos."); return; }
      sub = await Location.watchPositionAsync({ accuracy: Location.Accuracy.High, timeInterval: 5000, distanceInterval: 10 }, (p) => {
        secuencia.current += 1;
        const cuerpo = { pedidoId: pedidoRef.current?.id ?? 0, lat: p.coords.latitude, lon: p.coords.longitude,
          secuencia: secuencia.current, horaDispositivo: new Date(p.timestamp).toISOString() };
        api(token, "/v1/posiciones", "POST", cuerpo)
          .then(() => setGps(`GPS enviado #${secuencia.current} (${p.coords.latitude.toFixed(5)}, ${p.coords.longitude.toFixed(5)})`))
          .catch((e) => setGps(`GPS no enviado: ${e.message}`));
      });
    })();
    return () => sub?.remove();
  }, [token]);

  // Ofertas cercanas, cada 5 segundos, mientras no haya un pedido en curso.
  useEffect(() => {
    if (!token || pedido) return;
    const leer = () => api<Oferta[]>(token, "/v1/ofertas").then(setOfertas).catch((e) => setError(e.message));
    leer();
    const t = setInterval(leer, 5000);
    return () => clearInterval(t);
  }, [token, pedido]);

  const aceptar = async (pedidoId: number) => {
    setError("");
    try {
      const p = await api<Pedido>(token!, `/v1/pedidos/${pedidoId}/aceptar`, "POST");
      await guardarEnCurso(p);
      setPedido(await enCurso());
    } catch (e) { setError((e as Error).message); }
  };

  const informar = async (estado: "recogido" | "entregado") => {
    if (!pedido) return;
    setError("");
    try {
      await api(token!, "/v1/sincronizar", "POST", { cambios: [{ pedidoId: pedido.id, estado, horaDispositivo: new Date().toISOString() }] });
      if (estado === "entregado") { await terminar(); setPedido(null); return; }
      await cambiarEstadoLocal(pedido.id, estado);
      setPedido(await enCurso());
    } catch (e) { setError("No se pudo enviar (¿sin señal?): " + (e as Error).message); }
  };

  if (!token) {
    return (
      <View style={[s.pantalla, s.col]}>
        <Text style={s.h1}>🛵 URPOnline Repartidor</Text>
        <TextInput style={s.in} placeholder="correo" autoCapitalize="none" value={correo} onChangeText={setCorreo} />
        <TextInput style={s.in} placeholder="contraseña" secureTextEntry value={clave} onChangeText={setClave} />
        <Button title="Entrar" onPress={() => { setError(""); iniciarSesion(correo, clave).then(setToken).catch((e) => setError(e.message)); }} />
        {!!error && <Text style={s.err}>{error}</Text>}
      </View>
    );
  }

  return (
    <View style={[s.pantalla, s.col]}>
      <Text style={s.h1}>{pedido ? `Pedido ${pedido.id}` : "Ofertas cerca de ti"}</Text>
      <Text style={s.gris}>{gps}</Text>
      {!!error && <Text style={s.err}>{error}</Text>}
      {pedido ? (
        <View style={s.tarjeta}>
          <Text style={s.b}>Estado: {pedido.estado} · S/ {pedido.total.toFixed(2)}</Text>
          <Text>Entregar en: {pedido.lat.toFixed(5)}, {pedido.lon.toFixed(5)}</Text>
          <View style={s.fila}>
            {pedido.estado === "aceptado" && <Button title="Recogido" onPress={() => informar("recogido")} />}
            {pedido.estado === "recogido" && <Button title="Entregado" onPress={() => informar("entregado")} />}
          </View>
        </View>
      ) : (
        <FlatList data={ofertas} keyExtractor={(o) => String(o.pedidoId)}
          ListEmptyComponent={<Text>Sin ofertas por ahora. Mantén la app abierta con el GPS encendido.</Text>}
          renderItem={({ item }) => (
            <View style={s.tarjeta}>
              <Text style={s.b}>Pedido {item.pedidoId} · {item.restaurante}</Text>
              <Text>S/ {item.total.toFixed(2)} · oferta {item.intento}</Text>
              <Button title="Aceptar" onPress={() => aceptar(item.pedidoId)} />
            </View>)} />
      )}
    </View>
  );
}
