// Pestana Comida: restaurantes, su carta, busqueda de platos (la base busca por coseno; C5 agrega reescritura y
// Jaccard) y el pedido. El punto de entrega es la ubicacion del celular.
import React, { useEffect, useState } from "react";
import { Button, FlatList, Text, TextInput, TouchableOpacity, View } from "react-native";
import * as Location from "expo-location";
import { api, Pedido, Plato, Restaurante } from "./api";
import { guardarPedido } from "./db";
import { s } from "./estilos";

export function Comida({ token, alPedir }: { token: string; alPedir: (p: Pedido) => void }) {
  const [restaurantes, setRestaurantes] = useState<Restaurante[]>([]);
  const [elegido, setElegido] = useState<Restaurante | null>(null);
  const [platos, setPlatos] = useState<Plato[]>([]);
  const [carrito, setCarrito] = useState<Record<number, number>>({});
  const [consulta, setConsulta] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    api<Restaurante[]>(token, "/v1/restaurantes").then(setRestaurantes).catch((e) => setError(e.message));
  }, [token]);

  const abrir = (r: Restaurante) => {
    setElegido(r); setCarrito({}); setError("");
    api<Plato[]>(token, `/v1/restaurantes/${r.id}/platos`).then(setPlatos).catch((e) => setError(e.message));
  };

  const buscar = () => {
    if (!consulta.trim()) return;
    setElegido(null); setCarrito({}); setError("");
    api<Plato[]>(token, `/v1/platos/buscar?q=${encodeURIComponent(consulta)}`).then(setPlatos).catch((e) => setError(e.message));
  };

  const pedir = async () => {
    const items = Object.entries(carrito).map(([platoId, cantidad]) => ({ platoId: Number(platoId), cantidad }));
    const restauranteId = platos.find((p) => carrito[p.id])?.restauranteId;
    if (!items.length || !restauranteId) return;
    try {
      const permiso = await Location.requestForegroundPermissionsAsync();
      if (permiso.status !== "granted") throw new Error("Sin permiso de ubicación no se sabe dónde entregar.");
      const aqui = await Location.getCurrentPositionAsync({});
      const pedido = await api<Pedido>(token, "/v1/pedidos", "POST",
        { restauranteId, items, lat: aqui.coords.latitude, lon: aqui.coords.longitude });
      await guardarPedido(pedido);
      setCarrito({});
      alPedir(pedido);
    } catch (e) { setError((e as Error).message); }
  };

  const total = platos.reduce((t, p) => t + (carrito[p.id] ?? 0) * p.precio, 0);
  return (
    <View style={s.col}>
      <View style={s.fila}>
        <TextInput style={[s.in, { flex: 1 }]} placeholder="Busca: «algo picante con pollo»" value={consulta}
          onChangeText={setConsulta} onSubmitEditing={buscar} />
        <Button title="Buscar" onPress={buscar} />
      </View>
      <FlatList horizontal data={restaurantes} keyExtractor={(r) => String(r.id)} style={{ flexGrow: 0 }}
        renderItem={({ item }) => (
          <TouchableOpacity onPress={() => abrir(item)} style={[s.chip, elegido?.id === item.id && s.chipActivo]}>
            <Text>{item.nombre}</Text>
          </TouchableOpacity>)} />
      {!!error && <Text style={s.err}>{error}</Text>}
      <FlatList data={platos} keyExtractor={(p) => String(p.id)} renderItem={({ item }) => (
        <View style={s.tarjeta}>
          <Text style={s.b}>{item.nombre} · S/ {item.precio.toFixed(2)}</Text>
          <Text>{item.descripcion}</Text>
          {item.alergenos.length > 0 && <Text style={s.aviso}>Alérgenos: {item.alergenos.join(", ")}</Text>}
          {item.coseno !== undefined && <Text style={s.gris}>coseno {item.coseno.toFixed(3)}</Text>}
          <View style={s.fila}>
            <Button title="−" onPress={() => setCarrito({ ...carrito, [item.id]: Math.max(0, (carrito[item.id] ?? 0) - 1) })} />
            <Text style={s.b}>{carrito[item.id] ?? 0}</Text>
            <Button title="+" onPress={() => setCarrito({ ...carrito, [item.id]: Math.min(20, (carrito[item.id] ?? 0) + 1) })} />
          </View>
        </View>)} />
      {total > 0 && <Button title={`Pedir · S/ ${total.toFixed(2)}`} onPress={pedir} />}
    </View>
  );
}
