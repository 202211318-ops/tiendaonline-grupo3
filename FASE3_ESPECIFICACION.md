# Fase 3 — IA completa, apps móviles y contenedores (especificación)

> Rama `fase-3`. Presentación: 21 de noviembre de 2026. La guía paso a paso para los alumnos está en el `README.md`.

## 1. Alcance

- **Agentes completos, RAG e IA** sobre lo construido en la fase 2.
- **Más patrones de agentes**: enrutador, planificar y ejecutar, orquestador con subagentes, humano en el ciclo y memoria.
- **JSON en el prompt**: prompts y salidas estructuradas en JSON con esquema validado.
- **Aplicación móvil** que consume los servicios de la tienda por **API**, con **gestión de APIs**.
- **Aplicaciones web escalables** en contenedores: **Docker, EKS sobre Fargate y despliegue blue/green**.
- **Predicción con Machine Learning** de la saturación de la arquitectura y escalado proactivo.

## 2. Arquitectura

| Capa | Servicio | Dónde |
|---|---|---|
| App móvil | **React Native (Expo)** | `mobile/` |
| Autenticación de la app | **Amazon Cognito** (User Pool, cliente público sin secreto, SRP) | `infra/fase3-contenedores.yaml` |
| Gestión de APIs | **API Gateway HTTP API**: autorizador JWT de Cognito, `/v1`, límite de solicitudes (429). Claves y planes de uso: REST API que agrega el grupo | `infra/fase3-contenedores.yaml` |
| API de la tienda | ASP.NET Core: `GET /api/v1/products` y `/api/v1/products/{id}` | `Controllers/ProductsApiController.cs` |
| Contenedores | **Docker** multietapa, usuario no root; **ECR** con escaneo | `Dockerfile` |
| Kubernetes | **EKS sobre Fargate** dentro de la VPC de la tienda (subredes privadas de aplicación); IRSA para Secrets Manager y CloudWatch; HPA por CPU; Ingress → ALB | `k8s/eksctl-cluster.yaml`, `k8s/deployment.yaml` |
| Fargate vs. EC2 | `managedNodeGroups` que agrega el grupo | `k8s/eksctl-cluster.yaml` |
| Blue/green | dos versiones + pesos en el Ingress + reversión automática (Argo Rollouts u otra herramienta), lo construye el grupo | `k8s/` |
| ML | scikit-learn en CPU: dataset de CloudWatch, horizonte de 5 min, evaluación por episodio contra persistencia; CronJob que sube el mínimo del HPA; alarma + SNS | `ml/`, `k8s/prediccion.yaml` |

## 3. Patrones de agentes

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

## 5. Costos

- **EKS cobra por el clúster aunque no tenga carga** (aprox. 0,10 USD por hora): se crea, se prueba y se borra (README, paso 10).
- Fargate cobra por vCPU y memoria por segundo mientras los pods corren.
