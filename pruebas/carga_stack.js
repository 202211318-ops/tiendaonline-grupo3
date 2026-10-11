// Fase 3 - Genera carga para que el modelo de ML tenga TELEMETRIA de todo el stack.
// Corre esto mientras ml/collect_metrics.py recolecta: subes y bajas la carga para tener ejemplos
// de 'saturado' y 'no saturado'.
//   k6 run -e BASE_URL=https://TU-TIENDA pruebas/carga_stack.js
import http from 'k6/http';
import { sleep } from 'k6';
const BASE = __ENV.BASE_URL || 'http://localhost:5000';
export const options = {
  scenarios: {
    sube_y_baja: {
      executor: 'ramping-vus',
      startVUs: 5,
      stages: [
        { target: 20, duration: '3m' },
        { target: 150, duration: '5m' },   // satura
        { target: 10, duration: '3m' },    // se recupera
        { target: 120, duration: '4m' },   // vuelve a saturar
        { target: 5, duration: '2m' },
      ],
    },
  },
};
export default function () {
  http.get(`${BASE}/`);
  http.get(`${BASE}/?q=algo%20para%20escuchar`);
  sleep(Math.random() * 2);
}
