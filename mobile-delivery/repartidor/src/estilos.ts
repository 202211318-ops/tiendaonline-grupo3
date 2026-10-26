import { StyleSheet } from "react-native";

export const s = StyleSheet.create({
  pantalla: { flex: 1, paddingTop: 48, paddingHorizontal: 16, backgroundColor: "#ffffff" },
  col: { flex: 1, gap: 10 },
  fila: { flexDirection: "row", alignItems: "center", gap: 10, marginTop: 6 },
  h1: { fontSize: 24, fontWeight: "bold", color: "#14213d" },
  h2: { fontSize: 18, fontWeight: "bold", color: "#14213d" },
  b: { fontWeight: "bold" },
  in: { borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, padding: 10 },
  tarjeta: { padding: 12, borderRadius: 10, backgroundColor: "#f1f5f9", marginBottom: 8, gap: 4 },
  chip: { paddingHorizontal: 12, paddingVertical: 8, borderRadius: 16, backgroundColor: "#f1f5f9", marginRight: 8 },
  chipActivo: { backgroundColor: "#fde3cf" },
  pestanas: { flexDirection: "row", justifyContent: "space-around", borderTopWidth: 1, borderColor: "#e2e8f0", paddingVertical: 10 },
  pestana: { fontSize: 16, color: "#64748b" },
  pestanaActiva: { color: "#e8833a", fontWeight: "bold" },
  err: { color: "#b91c1c" },
  aviso: { color: "#b45309" },
  gris: { color: "#64748b", fontSize: 12 },
});
