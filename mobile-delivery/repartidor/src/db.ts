// Base local del celular del repartidor (SQLite). La base del curso guarda solo lo que la app muestra: el pedido
// en curso. Guardar los cambios hechos SIN senal y enviarlos al volver la red (cola local) es el desafio B1.
import * as SQLite from "expo-sqlite";
import type { Pedido } from "./api";

const db = SQLite.openDatabaseSync("repartidor.db");

export async function prepararBase() {
  await db.execAsync(
    "CREATE TABLE IF NOT EXISTS pedido_en_curso (id INTEGER PRIMARY KEY, estado TEXT NOT NULL, total REAL NOT NULL, lat REAL NOT NULL, lon REAL NOT NULL);");
}

export async function guardarEnCurso(p: Pedido) {
  await db.runAsync("DELETE FROM pedido_en_curso");
  await db.runAsync("INSERT INTO pedido_en_curso (id, estado, total, lat, lon) VALUES (?, ?, ?, ?, ?)", p.id, p.estado, p.total, p.lat, p.lon);
}

export async function cambiarEstadoLocal(id: number, estado: string) {
  await db.runAsync("UPDATE pedido_en_curso SET estado = ? WHERE id = ?", estado, id);
}

export const enCurso = () =>
  db.getFirstAsync<{ id: number; estado: string; total: number; lat: number; lon: number }>("SELECT id, estado, total, lat, lon FROM pedido_en_curso");

export async function terminar() { await db.runAsync("DELETE FROM pedido_en_curso"); }
