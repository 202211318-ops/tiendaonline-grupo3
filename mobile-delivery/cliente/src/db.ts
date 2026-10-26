// Base local del celular (SQLite). La base del curso guarda solo lo que la app muestra: los pedidos del cliente
// y la ultima posicion conocida de la moto. La cola de cambios sin senal y la sincronizacion son desafios del bloque B.
import * as SQLite from "expo-sqlite";
import type { Pedido } from "./api";

const db = SQLite.openDatabaseSync("cliente.db");

export async function prepararBase() {
  await db.execAsync(`
    CREATE TABLE IF NOT EXISTS pedidos (id INTEGER PRIMARY KEY, estado TEXT NOT NULL, total REAL NOT NULL, actualizado_en TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS ultima_posicion (pedido_id INTEGER PRIMARY KEY, lat REAL NOT NULL, lon REAL NOT NULL, secuencia INTEGER NOT NULL);
  `);
}

export async function guardarPedido(p: Pedido) {
  await db.runAsync(
    "INSERT INTO pedidos (id, estado, total, actualizado_en) VALUES (?, ?, ?, ?) " +
      "ON CONFLICT(id) DO UPDATE SET estado = excluded.estado, actualizado_en = excluded.actualizado_en",
    p.id, p.estado, p.total, p.actualizadoEn);
}

export const misPedidos = () =>
  db.getAllAsync<{ id: number; estado: string; total: number }>("SELECT id, estado, total FROM pedidos ORDER BY id DESC");

export async function guardarPosicion(pedidoId: number, lat: number, lon: number, secuencia: number) {
  await db.runAsync(
    "INSERT INTO ultima_posicion (pedido_id, lat, lon, secuencia) VALUES (?, ?, ?, ?) " +
      "ON CONFLICT(pedido_id) DO UPDATE SET lat = excluded.lat, lon = excluded.lon, secuencia = excluded.secuencia",
    pedidoId, lat, lon, secuencia);
}

export const ultimaPosicion = (pedidoId: number) =>
  db.getFirstAsync<{ lat: number; lon: number; secuencia: number }>(
    "SELECT lat, lon, secuencia FROM ultima_posicion WHERE pedido_id = ?", pedidoId);
