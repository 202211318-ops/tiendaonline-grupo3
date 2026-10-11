# Fase 2 — Agentes en la tienda (especificación)

> Rama `fase-2`. El laboratorio del 24 de octubre (`main`) está cerrado y no se modifica.
> Estado: **especificación acordada, sin implementar.** Cada punto marcado *(por defecto)* es una decisión que se toma así salvo que el profesor indique otra.

## 1. Objetivo

Que el alumno **domine RAG y agentes**: cómo cada tipo de agente razona y consume los datos de forma distinta, cómo se integran en una arquitectura AWS orientada a eventos, cómo se protegen (guardrails, secretos, cadena de suministro) y cómo se interpreta lo que hacen.

## 2. Los cuatro agentes

| # | Agente | Disparador | Tipo de razonamiento | Datos que consume | Acción | Dónde corre *(por defecto)* |
|---|---|---|---|---|---|---|
| 1 | **Stock** | EventBridge Scheduler (cada noche) y evento `StockBajo` | ReAct con herramientas | SQL: stock, ventas por día, tiempo de reposición | Propone una orden de compra; la aprueba un humano en `/admin` | Lambda C# |
| 2 | **Recuperación de carrito** | Evento `CarritoAbandonado` / `CompraCancelada` → SQS | Personalización sobre **una persona** | SQL: historial del cliente + RAG del catálogo (alternativas parecidas) + hallazgos del agente 4 | Correo con una oferta dentro de las reglas del negocio (descuento máximo) por Amazon SES | Lambda C# |
| 3 | **Soporte** | Pregunta del cliente en el chat de la tienda | Conversacional con respuesta fundamentada | **RAG estructural** de los contratos SLA por producto | Respuesta que cita la sección usada | Dentro de la aplicación |
| 4 | **Analista** | EventBridge Scheduler (cada 15 minutos) | Análisis de **agregados** (todos los clientes) | Resumen SQL de compras, cancelaciones, motivos y señales de comportamiento | Alerta al negocio por SNS para replantear el producto (precio, garantía, descripción, envío) o alerta positiva | Lambda C# |

Los agentes colaboran: los hallazgos del agente 4 los usa el agente 2 en sus ofertas.

## 3. RAG estructural

- Cada producto tiene su **contrato de nivel de servicio (SLA)** guardado **por secciones** en PostgreSQL: garantía, devoluciones, tiempos de entrega, soporte técnico, exclusiones.
- Cada sección es una fila con su vector (pgvector, el mismo modelo ONNX de 384 dimensiones que ya usa la búsqueda, con índice HNSW).
- Recuperación: primero **filtro por producto y tipo de sección**, luego **similitud** dentro de ese subconjunto.
- Experimento obligatorio: comparar con un **RAG ingenuo** (texto cortado en trozos iguales) con las mismas 10 preguntas; medir respuestas correctas y citas correctas.

## 4. Guardrails (agente de soporte)

1. **System prompt**: solo productos, pedidos y SLA de la tienda; si no está en el contexto, responde "no tengo esa información".
2. **Filtro de entrada**: rechaza preguntas fuera de tema, inapropiadas o con intentos de prompt injection.
3. **Filtro de salida**: la respuesta debe citar una sección recuperada y no revelar datos de otros clientes.
4. **Batería de 20 preguntas trampa** que deben quedar bloqueadas.

## 5. Ventana de configuración de modelos (`/admin/modelos`)

- Para cada agente: proveedor y modelo. Gratuitos: Gemini, Gemma, DeepSeek, Nemotron, modelos gratuitos de OpenRouter. De pago: Claude, OpenAI.
- El grupo pega **su** API key; se guarda en **AWS Secrets Manager** y no vuelve a mostrarse. Botón **Probar conexión**.
- Cliente único compatible con la API de OpenAI (cambian la URL, la key y el modelo).

## 6. Escenario 1: tormenta de compras y cancelaciones

- El profesor entrega un **generador (k6)** que simula cientos de clientes con una **causa oculta** (por ejemplo: subida de precio, cambio en la sección de garantía, falla de un método de pago).
- La tienda captura **motivo de cancelación** (lista opcional) y **señales de comportamiento** (producto visto, tiempo, consulta al agente de soporte y sección del SLA, paso de abandono, pago rechazado) como eventos en EventBridge → SQS.
- Requisitos de arquitectura: SQS absorbe el pico; **idempotencia** (un mensaje duplicado no genera dos correos); **máximo un correo por cliente cada 24 h**; alertas **agrupadas en ventanas de 15 minutos**; el modelo razona sobre el **resumen SQL**, no sobre cada evento.
- Evaluación: ¿descubrió la causa oculta con evidencia?, ¿una sola alerta clara?, ¿correos dentro de las reglas?, ¿interpretación del grupo sobre si el agente acertó?

## 7. Escenario real: la dependencia infectada (cadena de suministro)

- Basado en un incidente real, presentado de forma anónima.
- El profesor entrega un paquete **NuGet** `URPOnline.Agentes.Conectores` que se presenta como "conectores de LLM" *(por defecto, unido a los agentes)*. Simulación **inofensiva**: consume CPU, intenta conectarse a `203.0.113.10:3333` (dirección reservada para documentación) e intenta leer las API keys **sin enviarlas fuera de la cuenta**.
- El grupo lo detecta (CPU, Flow Logs, SSM), mide su alcance (rol IAM, IMDSv2), lo contiene sin tirar la tienda (regla de salida, reversión por el pipeline) y blinda el pipeline (`packages.lock.json` en modo bloqueado, `nuget.config` con fuentes permitidas, `dotnet list package --vulnerable` que frena el despliegue, salida a internet solo hacia lo necesario).

## 8. Datos personales (Ley 29733)

- Correos **solo a clientes de prueba** con los correos verificados de los propios alumnos (SES en modo sandbox) *(por defecto)*.
- El identificador de sesión nunca se registra tal cual (hash).
- El agente de soporte nunca revela datos de otros clientes.

## 9. Servicios AWS que se usan

EventBridge (bus y Scheduler), SQS con cola de mensajes fallidos, Lambda, SES, SNS, Secrets Manager, RDS PostgreSQL con pgvector, CloudWatch (métricas, logs y alarmas), VPC Flow Logs, Systems Manager, IAM.

## 10. Para un laboratorio posterior

- **Mythos traspasó la DMZ con descifrado cuántico** (detectar, contener, medir el daño, rediseñar; criptografía post-cuántica). *(por defecto, posterior)*
- Escenarios estilo MIT: partición de red, cerebro dividido, falla gris, falla en cascada, avalancha de caché, caída de una dependencia externa o de una región.

## 11. Orden de construcción propuesto

1. Eventos de la tienda (compras, cancelaciones con motivo, señales) → EventBridge → SQS, con su infraestructura en el YAML y pruebas.
2. Ventana `/admin/modelos` y cliente LLM con keys en Secrets Manager.
3. RAG estructural de los SLA y agente 3 con guardrails.
4. Agentes 2 y 4, y el generador del escenario 1.
5. Agente 1.
6. Paquete NuGet del escenario de cadena de suministro.
7. Rúbrica y banco de preguntas de la fase 2.
