# Fase 3 — Docker, Kubernetes y escalado con Machine Learning (especificación)

> Rama `fase-3`. Presentación: 21 de noviembre de 2026. La guía paso a paso para los alumnos está en el `README.md`.

## 0. El negocio

URPOnline **recibió capital de inversión para crecer en toda Latinoamérica**. El plan es abrir la tienda y el delivery en varios países a la vez, y para que la inversión rinda tiene que cumplir tres cosas.

**Retener a sus clientes.** Cada país nuevo trae picos que nadie predice a mano: el lanzamiento en Bogotá, el Cyber de Santiago, la hora del almuerzo en Ciudad de México. Si la tienda se cae o se pone lenta en ese momento, el cliente nuevo no vuelve. La plataforma tiene que escalar sola, desplegar sin cortes y anticipar la saturación antes de que ocurra.

**Integrar toda su cadena de valor en forma automatizada.** Proveedores, catálogo, pedidos, pagos, restaurantes y repartidores tienen que funcionar como una sola operación, sin personas copiando datos entre sistemas ni revisando servidores uno por uno.

**Prepararse para salir a bolsa.** Los inversionistas y los reguladores no compran promesas: piden evidencia de disponibilidad, costos predecibles, capacidad planificada, operación auditable y controles que impidan cambios no autorizados en producción.

**Lo que hace el grupo.** Construye esa evidencia con Docker, Kubernetes (EKS sobre Fargate), Machine Learning en CPU y un agente de operaciones de solo lectura, todo desplegado con CloudFormation. **EKS cobra por hora:** se crea, se prueba, se borra y se recrea para la presentación. **20 desafíos en 4 bloques de 5 puntos, todo o nada:** cada desafío se construye, se demuestra en vivo y se explica; el profesor elige a qué integrante preguntar, y si un desafío falla o no se sabe explicar, su bloque vale 0.

## 1. Alcance

- **Contenedores:** la tienda en una imagen **Docker** multietapa, usuario no root, publicada en **ECR** con escaneo.
- **Kubernetes:** la tienda corriendo en **EKS sobre Fargate** dentro de la VPC de la tienda, con HPA por CPU e Ingress → ALB.
- **Fargate frente a EC2:** la misma tienda en nodos EC2 administrados, para comparar dónde corren los pods.
- **Despliegue blue/green** con reversión automática ante una versión con error.
- **Predicción con Machine Learning** de la saturación de la arquitectura y escalado proactivo.
- **Agente de operaciones de Kubernetes** con tres patrones: planificar y ejecutar, orquestador con subagentes y memoria.

## 2. Arquitectura

| Capa | Servicio | Dónde |
|---|---|---|
| Contenedores | **Docker** multietapa, usuario no root; **ECR** con escaneo | `Dockerfile`, `infra/fase3-contenedores.yaml` |
| Kubernetes | **EKS sobre Fargate** dentro de la VPC de la tienda (subredes privadas de aplicación); IRSA para Secrets Manager y CloudWatch; HPA por CPU; Ingress → ALB | `k8s/eksctl-cluster.yaml`, `k8s/deployment.yaml` |
| Fargate vs. EC2 | `managedNodeGroups` que agrega el grupo | `k8s/eksctl-cluster.yaml` |
| Blue/green | dos versiones + pesos en el Ingress + reversión automática (Argo Rollouts u otra herramienta), lo construye el grupo | `k8s/`, `k8s/bluegreen-analisis.yaml` |
| ML | scikit-learn en CPU: dataset de CloudWatch, horizonte de 5 min, evaluación por episodio contra persistencia; CronJob que sube el mínimo del HPA; alarma + SNS | `ml/`, `k8s/prediccion.yaml` |

## 3. Agente de operaciones (patrones de agentes)

| Patrón | Uso en el clúster |
|---|---|
| Planificar y ejecutar | Arma el plan completo de un despliegue blue/green o de un escalado antes de ejecutarlo; se compara con ReAct |
| Orquestador con subagentes | Ante un incidente, un supervisor reparte entre subagentes de métricas (CloudWatch), de Kubernetes (solo lectura) y de predicción (ML) |
| Memoria | Recuerda los incidentes anteriores del clúster y qué acción funcionó |

## 4. Costos

- **EKS cobra por el clúster aunque no tenga carga** (aprox. 0,10 USD por hora): se crea, se prueba y se borra (README, paso 8).
- Fargate cobra por vCPU y memoria por segundo mientras los pods corren.

## 5. Rúbrica: 20 desafíos (4 bloques de 5 puntos, todo o nada)

### Bloque A — Crecer sin cortes: contenedores y despliegue (5 puntos)
1. **A1. Imagen Docker.** Dockerfile de varias etapas, publicado en ECR con escaneo sin hallazgos críticos y con la imagen reducida. *Explicar:* qué hace cada etapa y por qué la imagen final es más pequeña y segura.
2. **A2. EKS sobre Fargate.** La tienda en EKS sobre Fargate, con autoescalado de pods demostrado bajo carga. *Explicar:* por qué EKS y Fargate no son alternativas.
3. **A3. Fargate frente a EC2.** El mismo contenedor en los dos, con tabla de costo y rendimiento y elección justificada. *Explicar:* cuándo conviene cada uno.
4. **A4. Blue/green con reversión automática.** Versión nueva sin errores durante el cambio, y vuelta atrás sola cuando suben los errores. *Explicar:* qué pasa con las sesiones durante el cambio.
5. **A5. Un país no tumba a otro.** Un namespace por país con su cuota de recursos: saturar un país no degrada al otro. *Explicar:* cómo aíslan los namespaces y las cuotas.

### Bloque B — Capacidad anticipada con Machine Learning (5 puntos)
6. **B1. Dataset propio.** Bajo carga (k6) exportan de CloudWatch las métricas de todo el stack y las etiquetan (saturado / no saturado). *Explicar:* por qué su propia telemetría es mejor que un dataset ajeno.
7. **B2. Modelo en CPU y open source.** Predice la saturación de los próximos minutos; reproducible, con precisión, recall y cobertura de episodios, y mejor que el modelo trivial de persistencia. *Explicar:* por qué precisión y recall, y no solo el acierto.
8. **B3. Escalado proactivo.** La predicción sube los pods antes de saturar, en vivo con k6, con contador en CloudWatch y alerta por SNS. *Explicar:* escalar por reacción frente a escalar por predicción.
9. **B4. Capacidad y costo de un país nuevo.** Con el modelo y sus mediciones, proyectan cuántos pods y cuánto costo mensual necesita abrir un país más. *Explicar:* qué supuestos usaron y qué los haría equivocarse.
10. **B5. Interpretación.** Qué señales anticipan la saturación, con cuánta anticipación y cuánto cuesta una alarma falsa frente a una caída sin aviso. *Explicar:* qué cambiarían en la arquitectura con lo que reveló el modelo.

### Bloque C — Operación madura: agente de operaciones (5 puntos)
11. **C1. Planificar y ejecutar.** El agente arma el plan completo de un despliegue o de un escalado antes de ejecutarlo, comparado con ReAct sobre el mismo caso. *Explicar:* cuándo conviene planificar antes y cuándo ReAct.
12. **C2. Orquestador con subagentes.** Ante un incidente inyectado en vivo, un supervisor reparte entre los subagentes de métricas, de Kubernetes y de predicción hasta llegar a la causa. *Explicar:* cómo reparte y qué pasa si un subagente falla.
13. **C3. Memoria de incidentes.** Un segundo incidente parecido se resuelve con la memoria del primero. *Explicar:* qué guarda la memoria y qué no.
14. **C4. Solo lectura y aprobación humana.** El subagente de Kubernetes tiene RBAC e IAM de solo lectura (su intento de borrar un pod se rechaza) y toda acción que cambia el clúster espera la aprobación de una persona. *Explicar:* por qué el agente no debe cambiar el clúster por su cuenta.
15. **C5. Bitácora auditable.** Cada incidente queda registrado con qué se hizo, por qué, quién lo aprobó y cuánto tardó en resolverse (MTTR). *Explicar:* qué le muestra esa bitácora a un auditor.

### Bloque D — Cadena de valor integrada y lista para la bolsa (5 puntos)
16. **D1. Cadena de valor automatizada.** La importación de proveedores corre sola en el clúster (CronJob) y un pedido de la tienda o del delivery llega a su destino por eventos, sin intervención manual. *Explicar:* qué parte de la cadena dejó de depender de una persona.
17. **D2. Separación de funciones.** A producción solo se llega por el pipeline (GitHub Actions con OIDC y CloudFormation); un `kubectl apply` manual desde una laptop se rechaza. *Explicar:* por qué un regulador exige este control.
18. **D3. Disponibilidad demostrable (SLO).** Definen un objetivo de servicio (por ejemplo, 99,5 % y p95 menor a 500 ms) y lo miden en un tablero de CloudWatch con su presupuesto de errores. *Explicar:* qué hacen cuando se gasta el presupuesto de errores.
19. **D4. Costos predecibles.** Etiquetas de costo por país y servicio, y un presupuesto de AWS Budgets con alerta. *Explicar:* cuánto cuesta hoy cada país y qué lo encarece.
20. **D5. Recuperación ante desastres.** Borran el clúster y lo recrean desde cero con CloudFormation, midiendo cuánto tarda (RTO) y comprobando que los datos siguen en RDS. *Explicar:* qué se pierde y qué no cuando cae todo el clúster.

**Requisitos:** video con toda la implementación el día anterior (participan todos, con la cámara encendida). Todo como código (**CloudFormation**; `eksctl` también crea el clúster con CloudFormation). Sin GPU.
