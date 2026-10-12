# URPOnline — Laboratorio AWS (TiendaOnline .NET)

> **Fase 3:** 21 de noviembre

---

# 🧑‍🤝‍🧑 PRIMERO: CREA EL REPOSITORIO DE TU GRUPO. TRABAJA SOLO EN ÉL

Este repositorio del curso es **solo de lectura**: aquí no puedes subir cambios ni desplegar. Cada grupo crea **su propio repositorio**, sube ahí una copia del laboratorio y trabaja solo en él: ahí corren **sus** GitHub Actions contra **su** cuenta de AWS, y ese repositorio es el que se evalúa.

1. Acepta la invitación que te llegó al correo de la universidad (o entra a https://github.com/orgs/cortexgovernor/invitation): sin ella no puedes clonar el laboratorio.
2. **Un integrante crea el repositorio del grupo:** en GitHub, **+ → New repository** → *Owner*: su cuenta → *Repository name*: `tiendaonline-grupoN` (N = número del grupo) → **Private** → **sin** README, `.gitignore` ni licencia → **Create repository**.
3. **Ese integrante clona el laboratorio del curso y lo sube al repositorio del grupo** (terminal de VS Code):
   ```bash
   git clone --branch fase-3 https://github.com/cortexgovernor/tiendaonline.git tiendaonline-grupoN
   cd tiendaonline-grupoN
   git branch -M main                       # la fase queda como rama main del grupo
   git remote rename origin curso           # el repositorio del curso queda como "curso" (solo para leer)
   git remote add origin https://github.com/TU_USUARIO/tiendaonline-grupoN.git
   git push -u origin main
   ```
4. **Agrega a los demás integrantes y al profesor** como colaboradores (**Settings → Collaborators → Add people**). Cada integrante lo clona: `git clone https://github.com/TU_USUARIO/tiendaonline-grupoN.git`.
5. El enlace de **ese** repositorio es el que se entrega con el video.

> Si el curso publica una corrección de la fase: `git pull curso fase-3` y luego `git push`.

---

# 📋 FASE 3 — IA, APPS MÓVILES Y CONTENEDORES · RÚBRICA (20 PUNTOS)

### 👥 Grupos de **7 a 10** integrantes · 🎤 **Presentación: 21 de noviembre de 2026** · participan **todos** los integrantes

**4 categorías de 5 puntos, todo o nada** (notas 0, 5, 10, 15, 20). Sin GPU. Todo como código (**CloudFormation**; `eksctl` también crea el clúster con CloudFormation). App móvil: **React Native**. Autenticación: **Amazon Cognito + integración propia del grupo**.

| Categoría | Qué se evalúa (pasos y criterio de cumplimiento) | Puntos |
|---|---|---|
| **1. Contenedores y despliegue** | **Imagen Docker:** multietapa, publicada en ECR con escaneo, sin hallazgos críticos. **EKS sobre Fargate:** Rollout (Argo Rollouts), Service e Ingress con autoescalado de pods. **Fargate vs. EC2:** el mismo contenedor en ambos; costo y rendimiento justificados. **Blue/green con reversión automática:** si al cambiar el tráfico suben los errores, vuelve solo a la versión anterior. | 5 o 0 |
| **2. IA, API y app móvil** | **API de la tienda** con contrato OpenAPI. **Gestión de APIs** (claves, planes de uso, 429 al superar el límite, versión /v1). **App React Native + Cognito** con la integración propia del grupo, sin secretos en la app. **Patrones de agentes** (enrutador, planificar y ejecutar vs. ReAct, orquestador con subagentes, humano en el ciclo y memoria). **JSON en el prompt** (salida validada por esquema; experimento texto libre vs. JSON; entre agentes y hacia la app). **Caos suave en la app** (cortar la red o la API sin que se cierre). | 5 o 0 |
| **3. Validación — predecir toda la arquitectura** | **Dataset propio:** bajo carga (k6) exportan de CloudWatch las métricas de todo el stack y las etiquetan. **Modelo de Machine Learning (ML) en CPU y open source** (scikit-learn, XGBoost/LightGBM o ML.NET, sin costo): aprende de su telemetría a predecir cuándo la arquitectura se va a saturar o fallar. **Para qué sirve:** tener un modelo que prediga el comportamiento de la plataforma y ayude a decidir cuándo escalar y cómo mejorar o rediseñar; presentan precisión y recall, y por **episodio de saturación** la cobertura y la anticipación frente a un modelo trivial de persistencia. **Escalado proactivo:** la predicción sube los pods antes de saturar + contador en CloudWatch y alerta por SNS. **Interpretación** de los resultados. | 5 o 0 |
| **4. Sustentación individual** | Banco de **50 preguntas**; se sortean algunas por integrante. Si un integrante no sustenta, la categoría vale 0 y ese alumno tiene 0 en el trabajo. | 5 o 0 |

**Requisitos:** video con toda la implementación (participan todos, entregado el día anterior); la app llega instalada en un dispositivo o emulador. **Cada categoría es todo o nada. El alumno que no sustenta tiene 0.**


---

# 🚀 DESPLEGAR Y PROBAR LA FASE 3, PASO A PASO

> Todo con **CloudFormation y eksctl** (infraestructura como código). **EKS cobra el clúster por hora** aunque no tenga carga: créalo, pruébalo, **bórralo** (paso 10) y recréalo para la presentación.
> Todo en la región **Ohio (`us-east-2`)**, la misma de la tienda.

```
 App React Native
   │  ID token de Cognito + clave de API
   ▼
 API Gateway (REST) /v1 ── 401 sin token · 403 sin clave · 429 si se pasa del plan
   ▼
 ALB del Ingress ──► pods de la tienda en EKS/Fargate ──► RDS + Redis
                     (subredes privadas de la VPC de la tienda; blue/green con Argo Rollouts)

 CloudWatch ──► CronJob de predicción (ML) ──► sube el mínimo de pods del HPA ANTES de saturar + alerta SNS
```

## Paso 0. Lo que necesitas antes de empezar

1. **La tienda desplegada** con la pila `URPOnlineLab` (fases 1-2): la fase 3 usa su VPC, su RDS, su Redis y su secreto de Secrets Manager.
2. **Herramientas** en tu PC (una sola vez):

| Herramienta | Para qué | Instalar (Windows) | Comprobar |
|---|---|---|---|
| AWS CLI v2 | todo lo de AWS | `winget install Amazon.AWSCLI` | `aws --version` |
| Docker Desktop | construir las imágenes | https://www.docker.com/products/docker-desktop | `docker version` |
| kubectl | manejar Kubernetes | `winget install Kubernetes.kubectl` | `kubectl version --client` |
| eksctl | crear el clúster EKS | `winget install eksctl.eksctl` | `eksctl version` |
| Helm | instalar el controlador del ALB | `winget install Helm.Helm` | `helm version` |
| Python 3.11+ | el modelo de ML | `winget install Python.Python.3.12` | `python --version` |
| k6 | generar carga | `winget install k6` | `k6 version` |
| Node.js 20 LTS | la app móvil (Expo) | `winget install OpenJS.NodeJS.LTS` | `node --version` |

3. **Las variables de la tienda.** Los comandos de esta guía son de **Bash** (Git Bash, WSL o CloudShell). Copia los Outputs de `URPOnlineLab` en variables:

```bash
export AWS_REGION=us-east-2
out() { aws cloudformation describe-stacks --stack-name "$1" --query "Stacks[0].Outputs[?OutputKey=='$2'].OutputValue" --output text; }
VPC=$(out URPOnlineLab VpcId);              PUB_A=$(out URPOnlineLab PublicSubnetA); PUB_B=$(out URPOnlineLab PublicSubnetB)
APP_A=$(out URPOnlineLab AppSubnetA);       APP_B=$(out URPOnlineLab AppSubnetB)
RDS=$(out URPOnlineLab RDSEndpoint);        REDIS=$(out URPOnlineLab RedisPrimary)
SECRET=$(out URPOnlineLab DbSecretArn);     DB_SG=$(out URPOnlineLab DbSecurityGroup); REDIS_SG=$(out URPOnlineLab RedisSecurityGroup)
echo "$VPC $APP_A $APP_B $RDS $REDIS"       # si sale vacío, actualiza la pila URPOnlineLab con el YAML de esta rama
```

## Paso 1. Pila de la fase 3: ECR, Cognito, API Gateway, alertas y bucket del modelo

```bash
aws cloudformation deploy --stack-name urponline-fase3 \
  --template-file infra/fase3-contenedores.yaml --capabilities CAPABILITY_IAM \
  --parameter-overrides AppNameUrl=http://pendiente.invalid CorreoAlertas=tu-correo@ejemplo.com
ECR=$(out urponline-fase3 EcrRepoUri);  ECR_ML=$(out urponline-fase3 EcrMlRepoUri)
MODELOS=$(out urponline-fase3 ModelosBucket);  SNS=$(out urponline-fase3 AlertasTopicArn)
```

- `AppNameUrl` todavía no existe (es el ALB que creará EKS en el paso 4): por ahora va un valor provisional y en el **paso 5** se actualiza.
- Te llega un correo de AWS: **confirma la suscripción** para recibir las alertas de saturación.

| Crea | Para qué |
|---|---|
| ECR `urponline` y `urponline-ml` (con escaneo) | las imágenes de la tienda y del predictor |
| Cognito (User Pool + cliente sin secreto) | el inicio de sesión de la app |
| API Gateway **REST API**: autorizador de Cognito, ruta `/v1/{proxy+}`, **clave de API** y **plan de uso** (10 req/s por clave, ráfaga 20, 100 000 al mes) | la API gestionada: sin token → 401, sin clave → 403, si se pasa → 429 |
| SNS `urponline-alertas` + alarma `urponline-saturacion-predicha` | la alerta cuando el ML predice saturación |
| S3 `urponline-modelos-…` (cifrado, privado) | donde vive `modelo.joblib` |

## Paso 2. Imagen de la tienda (Docker → ECR)

```bash
bash scripts/descargar-modelo.sh                 # modelo de búsqueda semántica (Windows: scripts/descargar-modelo.ps1)
aws ecr get-login-password | docker login --username AWS --password-stdin ${ECR%/*}
docker build -t $ECR:v1 .
docker push $ECR:v1
aws ecr describe-image-scan-findings --repository-name urponline --image-id imageTag=v1 \
  --query "imageScanFindings.findingSeverityCounts"          # la rúbrica pide 0 CRITICAL
```

El `Dockerfile` es **multietapa** (compila con el SDK, ejecuta solo con el runtime), corre como **usuario no root** y trae un `HEALTHCHECK` sobre `/health`. Pruébalo en tu PC **antes** de subirlo:

```bash
docker network create urp
docker run -d --name urp-pg --network urp -e POSTGRES_USER=urponline -e POSTGRES_PASSWORD=localonly -e POSTGRES_DB=urponline pgvector/pgvector:pg16
docker run -d --name urp-redis --network urp redis:7
docker run -d --name urp-app --network urp -p 8080:8080 -e InitializeDatabase=true \
  -e "ConnectionStrings__Postgres=Host=urp-pg;Port=5432;Database=urponline;Username=urponline;Password=localonly" \
  -e "ConnectionStrings__Redis=urp-redis:6379" $ECR:v1
docker inspect -f '{{.State.Health.Status}}' urp-app      # healthy (tarda ~30 s)
curl http://localhost:8080/api/v1/products                 # el catálogo en JSON
docker rm -f urp-app urp-pg urp-redis && docker network rm urp
```

## Paso 3. Clúster EKS sobre Fargate, dentro de la VPC de la tienda

1. **Completa `k8s/eksctl-cluster.yaml`** con tus valores (`<VPC_ID>`, `<PUBLIC_A>`, `<PUBLIC_B>`, `<APP_A>`, `<APP_B>`, `<DB_SECRET_ARN>`):
   ```bash
   sed -i "s|<VPC_ID>|$VPC|; s|<PUBLIC_A>|$PUB_A|; s|<PUBLIC_B>|$PUB_B|; s|<APP_A>|$APP_A|; s|<APP_B>|$APP_B|; s|<DB_SECRET_ARN>|$SECRET|" k8s/eksctl-cluster.yaml
   ```
2. **Crea el clúster** (15-20 min). Crea también los roles IAM de los pods (IRSA): la tienda lee la contraseña de RDS de **Secrets Manager**, igual que las EC2; nunca hay Access Keys en un pod.
   ```bash
   aws eks describe-cluster-versions --query "clusterVersions[].[clusterVersion,endOfStandardSupportDate]" --output table   # la versión del YAML debe seguir en soporte estándar
   eksctl create cluster -f k8s/eksctl-cluster.yaml
   kubectl get nodes                                  # aparecen nodos "fargate-..."
   ```
3. **Deja entrar a los pods a RDS y Redis.** Los pods de Fargate usan el *security group del clúster*:
   ```bash
   EKS_SG=$(aws eks describe-cluster --name urponline --query cluster.resourcesVpcConfig.clusterSecurityGroupId --output text)
   aws ec2 authorize-security-group-ingress --group-id $DB_SG    --protocol tcp --port 5432 --source-group $EKS_SG
   aws ec2 authorize-security-group-ingress --group-id $REDIS_SG --protocol tcp --port 6379 --source-group $EKS_SG
   ```
4. **Instala metrics-server** (sin él, el autoescalado de pods no ve la CPU):
   ```bash
   kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.7.2/components.yaml
   ```
5. **Instala el AWS Load Balancer Controller** (convierte el Ingress en un ALB):
   ```bash
   helm repo add eks https://aws.github.io/eks-charts && helm repo update
   helm install aws-load-balancer-controller eks/aws-load-balancer-controller -n kube-system \
     --set clusterName=urponline --set serviceAccount.create=false \
     --set serviceAccount.name=aws-load-balancer-controller --set region=$AWS_REGION --set vpcId=$VPC
   kubectl -n kube-system rollout status deployment aws-load-balancer-controller
   ```
6. **Instala Argo Rollouts** (el blue/green con reversión automática del paso 8) y su plugin de kubectl:
   ```bash
   kubectl create namespace argo-rollouts
   kubectl apply -n argo-rollouts -f https://github.com/argoproj/argo-rollouts/releases/download/v1.7.2/install.yaml
   kubectl -n argo-rollouts rollout status deployment argo-rollouts
   # Plugin (Windows): descarga kubectl-argo-rollouts-windows-amd64 de
   # https://github.com/argoproj/argo-rollouts/releases/tag/v1.7.2, renómbralo a kubectl-argo-rollouts.exe y ponlo en el PATH
   kubectl argo rollouts version
   ```

## Paso 4. Desplegar la tienda en EKS

```bash
sed -i "s|<ECR_IMAGE>|$ECR:v1|; s|<RDS_ENDPOINT>|$RDS|; s|<REDIS_ENDPOINT>|$REDIS|; s|<DB_SECRET_ARN>|$SECRET|; s|<PUBLIC_A>|$PUB_A|; s|<PUBLIC_B>|$PUB_B|" k8s/deployment.yaml
kubectl apply -f k8s/bluegreen-analisis.yaml      # la prueba automática del blue/green (paso 8)
kubectl apply -f k8s/deployment.yaml               # ConfigMap + Rollout + Services + Ingress + HPA
kubectl argo rollouts get rollout urponline -n urponline --watch   # espera "Status: ✔ Healthy" (Ctrl+C)
ING=$(kubectl -n urponline get ingress urponline -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')
curl http://$ING/health                            # {"status":"ok"}  (el ALB tarda 2-3 min)
curl http://$ING/api/v1/products                   # el catálogo en JSON
kubectl -n urponline get hpa                       # TARGETS debe mostrar un % de CPU, no <unknown>
```

**Autoescalado de pods:** con carga (paso 7) mira `kubectl -n urponline get hpa -w`: al pasar el 60 % de CPU suben las réplicas (hasta 10).

## Paso 5. Conectar API Gateway con la tienda

```bash
aws cloudformation deploy --stack-name urponline-fase3 \
  --template-file infra/fase3-contenedores.yaml --capabilities CAPABILITY_IAM \
  --parameter-overrides AppNameUrl=http://$ING
API=$(out urponline-fase3 ApiUrl)                  # https://....execute-api.us-east-2.amazonaws.com/prod/v1
CLAVE=$(aws apigateway get-api-key --api-key $(out urponline-fase3 ApiKeyId) --include-value --query value --output text)

# Usuario de prueba y su token (ID token de Cognito)
POOL=$(out urponline-fase3 UserPoolId); CLIENTE=$(out urponline-fase3 UserPoolClientId)
aws cognito-idp admin-create-user --user-pool-id $POOL --username alumno@ejemplo.com \
  --user-attributes Name=email,Value=alumno@ejemplo.com Name=email_verified,Value=true --message-action SUPPRESS
aws cognito-idp admin-set-user-password --user-pool-id $POOL --username alumno@ejemplo.com --password 'Clave2026Segura' --permanent
TOKEN=$(aws cognito-idp initiate-auth --client-id $CLIENTE --auth-flow USER_PASSWORD_AUTH \
  --auth-parameters USERNAME=alumno@ejemplo.com,PASSWORD='Clave2026Segura' --query AuthenticationResult.IdToken --output text)

curl -s -o /dev/null -w "%{http_code}\n" -H "x-api-key: $CLAVE" $API/products                       # 401: sin token
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: $TOKEN" $API/products                    # 403: sin clave de API
curl -s -H "Authorization: $TOKEN" -H "x-api-key: $CLAVE" $API/products                               # 200: el catálogo
for i in $(seq 1 60); do curl -s -o /dev/null -w "%{http_code} " -H "Authorization: $TOKEN" -H "x-api-key: $CLAVE" $API/products & done; wait; echo   # aparecen 429
```

- `/v1/{proxy}` de API Gateway llega a `/api/v1/{proxy}` de la tienda (`Controllers/ProductsApiController.cs`): `GET /v1/products` y `GET /v1/products/{id}`.
- **Clave de API y plan de uso:** la clave identifica a la app frente al **plan** `urponline-basico` (10 solicitudes/s por clave, ráfaga 20, 100 000 al mes); al pasarse, **429**. Una clave que **no** está en el plan recibe **403**: créala sin asociarla y pruébala. La clave no es un secreto de autenticación (al usuario lo autentica Cognito).
- **Contrato OpenAPI:** expórtalo de la API desplegada:
  ```bash
  aws apigateway get-export --rest-api-id $(out urponline-fase3 RestApiId) --stage-name prod --export-type oas30 \
    --parameters extensions=apigateway openapi.json
  ```
- **Versionado:** agrega en el YAML un recurso `v2` con su `{proxy+}` y su método hacia `/api/v2/{proxy}` (más el controlador `v2` en la tienda) **y cambia el nombre lógico de `ApiDeployment`** para que se publique; la app que usa `/v1` no se rompe.

## Paso 6. App móvil (React Native + Expo + Cognito)

1. **Usuario de prueba:** el que creaste en el paso 5 (`alumno@ejemplo.com`).
2. **Configura y arranca la app:**
   ```bash
   cd mobile
   npm install
   # Edita config.ts: API_URL = Output ApiUrl (ya termina en /v1); API_KEY = el valor de $CLAVE (paso 5);
   # UserPoolId y ClientId = Outputs UserPoolId y UserPoolClientId
   npx expo start                                  # escanea el QR con Expo Go o abre un emulador (tecla a / i)
   ```
3. Inicia sesión con el usuario del punto 1 (la app usa **SRP**: la contraseña no viaja): la app pide `GET /v1/products` con el **ID token** en `Authorization` y la clave en `x-api-key`, y muestra el catálogo.
4. **Caos suave:** pon el teléfono en **modo avión**, o apaga la tienda (`kubectl -n urponline delete hpa urponline && kubectl -n urponline scale rollout urponline --replicas=0`): la app muestra *"Sin conexión con la API"* y **no se cierra**. Para volver: `kubectl apply -f k8s/deployment.yaml`.

La app **no lleva secretos**: el cliente de Cognito es público (sin secreto), el token vive solo en memoria y la clave de API solo identifica a la app frente al plan de uso.

## Paso 7. Modelo de ML: predecir la saturación y escalar ANTES

Detalle de cómo funciona: sección **"Cómo implementar el modelo de ML"** más abajo.

```bash
pip install -r ml/requirements.txt
ALB_DIM=$(aws elbv2 describe-load-balancers --query "LoadBalancers[?DNSName=='$ING'].LoadBalancerArn" --output text | sed 's|.*:loadbalancer/||')

# a) Primera corrida de carga (17 min, sube y baja) y su dataset
k6 run -e BASE_URL=http://$ING pruebas/carga_stack.js
python ml/collect_metrics.py --alb $ALB_DIM --horas 0.5 --p95-umbral-ms 2000 --salida ml/dataset.csv

# b) Entrenar: predice saturación en los próximos 5 min; reporta precisión, recall y la comparación con la persistencia
python ml/train.py --dataset ml/dataset.csv --horizonte 5

# c) Pruebas del ML (sin AWS)
python -m pytest -q ml/test_ml.py

# d) Subir el modelo y la imagen del predictor
aws s3 cp modelo.joblib s3://$MODELOS/modelo.joblib
aws ecr get-login-password | docker login --username AWS --password-stdin ${ECR_ML%/*}
docker build -t $ECR_ML:v1 ml/ && docker push $ECR_ML:v1

# e) CronJob que predice cada minuto y sube el mínimo de pods del HPA antes de saturar
sed -i "s|<ECR_ML_IMAGE>|$ECR_ML:v1|; s|<ALB>|$ALB_DIM|; s|<BUCKET>|$MODELOS|; s|<SNS_TOPIC>|$SNS|" k8s/prediccion.yaml
kubectl apply -f k8s/prediccion.yaml

# f) Segunda corrida de carga: mira el escalado proactivo en vivo
k6 run -e BASE_URL=http://$ING pruebas/carga_stack.js &
kubectl -n urponline logs -f job/$(kubectl -n urponline get jobs -o jsonpath='{.items[-1:].metadata.name}')
kubectl -n urponline get hpa -w                    # MINPODS sube de 2 a 4+ ANTES de que el p95 pase de 2 s
```

**Qué deben ver:** en CloudWatch (namespace `URPOnline`) las métricas `SaturacionPredicha` y `SaturacionProbabilidad`; la alarma `urponline-saturacion-predicha` en rojo y el correo de SNS **antes** de la saturación; y el `MINPODS` del HPA subiendo. **Compárenlo** con el escalado solo por CPU (reactivo): ¿cuántos minutos antes reaccionó cada uno?

## Paso 8. Fargate frente a EC2, y blue/green

**Fargate frente a EC2:** el **mismo** contenedor en un namespace `urponline-ec2`, que **no** está en el perfil de Fargate, así que sus pods van a nodos EC2. El clúster ya crea su ServiceAccount con permiso para leer el secreto de RDS (`k8s/eksctl-cluster.yaml`).

```bash
# Nodos EC2 en las subredes privadas de la tienda (usan el security group del clúster: ya llegan a RDS y Redis)
eksctl create nodegroup --cluster urponline --name ec2 --node-type t3.medium --nodes 2 \
  --node-private-networking --subnet-ids $APP_A,$APP_B
# La misma tienda en el namespace urponline-ec2 (otro ALB, misma imagen y configuración)
kubectl create namespace urponline-ec2
sed 's/namespace: urponline$/namespace: urponline-ec2/; s/\.urponline\.svc/.urponline-ec2.svc/' \
  k8s/bluegreen-analisis.yaml k8s/deployment.yaml | kubectl apply -f -
kubectl -n urponline-ec2 get pods -o wide                # NODE: ip-10-0-11-x (EC2), no fargate-...
ING_EC2=$(kubectl -n urponline-ec2 get ingress urponline -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')
k6 run -e BASE_URL=http://$ING pruebas/carga_stack.js      # Fargate
k6 run -e BASE_URL=http://$ING_EC2 pruebas/carga_stack.js  # EC2
```

Comparen con la **misma** carga: p95, errores, segundos que tarda un pod nuevo en estar listo (`kubectl get pods -w`) y costo por hora (Fargate cobra vCPU y memoria por segundo; EC2 cobra la instancia aunque esté ociosa). Justifiquen con cuál se quedan.

**Blue/green con reversión automática (Argo Rollouts, ya configurado en `k8s/deployment.yaml`):**

| Momento | Qué pasa |
|---|---|
| Publicas una versión nueva | se levanta **al lado** de la actual, detrás del Service `urponline-preview`, **sin tráfico** |
| Prueba previa | un Job de **k6** (`k8s/bluegreen-analisis.yaml`) la ataca 1 minuto: si hay más de 1 % de errores o el p95 pasa de 2 s, **se aborta** y el tráfico sigue en la anterior |
| Cambio | si la prueba pasa, el Service `urponline` (el del ALB) apunta a la versión nueva: **sin cortes** |
| Prueba posterior | la misma prueba contra la versión activa: si falla, el tráfico **vuelve solo** a la anterior, que sigue encendida 5 min |
| Versión que no arranca | si no está lista en 5 min, se aborta sola |

```bash
# Versión nueva buena: construye y sube v2 (paso 2 con :v2) y publícala
kubectl argo rollouts set image urponline urponline=$ECR:v2 -n urponline
kubectl argo rollouts get rollout urponline -n urponline --watch     # Preview -> análisis -> Active, "✔ Healthy"

# Versión con error: arranca, pero no llega a la base (/health 200, catálogo 500)
kubectl -n urponline patch rollout urponline --type json -p '[{"op":"add","path":"/spec/template/spec/containers/0/env/-","value":{"name":"ConnectionStrings__Postgres","value":"Host=no-existe;Port=5432;Database=urponline;Username=urponline;Timeout=3"}}]'
kubectl argo rollouts get rollout urponline -n urponline --watch     # el análisis falla -> "✖ Degraded" (RolloutAborted)
curl -s -o /dev/null -w "%{http_code}\n" http://$ING/api/v1/products   # 200: el tráfico SIGUE en la versión buena

# Volver a la versión buena en el Rollout (quita el cambio y reintenta)
kubectl argo rollouts undo urponline -n urponline
```

Demuéstrenlo **con carga** (k6 contra `http://$ING` mientras publican): durante el cambio y la reversión no debe haber errores.

## Paso 9. Qué presentar

1. La imagen en ECR con el escaneo **sin CRITICAL** y el `Dockerfile` multietapa.
2. La tienda en **EKS/Fargate**: `kubectl get pods,hpa,ingress` y el autoescalado de pods bajo carga.
3. Fargate vs. EC2 con números, y blue/green volviendo solo a la versión anterior.
4. La API: 401 sin token, 403 sin clave (o con una clave sin plan), 200 con token y clave, 429 al pasarse del plan, `/v1` y `/v2`, y el contrato OpenAPI exportado.
5. La app en un teléfono o emulador: login con Cognito, catálogo, y caos suave sin cerrarse.
6. El ML: precisión, recall, **cobertura por episodio frente a la persistencia**, anticipación en minutos, y el HPA subiendo **antes** de saturar con la alerta de SNS.

## Paso 10. Borrar todo (¡EKS cobra por hora!)

```bash
kubectl delete -f k8s/prediccion.yaml -f k8s/deployment.yaml -f k8s/bluegreen-analisis.yaml   # primero: así el controlador borra el ALB
kubectl delete namespace urponline-ec2 --ignore-not-found                                       # el segundo ALB (comparación con EC2)
aws ec2 revoke-security-group-ingress --group-id $DB_SG    --protocol tcp --port 5432 --source-group $EKS_SG
aws ec2 revoke-security-group-ingress --group-id $REDIS_SG --protocol tcp --port 6379 --source-group $EKS_SG
eksctl delete cluster -f k8s/eksctl-cluster.yaml                  # 10-15 min
aws s3 rm s3://$MODELOS --recursive
aws ecr delete-repository --repository-name urponline --force
aws ecr delete-repository --repository-name urponline-ml --force
aws cloudformation delete-stack --stack-name urponline-fase3
```

Si quitas los permisos del paso 3.3 **antes** de borrar el clúster, el security group del clúster se puede borrar sin errores. La pila `URPOnlineLab` (la tienda) se borra aparte, como en las fases anteriores.

## 📁 Dónde está cada cosa en el código

| Parte | Archivo |
|---|---|
| Imagen del contenedor | `Dockerfile` · `.dockerignore` |
| API JSON del catálogo para la app | `Controllers/ProductsApiController.cs` |
| EKS sobre Fargate (VPC de la tienda, IRSA) | `k8s/eksctl-cluster.yaml` |
| Tienda en Kubernetes (ConfigMap, Rollout blue/green, Services, Ingress, HPA) | `k8s/deployment.yaml` |
| Prueba automática del blue/green (k6) | `k8s/bluegreen-analisis.yaml` |
| ECR, Cognito, API Gateway REST (autorizador, /v1, clave, plan de uso, 429), SNS, alarma, bucket del modelo | `infra/fase3-contenedores.yaml` |
| App móvil (React Native + Cognito) | `mobile/` (`App.tsx`, `config.ts`) |
| Dataset de telemetría | `ml/collect_metrics.py` |
| Entrenar el modelo (CPU) | `ml/train.py` |
| Predecir + escalado proactivo | `ml/predict.py` · `ml/Dockerfile` · `k8s/prediccion.yaml` |
| Pruebas del ML | `ml/test_ml.py` |
| Carga para el dataset (k6) | `pruebas/carga_stack.js` |

---

# 🔧 QUÉ CONFIGURAR EN EL YAML Y QUÉ CÓDIGO CAMBIAR

El código se entrega **hecho y probado**: ustedes lo **despliegan, lo amplían y lo modifican**.

| Paso de la rúbrica | Archivo(s) | Qué hacen ustedes |
|---|---|---|
| Imagen Docker | `Dockerfile` | reducir el tamaño de la imagen; dejar el escaneo de ECR sin CRITICAL |
| EKS sobre Fargate | `k8s/eksctl-cluster.yaml` · `k8s/deployment.yaml` | desplegar; ajustar `resources` y el umbral del `HorizontalPodAutoscaler`; demostrar el autoescalado |
| Fargate vs. EC2 | `k8s/eksctl-cluster.yaml` | agregar un `managedNodeGroups` y comparar costo y rendimiento |
| Blue/green | `k8s/deployment.yaml` · `k8s/bluegreen-analisis.yaml` | ajustar la prueba (rutas, umbrales, duración) y demostrar el cambio y la reversión con carga (paso 8) |
| Gestión de APIs | `infra/fase3-contenedores.yaml` | límites del plan (429), una clave sin plan (403), ruta `/v2` y el contrato OpenAPI exportado |
| App móvil + Cognito | `mobile/` | refresco del token, pantallas (detalle, carrito), política de contraseñas y scopes en Cognito |
| Patrones de agentes / JSON | servicios de los agentes (de la fase 2) | enrutador, planificar y ejecutar, orquestador, memoria; salida JSON validada |
| Dataset de telemetría | `ml/collect_metrics.py` | qué métricas incluir y qué umbral de saturación tiene sentido |
| Modelo de ML | `ml/train.py` | algoritmo, horizonte y señales; reportar precisión, recall y la comparación con la persistencia |
| Escalado proactivo | `ml/predict.py` · `k8s/prediccion.yaml` | cuántos pods agregar (`--pods-extra`) y comparar contra el escalado por CPU |

# 🤖 CÓMO IMPLEMENTAR EL MODELO DE ML (LO HACEN USTEDES)

El repo trae una **implementación base funcional y con pruebas** (scikit-learn, CPU). Ustedes la **entienden, la adaptan y la interpretan**:

1. **Generen telemetría real.** `pruebas/carga_stack.js` (k6) **sube y baja** la carga para tener minutos *saturados* y *no saturados*. Sin las dos clases, el modelo no aprende.
2. **Armen y etiqueten el dataset.** `ml/collect_metrics.py` baja de CloudWatch, por minuto, las métricas de **cada recurso** (ALB p95, 5xx y solicitudes; RDS conexiones y CPU; SQS; Lambda) y marca `saturado=1` si el p95 del ALB superó el umbral. CloudWatch guarda estas métricas **por recurso**: por eso se le pasa `--alb` (o `--stack`, y él busca el ALB, el RDS, la cola y la Lambda de la pila).
3. **Entrenen** con `ml/train.py`. La etiqueta es: **¿habrá saturación en los próximos `--horizonte` minutos?** (por defecto 5: lo que tarda en arrancar un pod nuevo). Además del valor de cada métrica usa su **tendencia** (cuánto cambió respecto del minuto anterior): la carga subiendo es lo que avisa antes. Es una **serie de tiempo**: se entrena con el pasado (primer 70 %) y se prueba con el futuro (último 30 %); mezclar al azar pondría minutos vecinos en los dos lados e inflaría los resultados. RandomForest **balanceado** (la saturación es rara); pueden cambiarlo por XGBoost/LightGBM o ML.NET.
4. **Evalúen bien.**
   - **Precisión y recall** sobre «saturado», no el acierto: con eventos raros, un modelo que siempre dice «ok» acierta 95 % y no sirve.
   - **Por episodio de saturación**: ¿avisó **antes de que empezara** cada saturación (cobertura) y con cuántos minutos (anticipación)? Dentro de una saturación que ya empezó, decir «sigue saturado» es trivial.
   - **Contra la persistencia** («seguirá igual que ahora»): `train.py` imprime las dos. Si el ML no anticipa más episodios que la persistencia, no aporta.
   - **Señales**: qué métricas pesan más (importancia de variables).
5. **Conecten la predicción al escalado.** `ml/predict.py` corre cada minuto como CronJob en EKS (`k8s/prediccion.yaml`): publica `SaturacionPredicha` en CloudWatch, alerta por SNS y **sube el mínimo de pods del HPA** antes de saturar; cuando la predicción vuelve a «ok», lo devuelve a 2. Compárenlo con el escalado por CPU (reactivo).
6. **Interpreten.** ¿Qué señal avisa primero? ¿Con cuánta anticipación? ¿Cuál es el costo de una alarma falsa (pods de más) frente a una caída sin aviso? ¿Qué cambiarían en la arquitectura con lo que el modelo reveló?

> **Para qué sirve:** tener un modelo que prediga el comportamiento de la plataforma y les ayude a decidir **cuándo escalar** y **cómo mejorar o rediseñar** la arquitectura. Todo en **CPU, sin GPU y sin costo**.

---

# 📊 CÓMO AGREGAR CONTADORES A CLOUDWATCH (AWS SDK)

El monitoreo es **siempre con CloudWatch**, y ustedes crean **sus propios contadores** programando con el **AWS SDK** (`PutMetricData`), en el namespace `URPOnline`. Luego los grafican y les ponen alarmas.

**Desde la tienda (.NET, C#)** — igual que la fase 2 (`Services/BusinessMetrics.cs`):

```csharp
await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest {
    Namespace = "URPOnline",
    MetricData = new List<MetricDatum> {
        new() { MetricName = "PodsActivos",   Value = pods,  Unit = StandardUnit.Count },
        new() { MetricName = "LatenciaChat",  Value = ms,    Unit = StandardUnit.Milliseconds }
    }
});
```

**Desde el ML (Python, boto3)** — ya lo hace `ml/predict.py`:

```python
import boto3
boto3.client("cloudwatch").put_metric_data(Namespace="URPOnline", MetricData=[
    {"MetricName": "SaturacionPredicha", "Value": 1, "Unit": "Count"},
    {"MetricName": "SaturacionProbabilidad", "Value": prob*100, "Unit": "Percent"},
])
```

**Credenciales sin claves:** en EKS/Fargate el pod usa un **rol IAM** (IRSA, OIDC del clúster); en la EC2, el rol de la instancia. Nunca se escriben Access Keys. El permiso mínimo es `cloudwatch:PutMetricData` restringido al namespace `URPOnline`.

**Graficar y alarmar:** crea el dashboard y las alarmas **desde el YAML** (infraestructura como código), no a mano. Una alarma sobre `SaturacionPredicha` dispara el **autoescalado proactivo**.

---

## 💬 [Canal del curso: GitHub Discussions](https://github.com/cortexgovernor/tiendaonline/discussions)

El canal es **único para todo el curso** (el del repositorio principal): ahí se comparten dudas de la rúbrica y los desafíos, respuestas del banco de preguntas, código y soluciones a errores. No abran un Discussions aparte en el repositorio del grupo. **Nunca publiquen secretos** (contraseñas, claves de AWS, tokens).

---

## 👉 Empieza aquí

1. **Prepara tu equipo:** [CONFIGURACION_VSCODE.md](CONFIGURACION_VSCODE.md) — qué instalar y configurar (VS Code, .NET 8, AWS CLI, extensiones).
2. **Sigue la ruta del curso en orden:** carpeta [`RUTA_DEL_CURSO/`](RUTA_DEL_CURSO/) — abre primero `URPOnline_INICIO_AQUI.docx`.

| Paso | Carpeta | Contenido |
|---|---|---|
| 01 | `RUTA_DEL_CURSO/01_CASO_Y_ARQUITECTURA` | Caso empresarial, arquitectura y dashboard de analítica |
| 02 | `RUTA_DEL_CURSO/02_APLICACION` | La aplicación C# (código en la raíz de este repo) |
| 03 | `RUTA_DEL_CURSO/03_INSTALACION_Y_AWS_SDK` | Configuración local y AWS SDK (simulacro de 40 preguntas) |
| 04 | `RUTA_DEL_CURSO/04_SESIONES_REDIS` | Sesiones con Redis |
| 05 | `RUTA_DEL_CURSO/05_CLAUDE_CODE_Y_DESPLIEGUE` | Claude Code + CloudFormation → [MEMORIA_DESPLIEGUE.md](MEMORIA_DESPLIEGUE.md) |
| 06 | `RUTA_DEL_CURSO/06_DESAFIOS_AVANZADOS` | Ingeniería distribuida y pruebas de caos |
| 07 | `RUTA_DEL_CURSO/07_EVALUACION` | **Rúbrica de evaluación (20 puntos)** |

---

## 🔑 Conectar tu equipo a tu cuenta de AWS (AWS CLI)

**0. Descarga e instala la AWS CLI v2** (una sola vez):

- **Windows** — en PowerShell: `winget install Amazon.AWSCLI`
  o descarga el instalador: https://awscli.amazonaws.com/AWSCLIV2.msi (siguiente → siguiente → finalizar)
- **macOS** — https://awscli.amazonaws.com/AWSCLIV2.pkg o `brew install awscli`
- **Linux** — guía oficial: https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html

Comprueba la instalación (después de **cerrar y volver a abrir VS Code**): `aws --version` → debe mostrar `aws-cli/2.x.x`.

> El **AWS SDK for .NET** (S3, Secrets Manager, CloudWatch) **no se instala aparte**: ya está en el proyecto y lo descarga `dotnet restore`. Usa las mismas credenciales que configuras aquí con `aws configure`.

**1. Abre la terminal de VS Code:** menú **Terminal → New Terminal**.

> Si `aws` dice *"no se reconoce como comando"*: cierra **todas** las ventanas de VS Code y vuelve a abrirlo (recién instalado no se ve). Mientras tanto puedes usar la ruta completa: `& "C:\Program Files\Amazon\AWSCLIV2\aws.exe" configure`

**2. Inicia sesión SIN usar la cuenta root.** Nunca crees Access Keys de la cuenta **root**: quien las obtiene controla toda la cuenta y no se pueden limitar. Elige una forma:

- **Recomendada — IAM Identity Center (SSO), sin claves guardadas:** en la consola, **IAM Identity Center → Enable** → crea tu usuario y asígnale la cuenta con el permiso **AdministratorAccess** (solo para el laboratorio). Luego en la terminal:
  ```powershell
  aws configure sso          # SSO start URL: el "AWS access portal URL" de Identity Center; region: us-east-2
  $env:AWS_PROFILE = "el-nombre-de-perfil-que-elegiste"
  ```
  Las credenciales son **temporales** y se renuevan con `aws sso login`.
- **Alternativa — usuario IAM (no root) con Access Key:** **IAM → Users → Create user** (`laboratorio-urp`) → *Attach policies directly* → **AdministratorAccess** (solo laboratorio) → **Create user** → abre el usuario → **Security credentials → Create access key** → *Command Line Interface (CLI)*. Copia los dos valores (el secreto **solo se muestra una vez**):
  - **Access key ID** → 20 caracteres, **empieza con `AKIA`**
  - **Secret access key** → 40 caracteres

  Y configúralos:
  ```powershell
  aws configure
  ```

| Te pregunta | Escribe |
|---|---|
| `AWS Access Key ID` | la clave que empieza con `AKIA` (del usuario IAM, **nunca** de root) |
| `AWS Secret Access Key` | el secreto |
| `Default region name` | el **código** de la región, no el nombre: Ohio = **`us-east-2`**, N. Virginia = **`us-east-1`** |
| `Default output format` | **`json`** (es el formato en que se muestran los resultados) |

> ⚠️ El número de 12 dígitos de la página *Account* (ej. `5013-8083-3144`) es el **ID de cuenta**, **no** es la clave. Tampoco lo son los códigos de MFA. Al terminar el curso, **desactiva y borra** la Access Key del usuario IAM.

**4. Verifica:**

```powershell
aws sts get-caller-identity
```

Debe mostrar tu `Account` (12 dígitos) y tu `Arn`. 🔒 **Nunca pegues la Access Key ni el secreto en chats, código, correos ni en GitHub.**

| Error | Causa | Solución |
|---|---|---|
| `Could not connect to the endpoint URL: https://sts.OHIO.amazonaws.com` | Pusiste el nombre de la región | `aws configure set region us-east-2` |
| `InvalidClientTokenId` / `The security token included in the request is invalid` | La Access Key está mal copiada o no es una Access Key | Crea una nueva (paso 2) y vuelve a `aws configure` |
| `The SSO session associated with this profile has expired` | La sesión SSO venció | `aws sso login` |
| `Unable to locate credentials` | No ejecutaste `aws configure` / `aws configure sso` | Paso 2 |
| `SignatureDoesNotMatch` | El secreto está mal copiado | Vuelve a `aws configure` y pega el secreto completo |

---

## ☁️ Desplegar la tienda en AWS

> 📄 **Archivos de CloudFormation (carpeta `infra/`):**
> - [`infra/urponline-oneclick.yaml`](infra/urponline-oneclick.yaml) → **la tienda completa en AWS** (este es el que despliegas).
> - [`infra/github-oidc.yaml`](infra/github-oidc.yaml) → conexión GitHub Actions → AWS sin Access Keys (solo para la opción B).
>
> Si ves otra copia de `urponline-oneclick.yaml` en otra carpeta o en un ZIP del curso, **no la uses**: es una versión anterior.

Toda la arquitectura está escrita en ese archivo **YAML de CloudFormation**. AWS lee el archivo y crea **todo**:

| Capa | Recursos que crea el YAML |
|---|---|
| Red | VPC `10.0.0.0/16`, subredes **públicas** (ALB, NAT), **de aplicación** (EC2) y **de datos** (RDS, Redis) en 2 zonas, IGW, 2 NAT, tablas de rutas, security groups |
| Entrada | **CloudFront** con **HTTPS** (certificado de AWS, sin dominio propio) → **ALB** público en el puerto 80 |
| Aplicación | **Launch Template + Auto Scaling** (2 EC2 privadas con Nginx → Kestrel) |
| Datos | **RDS PostgreSQL 16** (con pgvector), **ElastiCache Redis** (sesiones, TLS), **S3** de proveedores |
| Seguridad | **Rol IAM** para las EC2 (S3 + Session Manager, sin claves) |

### 🔒 HTTPS: certificado digital

Para que la tienda abra con candado (`https://`) hace falta un **certificado digital**. En AWS hay dos caminos:

| | Opción 1 — CloudFront (**ya incluida en la plantilla**) | Opción 2 — Dominio propio + ACM |
|---|---|---|
| ¿Necesita dominio? | **No** | Sí (ej. `urponline.com`, ~13 USD/año en Route 53) |
| Certificado | El de AWS para `*.cloudfront.net` (automático) | **AWS Certificate Manager (ACM)**, gratis, para tu dominio |
| URL | `https://xxxx.cloudfront.net` | `https://tienda.tudominio.com` |
| Servicios | CloudFront → ALB | Route 53 + ACM + listener 443 en el ALB (y opcional CloudFront) |

> ¿Por qué no un certificado ACM para la dirección del ALB? ACM solo emite certificados para dominios que **demuestras que son tuyos**; `...elb.amazonaws.com` es de AWS. Por eso, **sin dominio**, el camino es CloudFront.

#### Opción 1 — CloudFront (sin dominio)

Ya está en `infra/urponline-oneclick.yaml` (recurso **`CDN`**): no hay que hacer nada extra.

- La URL segura aparece en **CloudFormation → URPOnlineLab → Outputs → `ApplicationHTTPS`**.
- Si entras por `http://` CloudFront te redirige a `https://`.
- Las páginas **no se guardan en caché** (el carrito y los precios siempre al día); los `/css/*` y `/js/*` **sí** (cargan más rápido).
- CloudFront tarda **5-10 min** en publicarse después de crearse; si al principio da error, espera y recarga.
- Compruébalo: abre la URL y haz clic en el **candado** del navegador → *La conexión es segura* → certificado emitido por **Amazon** para `*.cloudfront.net`.

#### Opción 2 — Con dominio propio (ACM + Route 53)

1. **Dominio en Route 53:** Consola → **Route 53 → Registered domains → Register domain** (o, si lo compraste en otro proveedor, **Hosted zones → Create hosted zone** y copia los 4 servidores *NS* en tu proveedor).
2. **Certificado en ACM** (en la **misma región del ALB**, ej. Ohio): Consola → **Certificate Manager → Request → Request a public certificate** → *Fully qualified domain name*: `tienda.tudominio.com` → **DNS validation** → **Request**. Abre el certificado → **Create records in Route 53** → espera el estado **Issued** (≈5 min).
3. **Listener HTTPS en el ALB:** Consola → **EC2 → Load Balancers** → el ALB de `URPOnlineLab` → **Listeners → Add listener** → *HTTPS : 443* → *Forward to* el target group de la pila → *Certificate*: el de ACM → **Add**.
4. **Abrir el puerto 443:** **EC2 → Security Groups** → el del ALB (`URPOnline ALB public HTTP...`) → **Edit inbound rules** → *Add rule*: HTTPS, 443, `0.0.0.0/0`.
5. **Registro DNS:** **Route 53 → Hosted zones → tu dominio → Create record** → nombre `tienda` → *Alias* → *Alias to Application Load Balancer* → región Ohio → el ALB → **Create**.
6. Abre `https://tienda.tudominio.com`.

> Los pasos 3 y 4 hechos a mano en la consola se pierden si borras la pila. Para dejarlos fijos, agrégalos al YAML como un `AWS::ElasticLoadBalancingV2::Listener` de puerto 443 con `Certificates: [{CertificateArn: ...}]` y una regla 443 en `AlbSG`.

### ⚡ Opción rápida: AWS CloudShell (sin instalar nada, sin Access Keys)

**CloudShell** es una terminal dentro de la consola de AWS que ya trae la AWS CLI conectada a tu cuenta.

> 🔒 Este repositorio es **privado**: CloudShell no puede descargarlo solo (no tiene tu sesión de GitHub). Por eso los dos archivos se descargan primero en tu navegador, donde sí iniciaste sesión, y se suben a CloudShell. No uses tokens ni contraseñas de GitHub en CloudShell.

1. **En tu navegador, con tu cuenta de GitHub iniciada**, descarga estos dos archivos a tu PC:
   - El ZIP compilado de la tienda: [Releases v1.0](https://github.com/cortexgovernor/tiendaonline/releases/tag/v1.0) → **`urponline-publish.zip`**.
   - La plantilla: [`infra/urponline-oneclick.yaml`](infra/urponline-oneclick.yaml) → botón **Download raw file** (icono de descarga, arriba a la derecha del archivo).
2. Consola de AWS → arriba a la derecha elige la región **Ohio (us-east-2)**.
3. Haz clic en el icono **`>_`** de la barra superior (o busca **CloudShell**).
4. En CloudShell: **Acciones → Cargar archivo** → sube `urponline-publish.zip`. Repite con `urponline-oneclick.yaml`. Quedan en tu carpeta de inicio (`~`).
5. Pega este bloque y presiona **Enter**:

```bash
cd ~ && ls urponline-publish.zip urponline-oneclick.yaml
REGION=us-east-2; STACK=URPOnlineLab
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
BUCKET=urponline-artefactos-$ACCOUNT-$REGION
DBPASS=$(tr -dc A-Za-z0-9 </dev/urandom | head -c 20); echo "$DBPASS" > ~/urponline-dbpass.txt
aws s3 mb s3://$BUCKET --region $REGION; aws s3 cp urponline-publish.zip s3://$BUCKET/releases/urponline-v1.zip
aws cloudformation deploy --stack-name $STACK --region $REGION --template-file urponline-oneclick.yaml --capabilities CAPABILITY_IAM --parameter-overrides ArtifactBucket=$BUCKET ArtifactKey=releases/urponline-v1.zip DBPassword=$DBPASS EnableMultiAZ=false
aws cloudformation describe-stacks --stack-name $STACK --region $REGION --query "Stacks[0].Outputs" --output table
```

| Línea | Qué hace |
|---|---|
| `ls ...` | Comprueba que los dos archivos se subieron (si dice *No such file*, repite el paso 4) |
| `ACCOUNT=` / `BUCKET=` | Arma un nombre de bucket único con tu número de cuenta |
| `DBPASS=` | Genera la contraseña de RDS y la guarda en `~/urponline-dbpass.txt` |
| `aws s3 mb` / `aws s3 cp` | Crea el bucket y sube el ZIP (las EC2 lo descargan al arrancar) |
| `aws cloudformation deploy` | Entrega el YAML a CloudFormation, que crea VPC, ALB, EC2, RDS, ElastiCache y S3 (20-30 min) |
| `describe-stacks` | Muestra los resultados: **`ApplicationHTTPS` es la URL segura de tu tienda** (CloudFront, `https://xxxx.cloudfront.net`); `ApplicationHTTP` es la del ALB directo |

Puedes cerrar CloudShell mientras se crea: sigue en AWS. Avance en **CloudFormation → URPOnlineLab → Events**; al terminar, la URL está en la pestaña **Outputs**.

---

Además, hay dos formas de hacerlo desde tu PC. **Haz primero la A** (para entender cada paso); después automatiza con la B.

> 🆓 **Cuentas del plan gratuito de AWS:** la plantilla ya viene ajustada (respaldo de RDS de **1 día**, instancias pequeñas t3.small / db.t3.micro / cache.t3.micro, RDS sin Multi-AZ). Si AWS rechaza algo por el plan gratuito, el motivo aparece en **CloudFormation → Events** (ej.: *exceeds the maximum available to free tier customers*).
>
> ⚠️ **Cuesta dinero mientras exista** (NAT, ALB, EC2, RDS, Redis). Al terminar la clase **bórrala** (ver "Borrar todo" al final de esta sección).

### A) A mano con la AWS CLI

Requisito: haber hecho **"Conectar tu equipo a tu cuenta de AWS"** (arriba). Ejecuta en la terminal de VS Code, desde la carpeta del proyecto:

```powershell
# 0. Variables (cambia la contraseña: 12+ caracteres, solo letras y números)
$REGION = "us-east-2"
$STACK  = "URPOnlineLab"
$DBPASS = "CambiaEstaClave2026"
$ACCOUNT = aws sts get-caller-identity --query Account --output text
$BUCKET = "urponline-artefactos-$ACCOUNT-$REGION"

# 1. Compilar para Linux (las EC2 usan Amazon Linux) e incluir el modelo de búsqueda
powershell -ExecutionPolicy Bypass -File scripts/descargar-modelo.ps1
dotnet publish TiendaOnline.csproj -c Release -r linux-x64 --self-contained false -o out
Compress-Archive -Path out\* -DestinationPath urponline-publish.zip -Force

# 2. Subir el ZIP a S3 (las EC2 lo descargan al arrancar)
aws s3 mb "s3://$BUCKET" --region $REGION
aws s3 cp urponline-publish.zip "s3://$BUCKET/releases/urponline-v1.zip"

# 3. Validar el YAML y crear la pila (15-30 min)
aws cloudformation validate-template --template-body file://infra/urponline-oneclick.yaml
aws cloudformation deploy --stack-name $STACK --region $REGION `
  --template-file infra/urponline-oneclick.yaml --capabilities CAPABILITY_IAM `
  --parameter-overrides ArtifactBucket=$BUCKET ArtifactKey=releases/urponline-v1.zip DBPassword=$DBPASS EnableMultiAZ=false

# 4. Ver el resultado
aws cloudformation describe-stacks --stack-name $STACK --region $REGION --query "Stacks[0].Outputs" --output table
```

Mientras se crea, míralo en la consola: **CloudFormation → URPOnlineLab → pestaña Events** (cada recurso aparece al crearse).

**Comprobar que funciona en AWS:**

```powershell
$URL = aws cloudformation describe-stacks --stack-name $STACK --region $REGION --query "Stacks[0].Outputs[?OutputKey=='ApplicationHTTP'].OutputValue" --output text
$TG  = aws cloudformation describe-stacks --stack-name $STACK --region $REGION --query "Stacks[0].Outputs[?OutputKey=='TargetGroupArn'].OutputValue" --output text

curl.exe "$URL/health"                                         # {"status":"ok"}
aws elbv2 describe-target-health --target-group-arn $TG --region $REGION --output table   # 2 EC2 "healthy"
1..6 | % { (curl.exe -s $URL) -match 'Servido por: <b>([^<]*)' | Out-Null; $Matches[1] }   # el ALB reparte entre las 2 EC2
start $URL                                                      # abre la tienda; busca "tomar fotos" (pgvector en RDS)
```

El pie de cada página dice **"Servido por: ip-10-0-11-x"** o **"ip-10-0-12-x"**: es la EC2 que te atendió. Recarga y verás el balanceo del ALB, y el carrito se mantiene porque la sesión vive en **ElastiCache**.

**Publicar una versión nueva:** repite el paso 1 y 2 con otro nombre (`urponline-v2.zip`) y el paso 3 con `ArtifactKey=releases/urponline-v2.zip`. CloudFormation reemplaza las EC2 **de una en una** sin tumbar la tienda.

### B) Automático con GitHub Actions: CI/CD (cada cambio se compila, se valida y se despliega solo)

#### ¿Qué es CI/CD y para qué sirve?

- **CI — Integración Continua:** cada vez que alguien sube un cambio a GitHub (`git push`), un servidor de GitHub **compila la aplicación y revisa la plantilla YAML automáticamente**. Si algo está roto, lo marca con una ❌ roja en minutos, **antes** de que llegue a AWS.
- **CD — Despliegue Continuo:** si la compilación sale bien, **el mismo servidor sube la nueva versión a AWS** (S3 + CloudFormation) sin que nadie ejecute comandos a mano.

**¿Por qué importa?** Sin CI/CD, cada versión exige repetir a mano los pasos de la opción A (compilar, zip, subir a S3, `cloudformation deploy`): es lento y es fácil equivocarse. Con CI/CD: **cambias código → `git push` → en ~20 min la tienda nueva está en AWS**, y cada paso queda registrado (quién, cuándo, qué se desplegó).

```
 Tú (VS Code)          GitHub                          AWS
 ───────────           ──────────────────────          ──────────────────────────
 git push   ───────►   GitHub Actions
                        1. compila .NET  (CI)
                        2. valida el YAML (CI)
                        3. arma el ZIP
                        4. sube el ZIP  ─────────────►  S3
                        5. cloudformation deploy ────►  CloudFormation actualiza
                                                        EC2 (de una en una), ALB...
                        6. prueba /health ◄───────────  ALB responde
                        ✅ / ❌ en la pestaña Actions
```

#### Primer despliegue vs. los siguientes

| | Qué hace CloudFormation | Tiempo |
|---|---|---|
| **Primer despliegue** | Crea **todo**: VPC, subredes, NAT, ALB, RDS, ElastiCache, S3, rol IAM y las EC2 | 20-30 min |
| **Cada cambio siguiente** (`git push`) | Compara el YAML y ve que solo cambió el ZIP de la app → **solo reemplaza las EC2, de una en una** (rolling update). VPC, RDS (tus datos), Redis y ALB **no se tocan** | 10-15 min, sin caída |

Si cambias el **YAML** (por ejemplo, agregas un recurso), CloudFormation actualiza solo ese recurso.

#### ¿Qué es GitHub Actions?

Es el servicio de GitHub que ejecuta esos pasos. Las instrucciones viven en archivos **YAML** dentro de [`.github/workflows/`](.github/workflows/) (cada archivo es un *workflow*). Este proyecto trae 3:

| Archivo | Nombre en GitHub | Cuándo corre | Qué hace | ¿Usa AWS? |
|---|---|---|---|---|
| `ci.yml` | *CI - compilar y validar* | Cada `git push` y cada pull request | Compila .NET y valida los YAML de CloudFormation con `cfn-lint` | No (gratis) |
| `deploy-aws.yml` | *Desplegar en AWS* | Cada `git push` a `main` (o con el botón **Run workflow**) | Compila para Linux, sube el ZIP a S3, ejecuta `aws cloudformation deploy`, prueba `/health` en el ALB y muestra la URL | Sí |
| `destroy-aws.yml` | *Destruir AWS* | Solo a mano, escribiendo `DESTRUIR` | Borra la pila para dejar de pagar | Sí |

> Los workflows ejecutan **exactamente los mismos comandos de la AWS CLI** de la opción A; solo que los corre un servidor de GitHub en vez de tu PC. Ábrelos en VS Code: cada paso tiene un `name:` que explica qué hace.

#### ¿Cómo entra GitHub a tu cuenta de AWS? (OIDC, sin Access Keys)

GitHub necesita permiso para crear recursos en **tu** cuenta. En lugar de guardar tu Access Key en GitHub (peligroso si se filtra), se usa **OIDC**: creas en AWS un **rol IAM** que dice *"confío en GitHub, pero solo en la rama `main` del repositorio `TU_USUARIO/tiendaonline-grupoN`"*. En cada ejecución GitHub pide a AWS credenciales **temporales** (duran minutos) para ese rol. Ese rol lo crea la plantilla [`infra/github-oidc.yaml`](infra/github-oidc.yaml).

#### Configúralo en TU GitHub, paso a paso (una sola vez, ~15 min)

**Paso 1 — El repositorio del grupo.**
Es el que crearon al inicio ("Crea el repositorio de tu grupo"): `https://github.com/TU_USUARIO/tiendaonline-grupoN`. Ahí correrán **sus** workflows contra **su** AWS.

**Paso 2 — Crea en AWS el rol para GitHub (OIDC).** Elige una forma:

- **Con la consola de AWS (sin terminal):**
  1. Descarga el archivo [`infra/github-oidc.yaml`](infra/github-oidc.yaml) (botón *Download raw file*).
  2. Consola de AWS (región **Ohio**) → **CloudFormation → Create stack → With new resources** → **Upload a template file** → elige `github-oidc.yaml` → **Next**.
  3. **Stack name:** `urponline-github-oidc` · **GitHubOwner:** tu usuario de GitHub · **RepoName:** `tiendaonline-grupoN` (el nombre de **su** repositorio) → **Next** → **Next**.
  4. Marca *"I acknowledge that AWS CloudFormation might create IAM resources with custom names"* → **Submit**.
  5. Cuando diga `CREATE_COMPLETE` (≈1 min) → pestaña **Outputs** → copia el valor de **`RoleArn`** (`arn:aws:iam::123456789012:role/urponline-github-deploy-tiendaonline`).
- **Con CloudShell** (icono `>_` en la consola de AWS):
  ```bash
  git clone https://github.com/TU_USUARIO/tiendaonline-grupoN.git && cd tiendaonline-grupoN
  aws cloudformation deploy --stack-name urponline-github-oidc --region us-east-2 \
    --template-file infra/github-oidc.yaml --capabilities CAPABILITY_NAMED_IAM \
    --parameter-overrides GitHubOwner=TU_USUARIO RepoName=tiendaonline-grupoN
  aws cloudformation describe-stacks --stack-name urponline-github-oidc --region us-east-2 \
    --query "Stacks[0].Outputs[0].OutputValue" --output text
  ```

> Si sale *"Provider with url https://token.actions.githubusercontent.com already exists"*: tu cuenta ya tenía la conexión. Repite con el parámetro **CreateOidcProvider = false**.

**Paso 3 — Guarda 2 secretos en el repositorio del grupo.** Los *secrets* son valores privados que los workflows pueden usar pero que nadie ve (ni en los logs).
En GitHub → **tu** repositorio → **Settings** (pestaña de arriba) → menú izquierdo **Secrets and variables → Actions** → botón **New repository secret**. Crea estos dos:

| Name | Secret |
|---|---|
| `AWS_ROLE_ARN` | el `RoleArn` del paso 2 |
| `DB_PASSWORD` | una contraseña para la base de datos RDS: **12 o más caracteres, solo letras y números** (ej. `UrpTienda2026Clave`) |

(Opcional) Si no usas Ohio: pestaña **Variables** → **New repository variable** → `AWS_REGION` = `us-east-1`.

**Paso 4 — Comprueba que Actions esté activo.** En un repositorio nuevo viene activado: pestaña **Actions** → deben aparecer los 3 workflows. Si dice que están desactivados, pulsa **Enable**.

**Paso 5 — Primer despliegue.** Pestaña **Actions** → en la izquierda **Desplegar en AWS** → botón **Run workflow** → **Run workflow**. Haz clic en la ejecución que aparece para ver cada paso en vivo (≈25 min la primera vez, porque crea toda la infraestructura). Al terminar, abre el **Summary**: muestra la **URL de la tienda** y las EC2 *healthy* detrás del ALB.

**Paso 6 — Comprueba el CI/CD con un cambio real.** En VS Code (con el repositorio del grupo clonado) cambia un texto, por ejemplo en `Views/Home/Index.cshtml`, y súbelo:

```powershell
git add .
git commit -m "Cambio mi primer texto"
git push
```

En **Actions** verás arrancar solos *CI - compilar y validar* y *Desplegar en AWS*. Cuando terminen, recarga la tienda: tu cambio está en AWS. Las EC2 se reemplazaron **de una en una** (Auto Scaling rolling update), así que la tienda nunca dejó de responder.

#### Alternativa: configurarlo todo desde VS Code (terminal + GitHub CLI)

Si prefieres no usar la web de GitHub, los pasos 1, 3, 4 y 5 se hacen con la **GitHub CLI (`gh`)** en la terminal de VS Code:

```powershell
# Instalar GitHub CLI (una vez) y cerrar/abrir VS Code
winget install GitHub.cli

# Iniciar sesión en GitHub con permiso para workflows (abre el navegador: pega el código que muestra y "Authorize")
gh auth login --web --scopes workflow

# Paso 1: clonar el laboratorio y crear con él el repositorio privado del grupo
git clone --branch fase-3 https://github.com/cortexgovernor/tiendaonline.git tiendaonline-grupoN
cd tiendaonline-grupoN
git branch -M main && git remote rename origin curso
gh repo create TU_USUARIO/tiendaonline-grupoN --private --source . --remote origin --push
code . -r

# Paso 2 (AWS): crear el rol OIDC — mismos comandos de la sección anterior, con GitHubOwner=TU_USUARIO

# Paso 3: guardar los 2 secretos (te pide pegar cada valor; no se muestran)
gh secret set AWS_ROLE_ARN
gh secret set DB_PASSWORD

# Paso 4: activar los workflows (en un repositorio nuevo ya lo están; no hace daño)
gh workflow enable ci.yml
gh workflow enable deploy-aws.yml
gh workflow enable destroy-aws.yml

# Paso 5: lanzar el despliegue y verlo en vivo en la terminal
gh workflow run deploy-aws.yml
gh run watch

# Al terminar la clase: borrar todo
gh workflow run destroy-aws.yml -f confirmar=DESTRUIR
```

**Ver las ejecuciones dentro de VS Code:** instala la extensión **GitHub Actions** (`github.vscode-github-actions`; VS Code la sugiere al abrir el proyecto). En la barra lateral aparece un icono de **GitHub Actions**: muestra cada workflow, su estado ✅/❌ y los logs de cada paso, y permite relanzarlos con clic derecho → **Run workflow**.

#### Leer los resultados

| Ves | Significa | Qué hacer |
|---|---|---|
| ✅ verde | Todo salió bien | Abre el **Summary** para la URL |
| ❌ rojo en *CI* | El código no compila o el YAML tiene un error | Clic en el paso rojo → lee el mensaje → corrige → `git push` |
| ❌ *Falta el secreto AWS_ROLE_ARN* | No hiciste el paso 3 | Crea el secreto y pulsa **Re-run jobs** |
| ❌ *Not authorized to perform sts:AssumeRoleWithWebIdentity* | `GitHubOwner` del paso 2 no coincide con tu usuario, o el push no fue a `main` | Revisa el usuario en la pila `urponline-github-oidc` |
| ❌ en *Crear o actualizar la pila* | CloudFormation falló | Consola de AWS → CloudFormation → `URPOnlineLab` → **Events** → la primera línea roja dice el motivo |

#### Al terminar la clase

Pestaña **Actions** → **Destruir AWS** → **Run workflow** → escribe `DESTRUIR` → **Run workflow**. Borra la pila para no seguir gastando.

**¿Por qué OIDC y no Access Keys?** GitHub pide a AWS credenciales **temporales** en cada ejecución; el rol solo acepta la rama `main` de **tu** repositorio. No hay claves guardadas que se puedan filtrar.

### 📊 Telemetría: ver todos los servicios funcionando en UNA pantalla

La plantilla crea un **dashboard de Amazon CloudWatch** llamado `URPOnline-URPOnlineLab` con todos los servicios juntos. Ábrelo desde **CloudFormation → URPOnlineLab → Outputs → `DashboardURL`** (o CloudWatch → **Dashboards**).

| Panel | Servicio | Qué demuestra |
|---|---|---|
| Alarmas | CloudWatch Alarms | Verde = todo bien. Rojo si una EC2 no responde o hay errores 5xx |
| Peticiones y errores 5xx · Tiempo de respuesta | **ALB** | La tienda recibe tráfico y responde rápido |
| EC2 sanas detrás del balanceador | **ALB + Auto Scaling** | Hay 2 EC2 *healthy* |
| CPU % y memoria % | **EC2** (agente de CloudWatch) | Carga de los servidores |
| Conexiones y CPU % | **RDS PostgreSQL** | La base de datos en uso |
| Conexiones y memoria % | **ElastiCache Redis** | Sesiones y carritos guardados |
| Pedidos y ventas S/ | **La app con el AWS SDK** (`PutMetricData`) | La aplicación escribe en CloudWatch desde el código |
| Peticiones y % de errores | **CloudFront** | El HTTPS y la caché |
| Logs de la tienda | **CloudWatch Logs** | Cada línea con la EC2 que la generó |
| Peticiones por EC2 | **CloudWatch Logs (Nginx)** | El ALB reparte entre las 2 EC2 |
| Tráfico rechazado | **VPC Flow Logs** | Los security groups bloquean lo que no debe pasar |

**Demo en clase (10 min):**

1. Abre el dashboard y, en otra pestaña, la tienda (`ApplicationHTTPS`).
2. Navega, busca (`tomar fotos`, `reloj deportivo`), agrega al carrito y **compra**. Repite 3 o 4 veces.
3. En 1-2 minutos (botón ⟳ del dashboard) suben **Peticiones**, **Conexiones RDS**, **Redis** y **Pedidos / Ventas**; en *Logs de la tienda* aparecen tus peticiones con el ID de la EC2.
4. **Prueba de alta disponibilidad:** consola → **EC2 → Instances** → elige una de las 2 → **Instance state → Terminate**. En el dashboard: *EC2 sanas* baja a 1 y la alarma se pone roja; Auto Scaling crea otra EC2 y en ~5 min vuelve a 2 y la alarma a verde. **La tienda nunca dejó de responder** y tu carrito sigue ahí (sesión en ElastiCache).

**Reachability Analyzer (seguridad de la red):** en **CloudShell**, dentro de la carpeta del proyecto:

```bash
bash scripts/analizar-red.sh
```

Resultado esperado:

```
ALB -> EC2 : 80 (Nginx)                       ALCANZABLE ✅
EC2 -> RDS : 5432 (PostgreSQL)                ALCANZABLE ✅
Internet -> EC2 : 80 (saltando el ALB)        BLOQUEADO 🔒
Internet -> RDS : 5432                        BLOQUEADO 🔒
```

Demuestra que **solo el ALB** llega a las EC2 y **solo las EC2** llegan a la base de datos. El detalle visual de cada ruta está en **VPC → Reachability Analyzer** (~0,10 USD por análisis).

### 🧩 AWS SDK for .NET: la tienda usa AWS desde su código

**¿Qué es?** Una librería que permite que **la aplicación** (no una persona) use los servicios de AWS desde el código C#.

| Herramienta | Quién la usa | Ejemplo en este curso |
|---|---|---|
| Consola web | Una persona, con clics | Abrir el dashboard de CloudWatch |
| AWS CLI | Una persona o un script, con comandos | `aws cloudformation deploy ...` |
| **AWS SDK** | **La aplicación, desde su código** | La tienda envía a CloudWatch *"se creó un pedido de S/ 3.799"* |

**¿Qué usa la tienda del SDK?**

| Paquete NuGet | Servicio | Para qué lo usa la tienda | Estado |
|---|---|---|---|
| `AWSSDK.CloudWatch` | **Amazon CloudWatch** | En cada compra publica las métricas **`OrdersCreated`** (pedidos) y **`OrderAmount`** (ventas S/) → panel *"Tienda (AWS SDK): pedidos y ventas S/"* del dashboard | ✅ En uso |
| `AWSSDK.S3` | **Amazon S3** | El **Worker** lee el CSV nocturno de proveedores (`GetObject`) para actualizar el catálogo | ⚠️ El Worker aún no compila (pendiente) |
| `AWSSDK.SecretsManager` | **AWS Secrets Manager** | Leer la contraseña de RDS de forma segura (en vez de un archivo en la EC2) | ⏳ Registrado, aún sin usar |
| `AWSSDK.Extensions.NETCore.Setup` | — | Conecta el SDK con ASP.NET Core: región y credenciales automáticas | ✅ En uso |

#### El código, paso a paso

**1. Instalar la librería** — [`TiendaOnline.csproj`](TiendaOnline.csproj) (la descarga `dotnet restore`):

```xml
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.7.400" />
<PackageReference Include="AWSSDK.CloudWatch" Version="3.7.401" />
```

**2. Registrar el cliente de CloudWatch** — [`Program.cs`](Program.cs):

```csharp
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions()); // región + credenciales
builder.Services.AddAWSService<IAmazonCloudWatch>();                          // cliente de CloudWatch
builder.Services.AddScoped<BusinessMetrics>();                                // nuestro servicio
```

**3. Llamar a AWS** — [`Services/BusinessMetrics.cs`](Services/BusinessMetrics.cs):

```csharp
public class BusinessMetrics(IAmazonCloudWatch cloudWatch, IConfiguration config, ILogger<BusinessMetrics> log) {
 public async Task OrderCreatedAsync(decimal amount, CancellationToken ct = default) {
  if (!config.GetValue("Metrics:CloudWatchEnabled", false)) return;   // en tu PC queda apagado
  try {
   await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest {      // ← la llamada a AWS
    Namespace = "URPOnline",
    MetricData = new List<MetricDatum> {
     new() { MetricName = "OrdersCreated", Value = 1, Unit = StandardUnit.Count },
     new() { MetricName = "OrderAmount", Value = (double)amount, Unit = StandardUnit.None }
    }
   }, ct);
  } catch (Exception ex) when (ex is not OperationCanceledException) {
   log.LogWarning(ex, "No se pudo publicar la métrica del pedido en CloudWatch"); // la venta ya está en RDS
  }
 }
}
```

**4. Usarlo al comprar** — [`Controllers/CheckoutController.cs`](Controllers/CheckoutController.cs), justo después de guardar el pedido en RDS:

```csharp
await metrics.OrderCreatedAsync(order.Total, HttpContext.RequestAborted);
```

#### ¿Y las credenciales? (no hay claves en el código)

| Dónde corre | De dónde saca los permisos el SDK |
|---|---|
| **EC2 en AWS** | Del **rol IAM** que crea el YAML: [`infra/urponline-oneclick.yaml`](infra/urponline-oneclick.yaml) le da `CloudWatchAgentServerPolicy` (incluye `cloudwatch:PutMetricData`) y activa las métricas con `Metrics__CloudWatchEnabled=true` y `AWS__Region` |
| **Tu PC** | De `aws configure` (`%USERPROFILE%\.aws\credentials`). Para probarlo: `$env:Metrics__CloudWatchEnabled="true"; $env:AWS__Region="us-east-2"; dotnet run --urls http://localhost:8080` |

#### Comprobar que funciona

1. Haz 2 o 3 compras en la tienda.
2. Dashboard → panel **"Tienda (AWS SDK): pedidos y ventas S/"** (pulsa ⟳ tras 1-2 min).
3. O con la CLI:

```bash
aws cloudwatch get-metric-statistics --namespace URPOnline --metric-name OrdersCreated \
  --start-time $(date -u -d '-1 hour' +%FT%TZ) --end-time $(date -u +%FT%TZ) \
  --period 3600 --statistics Sum --region us-east-2
```

### Borrar todo (¡obligatorio al terminar!)

- **Con GitHub Actions:** pestaña **Actions → Destruir AWS → Run workflow** → escribe `DESTRUIR`.
- **Con la CLI:**
  ```powershell
  aws cloudformation delete-stack --stack-name URPOnlineLab --region us-east-2
  aws cloudformation wait stack-delete-complete --stack-name URPOnlineLab --region us-east-2
  ```
  Si falla por el bucket de proveedores, vacíalo en la consola (S3 → bucket → **Empty**) y repite.
- Después: borra el **snapshot final de RDS** (RDS → Snapshots) y el bucket de artefactos (`aws s3 rb s3://$BUCKET --force`). Revisa **Billing** al día siguiente.

---

## 🚀 Descargar y correr la tienda en tu equipo

Requisitos: **.NET 8 SDK**, **Docker Desktop** abierto y **Git**. Cómo instalarlos: [CONFIGURACION_VSCODE.md](CONFIGURACION_VSCODE.md).

```powershell
# 1. Descargar el proyecto
git clone https://github.com/cortexgovernor/tiendaonline.git
cd tiendaonline

# 2. PostgreSQL con pgvector + Redis (Docker)
docker run -d --name urp-postgres -e POSTGRES_USER=urponline -e POSTGRES_PASSWORD=localonly -e POSTGRES_DB=urponline -p 5432:5432 pgvector/pgvector:pg16
docker run -d --name urp-redis -p 6379:6379 redis:7

# 3. Modelo de búsqueda semántica (118 MB, se descarga una vez)
powershell -ExecutionPolicy Bypass -File scripts/descargar-modelo.ps1

# 4. Arrancar la tienda (la primera vez crea las tablas y los productos de ejemplo)
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:InitializeDatabase = "true"
$env:Dashboard__AccessKey = "demo-dashboard"
$env:Admin__AccessKey = "demo-admin"
dotnet run --urls http://localhost:8080
```

5. Abre **http://localhost:8080**

### Librerías .NET (NuGet)

**No se instalan a mano**: están declaradas en `TiendaOnline.csproj` y `dotnet run` / `dotnet restore` las descarga solas.

| Paquete | Para qué |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core + PostgreSQL (RDS) |
| `Microsoft.EntityFrameworkCore.Design` | Crear y aplicar migraciones |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | Sesión y carrito en Redis (ElastiCache) |
| `AWSSDK.Extensions.NETCore.Setup`, `AWSSDK.S3`, `AWSSDK.SecretsManager`, `AWSSDK.CloudWatch` | **AWS SDK for .NET** |
| `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers` | Modelo de embeddings local (búsqueda semántica) |

### Base de datos: migraciones EF Core (tablas + pgvector + índice HNSW)

Las migraciones están en `Data/Migrations/`:

| Migración | Crea |
|---|---|
| `Inicial` | Tablas `Products`, `Orders`, `OrderItems`, `Promotions`, `BusinessEvents`, `SupplierImports` |
| `BusquedaVectorialPgvector` | Extensión `vector`, tabla `ProductEmbeddings` (`vector(384)`) e **índice HNSW** `vector_cosine_ops` |

Se aplican de **una** de estas dos formas:

```powershell
# A) Automática: al arrancar con InitializeDatabase=true (aplica migraciones + carga productos de ejemplo)
$env:InitializeDatabase = "true"; dotnet run --urls http://localhost:8080

# B) Manual, con la herramienta dotnet-ef (incluida en dotnet-tools.json del repo)
dotnet tool restore
dotnet tool run dotnet-ef database update
```

Comprueba en PostgreSQL:

```powershell
docker exec urp-postgres psql -U urponline -d urponline -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory";'
docker exec urp-postgres psql -U urponline -d urponline -c "SELECT indexname FROM pg_indexes WHERE tablename='ProductEmbeddings';"
```

Debe listar `20261010184951_Inicial`, `20261010185028_BusquedaVectorialPgvector` y el índice `IX_ProductEmbeddings_Embedding_hnsw`.

| Qué probar | Dónde |
|---|---|
| Tienda, carrito y compra | http://localhost:8080 |
| Búsqueda semántica (pgvector): busca `tomar fotos` o `reloj deportivo` → **Productos relacionados** | barra de búsqueda |
| Analítica | http://localhost:8080/Analytics?key=demo-dashboard |
| Promociones | http://localhost:8080/admin/promotions?key=demo-admin |

Otro día: `docker start urp-postgres urp-redis` y `dotnet run --urls http://localhost:8080` (ya sin `InitializeDatabase`).
¿Algo falla? Tabla de **Problemas frecuentes** en [CONFIGURACION_VSCODE.md](CONFIGURACION_VSCODE.md#5-problemas-frecuentes).

---

# URPOnline v2 — tienda C# + analítica + promociones + Worker AWS SDK

## Ejecutar localmente (sin Docker)
- Requiere .NET SDK 8, PostgreSQL y Redis (servicios instalados y ejecutándose).
- Crear base PostgreSQL `urponline` y usuario local.
- Configurar variables: `ConnectionStrings__Postgres`, `ConnectionStrings__Redis`, `InitializeDatabase=true` SOLO para instalación inicial.
- Ejecutar `dotnet restore` y `dotnet run --urls http://localhost:8080`.
- En local HTTPS usar `dotnet dev-certs https --trust` y perfiles HTTPS, o proxy TLS. La cookie `Secure` no viaja por HTTP plano.
- Dashboard: configurar `Dashboard__AccessKey` y abrir `/Analytics?key=...` para pruebas. Esta clave en querystring es SOLO para entorno de demostración; en despliegue real usar autenticación de usuarios/SSO y no URL secrets.
- Promociones: configurar `Admin__AccessKey` y abrir `/admin/promotions?key=...`. Reemplazar por autenticación real en producción.
- Carga proveedores: `dotnet run --project Worker -- --source local --file examples/products.csv`.
- Carga proveedores desde S3: configurar perfil AWS local (`aws configure sso` / `aws configure`) y ejecutar `dotnet run --project Worker -- --source s3 --bucket TU_BUCKET --key suppliers/products.csv`.
- AWS SDK permite usar credenciales IAM locales o rol de EC2; NO poner Access Key en repositorio.
- En EC2, configurar PostgreSQL vía PgBouncer y Redis ElastiCache; Nginx en puerto 80/Kestrel 8080.
- Pago real PayPal/Culqi/Mercado Pago NO implementado; checkout deja PENDIENTE_PAGO. Existe un endpoint de pago DEMO, DESHABILITADO por defecto.
- Los paneles y el modelo de eventos ilustran la analítica y necesitan controles empresariales de privacidad, autenticación y consentimiento antes de producción.
- PgBouncer y Redis deben ser altamente disponibles; template base no demuestra RTO/RPO por sí solo.
- NOTA: Sin acceso a dotnet SDK ni AWS aquí, compilación/despliegue final deben verificarse durante el laboratorio.
