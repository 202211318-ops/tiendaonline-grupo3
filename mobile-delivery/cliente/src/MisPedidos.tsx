// Mis pedidos: estado de cada pedido y el mapa con la moto. La posicion llega por el WebSocket del delivery
// (la Lambda RegistrarPosicion la empuja); si no hay senal, se muestra la ultima posicion guardada en SQLite.
// Que la moto nunca «salte hacia atras» con posiciones desordenadas es el desafio D1/D3.
import React, { useCallback, useEffect, useRef, useState } from "react";
import { Button, FlatList, Text, TouchableOpacity, View } from "react-native";
import MapView, { Marker } from "react-native-maps";
import { api, Pedido } from "./api";
import { WS_URL } from "./config";
import { guardarPedido, guardarPosicion, misPedidos, ultimaPosicion } from "./db";
import { s } from "./estilos";

type Moto = { lat: number; lon: number; secuencia: number };

export function MisPedidos({ token, inicial }: { token: string; inicial?: number }) {
  const [lista, setLista] = useState<{ id: number; estado: string; total: number }[]>([]);
  const [pedido, setPedido] = useState<Pedido | null>(null);
  const [moto, setMoto] = useState<Moto | null>(null);
  const [error, setError] = useState("");
  const ws = useRef<WebSocket | null>(null);

  const recargar = useCallback(() => { misPedidos().then(setLista); }, []);
  useEffect(recargar, [recargar]);

  const seguir = useCallback(async (id: number) => {
    setError("");
    setMoto(await ultimaPosicion(id));               // lo último que se supo, aunque no haya señal
    try {
      const p = await api<Pedido>(token, `/v1/pedidos/${id}`);
      setPedido(p); await guardarPedido(p); recargar();
    } catch (e) { setError("Sin conexión: se muestra lo último guardado. " + (e as Error).message); }
    ws.current?.close();
    const socket = new WebSocket(`${WS_URL}?token=${encodeURIComponent(token)}`);
    socket.onopen = () => socket.send(JSON.stringify({ action: "seguir", pedidoId: id }));
    socket.onmessage = async (m) => {
      const d = JSON.parse(String(m.data));
      if (d.tipo !== "posicion" || d.pedidoId !== id) return;
      setMoto({ lat: d.lat, lon: d.lon, secuencia: d.secuencia });
      await guardarPosicion(id, d.lat, d.lon, d.secuencia);
    };
    socket.onerror = () => setError("Se cortó la conexión en vivo.");
    ws.current = socket;
  }, [token, recargar]);

  useEffect(() => { if (inicial) seguir(inicial); return () => ws.current?.close(); }, [inicial, seguir]);

  if (pedido) {
    const region = { latitude: moto?.lat ?? pedido.lat, longitude: moto?.lon ?? pedido.lon, latitudeDelta: 0.02, longitudeDelta: 0.02 };
    return (
      <View style={s.col}>
        <Text style={s.h2}>Pedido {pedido.id} · {pedido.estado}</Text>
        {!!error && <Text style={s.err}>{error}</Text>}
        <MapView style={{ flex: 1, borderRadius: 12 }} region={region}>
          <Marker coordinate={{ latitude: pedido.lat, longitude: pedido.lon }} title="Tu entrega" />
          {moto && <Marker coordinate={{ latitude: moto.lat, longitude: moto.lon }} title="Tu repartidor"><Text style={{ fontSize: 30 }}>🛵</Text></Marker>}
        </MapView>
        <View style={s.fila}>
          <Button title="Actualizar" onPress={() => seguir(pedido.id)} />
          <Button title="Volver" onPress={() => { ws.current?.close(); setPedido(null); recargar(); }} />
        </View>
      </View>
    );
  }
  return (
    <View style={s.col}>
      {!!error && <Text style={s.err}>{error}</Text>}
      {lista.length === 0 && <Text>Todavía no tienes pedidos.</Text>}
      <FlatList data={lista} keyExtractor={(p) => String(p.id)} renderItem={({ item }) => (
        <TouchableOpacity style={s.tarjeta} onPress={() => seguir(item.id)}>
          <Text style={s.b}>Pedido {item.id} · {item.estado}</Text>
          <Text>S/ {item.total.toFixed(2)} · toca para ver la moto en el mapa</Text>
        </TouchableOpacity>)} />
    </View>
  );
}
