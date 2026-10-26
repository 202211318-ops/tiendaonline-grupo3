# Fase 3 — Docker, Kubernetes y escalado con Machine Learning (especificación)

> Rama `fase-3`. Presentación: 28 de noviembre de 2026. La guía paso a paso para los alumnos está en el `README.md`.

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

- **EKS cobra por el clúster aunque no tenga carga** (aprox. 0,10 USD por hora): se crea, se prueba y se borra (README, paso 10).
- Fargate cobra por vCPU y memoria por segundo mientras los pods corren.
