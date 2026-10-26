# Fase 3 — URPOnline: IA agéntica de punta a punta, a escala latinoamericana (especificación)

> Rama `fase-3`. Presentación: sábado 21 de noviembre de 2026, **40 minutos por grupo**, todo en vivo. El caso completo, los 38 desafíos, el guion de la presentación, las advertencias de costo y la guía paso a paso están en el `README.md`; la rúbrica Word se genera del mismo texto (`RUTA_DEL_CURSO/09_FASE3_IA_MOVIL_CONTENEDORES/URPOnline_Fase3_Rubrica.docx`). Este documento resume el alcance y la arquitectura; si algo difiere, manda el README.

**Objetivo del curso:** que los alumnos construyan un portafolio de proyectos con experiencias y casos reales que les permita usarlo en entrevistas y evaluaciones de trabajo.

## 0. El negocio (resumen)

URPOnline cerró una Serie A de 12 millones de dólares para abrir Colombia, Chile y México, triplicar los pedidos y salir a bolsa en tres años. Quiere ser el primer comercio electrónico de Latinoamérica basado en IA agéntica de punta a punta. Antecedentes del caso: el Cyber Day caído 47 minutos (un solo servidor de base de datos, monitoreo solo de CPU, reinicio a mano), un bot que probó contraseñas toda una madrugada, una API key filtrada en un repositorio y **Thanos**, un agente de IA ofensivo que entró seis días por una ruta interna sin permiso y capturó tráfico cifrado («cosechar ahora, descifrar después»). Precedentes citados: el agente de OpenAI en el portal de Medicare de Australia (septiembre de 2026) y el algoritmo JVG (proyección no verificada). Cuatro compromisos: retener a cada cliente nuevo, operar la cadena de valor sin intervención manual, una plataforma que no se puede vulnerar y prepararse para salir a bolsa.

## 1. Alcance

- **Contenedores:** imagen Docker multietapa, usuario no root, en ECR con escaneo; importador de proveedores también en contenedor.
- **Kubernetes:** EKS sobre Fargate en la VPC de la tienda; HPA; blue/green con reversión automática (Argo Rollouts); un namespace por país con cuotas; Fargate frente a nodos EC2.
- **Balanceo por capa y caídas en vivo:** web (ALB entre pods de dos zonas), negocio (API en su propio Deployment y Service), base de datos (réplica de lectura de RDS); se cae un nodo de cada capa, incluida la desconexión de la base de datos, y se recupera solo.
- **Monitoreo:** tablero de las tres capas, alarmas por capa con SNS, SLO con presupuesto de errores, recuperación ante desastres medida.
- **Seguridad:** WAF, secretos fuera del código, imágenes revisadas, aislamiento de red, agentes que no se dejan manipular, detección y contención, plan contra Thanos (criptografía post-cuántica) y sniffer en las capas profundas.
- **Machine Learning en CPU:** dataset propio de CloudWatch, predicción de saturación, escalado proactivo, capacidad y costo de un país nuevo.
- **IA agéntica gobernada:** cadena de valor por eventos, agente de operaciones (planificar y ejecutar, orquestador con subagentes, memoria), solo lectura con aprobación humana, bitácora auditable.

## 2. Arquitectura y código base

| Capa | Servicio | Dónde |
|---|---|---|
| Contenedores | Docker multietapa; ECR con escaneo (tienda, predictor, importador) | `Dockerfile`, `Worker/Dockerfile`, `infra/fase3-contenedores.yaml` |
| Clúster | EKS sobre Fargate, IRSA por pod, namespaces `urponline` (Perú) y `urponline-cl` (Chile) | `k8s/eksctl-cluster.yaml`, `k8s/paises.yaml` |
| Web | Rollout blue/green, HPA, dos zonas, PodDisruptionBudget, Ingress → ALB con WAF | `k8s/deployment.yaml`, `k8s/bluegreen-analisis.yaml` |
| Negocio | API en su propio Deployment + Service + HPA; el ALB le manda `/api/` | `k8s/api-pedidos.yaml` |
| Base de datos | RDS primario + réplica de lectura opcional (`EnableReadReplica`); el catálogo lee de la réplica | `infra/urponline-oneclick.yaml`, `Data/ShopReadDbContext.cs` |
| Monitoreo y seguridad | WAF, alarmas por capa, tablero de tres capas | `infra/fase3-seguridad.yaml` |
| ML | scikit-learn en CPU; CronJob que sube el mínimo del HPA | `ml/`, `k8s/prediccion.yaml` |
| Cadena de valor | Importador de proveedores como CronJob | `k8s/proveedores-cronjob.yaml` |
| Agente de operaciones | ServiceAccount de solo lectura (RBAC) | `k8s/agente-solo-lectura.yaml` |

Lo que construye cada grupo está en la sección «Lo que ya trae el repositorio» del README.

## 3. Agente de operaciones (patrones de agentes)

| Patrón | Uso en el clúster |
|---|---|
| Planificar y ejecutar | Arma el plan completo de un despliegue o de un escalado antes de ejecutarlo; se compara con ReAct |
| Orquestador con subagentes | Ante una caída provocada en vivo, un supervisor reparte entre subagentes de métricas, de Kubernetes (solo lectura) y de predicción |
| Memoria | Recuerda los incidentes anteriores y qué acción funcionó |
| Gobierno | Solo lectura, aprobación humana para todo cambio y bitácora auditable con MTTR |

## 4. Costos

Detalle con precios verificados y reglas en «Advertencias de costo» del README. Lo principal: EKS 0,10 USD por hora por existir; dos NAT Gateway a 0,045 USD por hora cada uno; RDS con réplica o Multi-AZ duplica la base; WAF unos 9 USD al mes; **AWS Network Firewall 0,395 USD por hora por endpoint** (el más caro: solo para la prueba y la presentación). Se crea, se prueba y se borra; presupuesto de AWS Budgets antes de empezar.

## 5. Rúbrica: 38 desafíos en 6 bloques (20 puntos, todo o nada por bloque)

| Bloque | Desafíos | Puntos |
|---|---|---|
| A. Crecer sin cortes: contenedores y despliegue | A1–A6 | 3 |
| B. Balanceo de carga y caída de cada capa | B1–B6 | 4 |
| C. Monitoreo y autorrecuperación | C1–C6 | 3 |
| D. Seguridad en cada capa y defensa contra Thanos | D1–D8 | 3 |
| E. Capacidad y costo anticipados con Machine Learning | E1–E6 | 3 |
| F. La cadena de valor con IA agéntica, gobernada | F1–F6 | 4 |
| **Total** | **38** | **20** |

Cada desafío se construye, se demuestra en vivo y se explica; el profesor elige a qué integrante preguntar. Si un desafío falla o no se sabe explicar, su bloque vale 0. Video obligatorio el día anterior con todos los integrantes y la cámara encendida. El texto de cada desafío está en el README.
