// Emulacion de DoS (corta) — Fase 2, escenario 1.
// Un pico BREVE y agresivo contra TU PROPIA infraestructura para validar que la soporta y que tu
// monitoreo lo IDENTIFICA como un DoS (lo distingue de la carga normal). Objetivo: detectar, alertar
// y observar que le pasa a la plataforma. NUNCA se lanza contra sistemas de terceros.
//
// Uso (desde CloudShell):
//   k6 run -e BASE_URL=https://tu-tienda pruebas/dos.js
//
// Observa en CloudWatch: salto de peticiones/s, errores 5xx o throttles, p95 disparado, y la alarma
// de DoS. Mira que absorbe el golpe (Auto Scaling, SQS, concurrencia) y si la tienda sigue respondiendo.

import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5000';

export const options = {
  scenarios: {
    dos: {
      executor: 'constant-arrival-rate',
      rate: 500,            // 500 req/s: salto brusco respecto a la carga normal
      timeUnit: '1s',
      duration: '1m',       // corto
      preAllocatedVUs: 200,
      maxVUs: 800,
    },
  },
  // No ponemos umbrales estrictos: aqui queremos VER la degradacion, no "pasar".
};

export default function () {
  const r = http.get(`${BASE}/`);
  check(r, { 'respondio': (res) => res.status !== 0 });
}
