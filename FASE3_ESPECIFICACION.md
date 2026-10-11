# Fase 3 — IA completa, apps móviles y contenedores (especificación)

> Rama `fase-3`. Fecha inicial: 21 de noviembre de 2026.
> Estado: **borrador para revisión del profesor, sin implementar.**

## 1. Alcance (lo indicado por el profesor)

- **Agentes completos, RAG e IA** sobre lo construido en la fase 2.
- **Más patrones de agentes**: enrutador, planificar y ejecutar, orquestador con subagentes, humano en el ciclo y memoria.
- **JSON en el prompt**: prompts y salidas estructuradas en JSON con esquema validado.
- **Aplicación móvil** que consume los servicios de la tienda por **API**, con **gestión de APIs**.
- **Aplicaciones web escalables** en contenedores.
- **Docker, Fargate, Kubernetes (EKS) y despliegue blue/green.**

## 2. Arquitectura propuesta

| Capa | Servicio |
|---|---|
| App móvil | Por definir con el profesor (.NET MAUI para seguir en C#, o Flutter) |
| Gestión de APIs | Amazon API Gateway: claves de API, planes de uso, límites de solicitudes, versiones (`/v1`) y documentación OpenAPI |
| Autenticación de la app | Por definir (Amazon Cognito es la opción nativa) |
| API de la tienda | ASP.NET Core Web API en **Docker**: catálogo, carrito, pedidos, chat de soporte y agentes |
| Contenedores | **ECS Fargate** detrás del ALB, con Auto Scaling por CPU y por solicitudes |
| Kubernetes | **EKS** con el mismo contenedor, para comparar con Fargate |
| Despliegue | **Blue/green**: la versión nueva se levanta al lado de la actual y el tráfico se cambia en el ALB sin cortar la tienda; vuelta atrás inmediata |
| Imágenes | Amazon ECR, con escaneo de vulnerabilidades |

## 3. Patrones de agentes nuevos

| Patrón | Uso en la tienda |
|---|---|
| Enrutador | El chat clasifica cada mensaje (soporte, pedido, queja) y lo envía al agente correcto |
| Planificar y ejecutar | El agente de stock arma un plan completo antes de ejecutarlo; se compara con ReAct |
| Orquestador con subagentes | Ante la tormenta, un supervisor reparte el trabajo entre analista, carrito y stock |
| Humano en el ciclo | Decisiones de alto impacto esperan aprobación desde la app móvil o `/admin` |
| Memoria | El agente de soporte recuerda la conversación anterior del cliente (corto y largo plazo) |

## 4. JSON en el prompt

- El contexto y las instrucciones se entregan al modelo en JSON; la salida debe cumplir un **esquema JSON**.
- Validación del esquema en el código; si la salida no cumple, se reintenta o se rechaza.
- Experimento: comparar prompt en texto libre frente a prompt y salida en JSON (aciertos, errores de formato, alucinaciones, costo).

## 5. Costos a planificar

- **EKS cobra por el clúster aunque no tenga carga** (aprox. 0,10 USD por hora). Definir cuándo se enciende y se apaga.
- Fargate cobra por vCPU y memoria por segundo mientras las tareas corren.

## 6. Decisiones pendientes del profesor

1. Tecnología de la app móvil.
2. Autenticación de la app (¿Cognito?).
3. ¿EKS lo despliegan todos los grupos o se demuestra en clase por el costo?
4. Rúbrica, banco de preguntas y requisitos.
