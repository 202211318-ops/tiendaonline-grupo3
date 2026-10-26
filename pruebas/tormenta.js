// Generador de la TORMENTA (k6) — Fase 2, escenario 1.
// Simula cientos de clientes que compran y cancelan a la vez, con una CAUSA OCULTA distinta por grupo.
// El profesor fija la causa con la variable CAUSA (p. ej. precio, garantia, pago) y una SEMILLA.
// El grupo NO conoce la causa: su agente analista debe descubrirla.
//
// Uso (desde CloudShell):
//   k6 run -e BASE_URL=https://tu-tienda -e CAUSA=garantia -e SEMILLA=42 pruebas/tormenta.js
//
// No se hace en clase: los resultados se traen. En la presentacion se lanza con una SEMILLA nueva.

import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5000';
const CAUSA = __ENV.CAUSA || 'precio';            // causa oculta del grupo
const SEMILLA = __ENV.SEMILLA || '1';

export const options = {
  scenarios: {
    tormenta: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 300,
      stages: [
        { target: 20, duration: '1m' },
        { target: 120, duration: '2m' },   // pico
        { target: 20, duration: '1m' },
      ],
    },
  },
  thresholds: { http_req_failed: ['rate<0.1'], http_req_duration: ['p(95)<2000'] },
};

// Motivos de cancelacion que captura la tienda (lista cerrada).
const MOTIVOS = ['precio', 'costo_envio', 'garantia', 'demora', 'pago', 'me_arrepenti', 'otro'];

export default function () {
  const productId = 1 + Math.floor(Math.random() * 6);

  // 1) Ver producto y agregar al carrito.
  http.get(`${BASE}/?q=producto`);
  const add = http.post(`${BASE}/cart/add`, JSON.stringify({ productId, quantity: 1 }),
    { headers: { 'Content-Type': 'application/json' } });
  check(add, { 'carrito ok': (r) => r.status < 500 });

  // 2) Una parte compra; otra cancela con un motivo sesgado por la CAUSA OCULTA (sin decir cual es).
  if (Math.random() < 0.5) {
    http.post(`${BASE}/checkout`, null);
  } else {
    // La causa oculta hace que su motivo salga mas seguido: eso es lo que el analista debe detectar.
    const motivo = Math.random() < 0.6 ? CAUSA : MOTIVOS[Math.floor(Math.random() * MOTIVOS.length)];
    http.post(`${BASE}/checkout/cancel`, JSON.stringify({ reason: motivo, seed: SEMILLA }),
      { headers: { 'Content-Type': 'application/json' } });
  }
  sleep(Math.random());
}
