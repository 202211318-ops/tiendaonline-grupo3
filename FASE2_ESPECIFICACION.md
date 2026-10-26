# Fase 2 — Agentes en la tienda (especificación)

> Rama `fase-2`. El laboratorio del 24 de octubre (`main`) está cerrado y no se modifica.
> Estado: los agentes (§2–§8) **están implementados**; URPOnline Delivery (§9) **está especificado y su base se construye según §9.9**. La rúbrica que se presenta el 7 de noviembre es la de §9.6. Cada punto marcado *(por defecto)* es una decisión que se toma así salvo que el profesor indique otra.

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
- **Prueba de carga (como en la fase 1):** la tormenta es la carga. El grupo ejecuta el generador de k6 desde CloudShell y mide cómo responde la arquitectura serverless: edad del mensaje más antiguo y mensajes en la DLQ de SQS; errores, duración y throttles de Lambda; conexiones a RDS (DatabaseConnections); y p95. Compara con y sin los controles (concurrencia reservada, tamaño de lote) y **interpreta**: dónde estuvo el cuello de botella, qué cambió con cada control y cuántos clientes simultáneos soporta. No se hace en clase: los resultados se traen.
- **Emulación de DoS (corta) e identificación:** además de la tormenta normal, el grupo lanza con k6 un **pico breve y agresivo** contra su propia infraestructura para validar que la soporta y que **su monitoreo lo identifica como un DoS** (lo distingue de la carga normal): un salto repentino de peticiones por segundo, errores 5xx o throttles, y p95 disparado. El monitoreo lo detecta, **alerta** (alarma de CloudWatch a ALARM y notificación por SNS) y el grupo observa **qué le pasa a la plataforma** bajo el DoS: si la tienda sigue respondiendo, qué se degrada y qué lo absorbe (Auto Scaling, SQS, concurrencia). El objetivo es **detectar, alertar y observar**, no atacar.

## 7. Escenario de intrusión: defensa proactiva (lo simula cada grupo)

El grupo **no ataca**: monitorea, detecta y responde. El profesor entrega un **emulador de intrusión inofensivo** (ya hecho) y **cada grupo lo ejecuta contra su propia infraestructura** en AWS, cuantas veces necesite, para comprobar y afinar su defensa; en la sustentación lo lanzan en vivo y la clase ve en tiempo real cómo la infraestructura alerta, actúa sola y señala dónde y por qué.

**El emulador es inofensivo** (no hay exploits reales; solo genera las señales que la defensa debe detectar): consume CPU (simula el minero), intenta conectarse a una IP reservada de documentación (`203.0.113.10:3333`) y contra el NAT, intenta leer el rol y las API keys **sin sacar nada de la cuenta**, y deja una marca visible en el frontend.

**Dos variantes de escenario:**
- **A — Troyano/minero:** basado en un incidente real, anónimo. Entra por una dependencia (paquete NuGet `URPOnline.Agentes.Conectores` que se presenta como "conectores de LLM"), consume CPU, intenta salir a la IP reservada e intenta leer las keys.
- **B — Mythos:** misma base inofensiva, con un indicador de "descifrado cuántico". **No rompe ningún cifrado**: solo simula la anomalía (una conexión con certificado/TLS anómalo o un intento de degradar la conexión) que la detección debe cazar. La teoría —"cosechar ahora, descifrar después"; RSA/ECC (TLS, SSH, certificados) como blanco teórico; AES-256 y SHA-256 resisten— va en la sustentación, no en el código.

**Defensa proactiva (montada ANTES del ataque):** Security Groups y NACL al mínimo; rol de la EC2 con permisos mínimos; keys solo en Secrets Manager; IMDSv2 obligatorio; monitoreo, alarmas y respuesta automática ya configurados.

**Lo que se ve en tiempo real al lanzarlo:** la alarma de CloudWatch pasa a ALARM; VPC Flow Logs marca el intento de salida a la IP reservada y contra el NAT; la respuesta automática corta la salida (regla de salida / NACL), aísla el frontend y lo reemplaza por Auto Scaling, y SNS notifica. Nadie toca nada a mano.

**Dónde y por qué (lo señala la propia infraestructura):** Flow Logs y los logs del ALB señalan el frontend como punto de entrada; Reachability Analyzer muestra qué bloqueó el camino hacia el NAT (NACL o Security Group) sin enviar tráfico real; CloudTrail y el diagnóstico del rol muestran qué permisos se intentaron usar y cuáles sobraban.

**Blindaje del pipeline (variante A):** `packages.lock.json` en modo bloqueado, `nuget.config` con fuentes permitidas, `dotnet list package --vulnerable` que frena el despliegue, salida a internet solo hacia lo necesario.

## 8. Datos personales: qué se valida

No es una validación legal: son cuatro reglas técnicas que se comprueban en la tienda y en AWS.

- Correos **solo a clientes de prueba** con los correos verificados de los propios alumnos (SES en modo sandbox) *(por defecto)*.
- El identificador de sesión nunca se registra tal cual (hash).
- El agente de soporte nunca revela datos de otros clientes.
- En los logs de CloudWatch no aparecen datos personales (correos, tarjetas, celulares ni nombres de clientes). Lo comprueba el verificador automático `scripts/verificadores/verificar_datos_personales.py`, que responde CUMPLE o NO CUMPLE.

## 9. URPOnline Delivery: dos apps móviles con base de datos local

URPOnline abrió un negocio de delivery de comida con cientos de repartidores suscritos. Hay dos apps
React Native (Expo) que comparten un solo backend serverless:

| App | Qué hace | Datos locales en el celular |
|---|---|---|
| **Repartidor** | Recibe pedidos cercanos, acepta uno, marca recogido / entregado y envía su posición GPS | SQLite: pedidos ofrecidos, pedido en curso, cola de cambios pendientes, posiciones no enviadas |
| **Cliente** | Hace el pedido y ve en el mapa cómo llega el repartidor | SQLite: sus pedidos, la última posición conocida del repartidor |

### 9.1 URPOnline también vende comida

La comida se vende en una pestaña propia, **🍔 Comida**, separada de las categorías de productos:

| Dónde | Qué ve el cliente | A dónde va el pedido |
|---|---|---|
| **Tienda web** (barra de categorías, junto a «☰ Todo») | Restaurantes → carta → pedir para delivery | `POST /v1/pedidos` → Lambda `RecibirPedido` |
| **App cliente** (pestañas: **Tienda** · **Comida** · **Mis pedidos**) | Lo mismo; en «Mis pedidos», el mapa con el repartidor llegando | Igual: `POST /v1/pedidos` → `RecibirPedido` (desde la cola offline si no hay señal) |

Un pedido de comida, se haga desde la web o desde el celular, entra por la misma Lambda y sigue el mismo
flujo de entrega (§9.2). Los productos de la tienda siguen con su carrito y checkout actuales, sin cambios.
Datos nuevos en RDS: `restaurantes`, `platos` (con precio, disponibilidad, ingredientes y alérgenos) y `pedidos_comida`.

### 9.2 Flujo de pedidos y entregas: API Manager → Lambda → SQS → Lambda → RDS

| Paso | Entrada | Cola | Lambda que consume | Por qué esa cola |
|---|---|---|---|---|
| 1. Recibir pedido | `POST /v1/pedidos` → `RecibirPedido` | `pedidos-recibidos` (estándar + DLQ) | `OfrecerPedido` | Absorbe picos de pedidos a la hora del almuerzo |
| 2. Nadie acepta | `OfrecerPedido` reencola con `DelaySeconds=120` | `pedidos-reofrecer` (estándar, con retraso) | `ReofrecerPedido` | El temporizador es la propia cola |
| 3. Aceptar | `POST /v1/pedidos/{id}/aceptar` → `AceptarPedido` | **ninguna, síncrono** | — | El repartidor necesita saber en el momento si ganó; una cola no le da esa respuesta |
| 4. Recogido / entregado / cola offline | `POST /v1/sincronizar` → `Sincronizar` | `cambios-entrega.fifo` (grupo = pedidoId, dedup = clave de idempotencia) | `ActualizarEntrega` | Orden por pedido + duplicados descartados por SQS |
| 5. Posición GPS | `POST /v1/posiciones` | `posiciones.fifo` (grupo = repartidorId) | `RegistrarPosicion` → **Redis** (última posición) → WebSocket al cliente | Orden por repartidor sin bloquear a los demás; Redis evita leer RDS con cada GPS |
| — | Mensaje que falla 3 veces | `*-dlq` + alarma CloudWatch | — | No frena al resto |

Estados del pedido: `recibido → ofrecido → aceptado → recogido → entregado` (y `sin_repartidor`, `cancelado`).
Una transición inválida se rechaza y se registra en CloudWatch. Los eventos `PedidoRecibido`, `PedidoAceptado`
y `PedidoEntregado` se publican en el bus de EventBridge de la §2. El mapa usa Amazon Location Service y la
posición llega al cliente por API Gateway WebSocket *(por defecto)*. RDS se accede a través de RDS Proxy.

### 9.3 API Manager con contratos

El grupo construye la capa de gestión de APIs sobre API Gateway, todo en CloudFormation:

- **Contrato primero:** `contratos/delivery-v1.openapi.yaml` (OpenAPI 3) es la fuente de verdad; API Gateway se
  genera desde él y valida cada request contra el contrato antes de llegar a Lambda.
- **Consumidores con su clave y su plan:** app repartidor, app cliente, tienda web y un restaurante socio;
  cada uno con su API key, plan de uso y límite de solicitudes.
- **Autenticación con Amazon Cognito:** las dos apps inician sesión contra un User Pool (grupos `repartidor` y `cliente`);
  API Gateway valida el token antes de llegar a Lambda y cada ruta exige su grupo. El grupo construye el flujo de
  inicio de sesión, la renovación del token y su manejo sin señal.
- **Versiones:** `/v1` y `/v2` conviven; un cambio que rompe el contrato va a `/v2`, nunca a `/v1`.
- **Pruebas de contrato en GitHub Actions:** si un cambio rompe el contrato, el despliegue se detiene.

### 9.4 Coseno + Jaccard y RAG en el delivery

El coseno ya se usa en la tienda (`ProductVectorStore`, `SlaSectionStore`). En el delivery se le suma Jaccard:

| Lugar | Coseno (pgvector) | Jaccard | Por qué los dos |
|---|---|---|---|
| **Búsqueda de platos** | Encuentra platos por significado («algo picante con pollo») | Ordena por ingredientes compartidos (intersección / unión de conjuntos) | El coseno trae lo parecido en significado; Jaccard premia lo que coincide exactamente. **Ninguno entiende la negación**: por eso primero se **reescribe la pregunta** con el modelo que el grupo elija (Gemini, Gemma, Nemotron, Llama o DeepSeek) en `{buscar, incluir, excluir}`; el coseno usa solo `buscar` y Jaccard descarta todo plato cuyo Jaccard con `excluir` sea mayor que 0 |
| **RAG de soporte del delivery** («llegó frío», «¿me reembolsan?», «¿tiene gluten?») | Recupera la sección de la política de delivery o de la carta del restaurante | Verifica la respuesta: Jaccard entre lo respondido y la sección citada; si es bajo, no se publica | El coseno encuentra el texto, pero no garantiza que el modelo lo haya usado |

«¿Dónde está mi pedido?» **no es RAG**: es un dato exacto y se responde con una consulta a RDS.

### 9.5 Qué entrega el curso y qué construye el grupo

**Lo entrega el curso (funciona, pero solo el camino feliz):** pila CloudFormation base, las dos apps React Native
con sus pantallas, la pestaña Comida, el contrato `delivery-v1.openapi.yaml`, las Lambdas del flujo
pedido → ofrecer → aceptar → entregar con sus colas, el User Pool de Cognito en CloudFormation, y el simulador de repartidores.
Sin modo offline, sin control de duplicados, sin versiones, sin tableros, sin trazas y sin Jaccard.

**Lo construye el grupo:** los 20 desafíos. Cada uno se **construye**, se **demuestra en vivo**, se **explica** y se **sustenta**.

**Cómo se les ayuda:**
- Cada desafío trae su pista («Cómo empezar»): qué servicio o técnica usar y el primer paso concreto.
- Un ejemplo resuelto por bloque, parecido pero no igual a un desafío.
- Consultas en GitHub Discussions del repositorio del curso; las respuestas quedan para todos los grupos.
- Una prueba de humo por bloque que dice CUMPLE / NO CUMPLE antes de presentar. La explicación sigue siendo de ellos.

### 9.6 Rúbrica: 20 desafíos · 4 bloques de 5 puntos · todo o nada

Si uno de los cinco desafíos de un bloque falla o no se sabe explicar, el bloque vale 0.

#### Bloque A — Pedidos y entrega con Lambda + SQS (5 pts)
| # | El grupo construye | Demuestra en vivo | Explica | Cómo empezar |
|---|---|---|---|---|
| A1 | Que el pedido de la pestaña Comida (web y app) entre por `pedidos-recibidos` | 50 pedidos simultáneos y ninguno se pierde | Por qué una cola y no llamar directo a `OfrecerPedido` | `SendMessage` desde `RecibirPedido`; *event source mapping* con `ReportBatchItemFailures` |
| A2 | La regla para ofrecer el pedido a los repartidores cercanos | Quién recibió la oferta y quién no | Su criterio de cercanía y qué pasa en una zona sin repartidores | PostGIS en RDS (`CREATE EXTENSION postgis`) y `ST_DWithin` sobre `geography`; empezar con 2 km |
| A3 | La máquina de estados del pedido | `entregado` antes de `recogido` se rechaza | Qué transiciones permite y por qué | Tabla de transiciones; `UPDATE … WHERE id=$1 AND estado='recogido'`: 0 filas = inválida |
| A4 | El reofrecimiento cuando nadie acepta | El radio se amplía y, tras N intentos, el cliente ve «sin repartidor» | Retraso de cola vs. Scheduler: cuándo usar cada uno | `SendMessage` con `DelaySeconds` (máx. 900 s); el número de intento va en el mensaje |
| A5 | La DLQ y su alarma | Un mensaje venenoso termina en la DLQ sin frenar al resto | Cómo lo reprocesan y qué pasa si lo reprocesan dos veces | `RedrivePolicy` con `maxReceiveCount: 3`; alarma sobre la DLQ; `aws sqs start-message-move-task` |

#### Bloque B — Sincronización con la base local del celular (5 pts)
| # | El grupo construye | Demuestra en vivo | Explica | Cómo empezar |
|---|---|---|---|---|
| B1 | La cola local (SQLite) de cambios pendientes | Modo avión → «recogido» → vuelve la red → llega a RDS | Qué pasa si la app se cierra antes de sincronizar | Tabla `cambios_pendientes` en `expo-sqlite`; `@react-native-community/netinfo` detecta la vuelta de la red |
| B2 | La aceptación con escritura condicional | Dos celulares aceptan a la vez y solo gana uno | Por qué aceptar no pasa por una cola | `UPDATE pedidos SET repartidor_id=$1, estado='aceptado', version=version+1 WHERE id=$2 AND estado='ofrecido'`; 0 filas → 409 |
| B3 | La idempotencia (clave + FIFO) | El mismo cambio enviado 2 veces deja una sola fila | La ventana de deduplicación de FIFO y qué pasa fuera de ella | UUID por cambio como `MessageDeduplicationId` y clave única en `cambios_aplicados` (FIFO deduplica solo 5 min) |
| B4 | La sincronización incremental con borrados | La app baja solo lo que cambió, incluidos los cancelados | Por qué un borrado necesita marca y no desaparecer | Columnas `actualizado_en` (hora del servidor) y `borrado`; `GET /v1/sincronizar?desde=<marca>` |
| B5 | Su regla de conflictos | El pedido se canceló mientras el repartidor estaba offline | Por qué eligieron esa regla y qué pierde | Comparar gana el servidor / gana el último / regla de negocio sobre este mismo caso |

#### Bloque C — API Manager, contratos y búsqueda híbrida (5 pts)
| # | El grupo construye | Demuestra en vivo | Explica | Cómo empezar |
|---|---|---|---|---|
| C1 | La validación desde el OpenAPI en API Gateway | Un request inválido es rechazado sin llegar a Lambda | Qué ahorra validar en la puerta | OpenAPI en `AWS::ApiGateway::RestApi` (`Body`) con `x-amazon-apigateway-request-validators` |
| C2 | Inicio de sesión con **Amazon Cognito** (User Pool con grupos `repartidor` y `cliente`) en las dos apps, autorizador Cognito en API Gateway, y además los cuatro consumidores con su clave, su plan y su límite | Un cliente con sesión iniciada intenta `POST /v1/pedidos/{id}/aceptar` → 403; un repartidor sí puede; un token vencido → 401; un consumidor se pasa del límite → 429 y los demás siguen | Autenticación frente a autorización; por qué la app usa un cliente público sin secreto; qué hace la app cuando el token vence sin señal | `AWS::Cognito::UserPool` + `UserPoolClient` sin secreto (SRP) — se reusa el de `infra/fase3-contenedores.yaml`; `AWS::ApiGateway::Authorizer` con `Type: COGNITO_USER_POOLS`; `ApiKey`, `UsagePlan` y `UsagePlanKey` |
| C3 | La `/v2` con un cambio que rompe el contrato | La app vieja sigue en `/v1` sin fallar | Qué cambio rompe un contrato y cuál no | Copiar a `delivery-v2.openapi.yaml` y renombrar un campo |
| C4 | La prueba de contrato en GitHub Actions | Un despliegue que rompe el contrato se detiene | Por qué en el pipeline y no en producción | `oasdiff breaking` entre el contrato publicado y el nuevo; si encuentra algo, el job falla |
| C5 | Búsqueda de platos híbrida: reescritura de la pregunta con **varios modelos**: Gemini 2.0 Flash, Gemma 2 9B, Nemotron 70B, Llama 3.3 70B y DeepSeek V3 (cada grupo saca sus propias API keys y las carga en `/admin/modelos`; los límites de cada proveedor los resuelve el grupo) → coseno sobre lo que se busca + Jaccard sobre los ingredientes a incluir y a excluir | **Primero validan la falla de la base:** la búsqueda solo por coseno devuelve «Pollo kung pao picante» y «Pollo picante con salsa de maní» en los primeros lugares para «pollo picante sin maní» (medido en AWS: coseno 0,671 y 0,641). Después, con su juego de 20 búsquedas de prueba (con resultado esperado), muestran **ajustando** el peso α en `puntaje = α·coseno + (1−α)·Jaccard` y los umbrales, la tabla de aciertos de cada ajuste, y que «pollo picante sin maní» no trae ningún plato con maní; y la misma batería de búsquedas corrida con cada modelo: aciertos, negaciones perdidas y tiempo de respuesta por modelo | **Qué ajuste les funcionó mejor y por qué**, qué búsquedas siguen fallando, qué pasa si la reescritura usa un sinónimo que no está en el catálogo, y **qué modelo reescribió mejor**, cuál perdió negaciones, y cómo resolvieron los límites y el costo de cada proveedor | La reescritura devuelve JSON `{buscar, incluir, excluir}` usando los ingredientes de la tabla `platos`; el embedding de `buscar` y de los platos con el mismo MiniLM de `EmbeddingService`; Jaccard en SQL: `cardinality(ARRAY(SELECT unnest(a) INTERSECT SELECT unnest(b)))::float / cardinality(ARRAY(SELECT unnest(a) UNION SELECT unnest(b)))`; medir aciertos en los 5 primeros resultados |

#### Bloque D — Ver todo el flujo y soporte con RAG (5 pts)
| # | El grupo construye | Demuestra en vivo | Explica | Cómo empezar |
|---|---|---|---|---|
| D1 | El rastreo de punta a punta con un id de correlación | Un pedido desde la app hasta la entrega, en una sola traza | Cómo viaja el id a través de las colas | X-Ray activo en API Gateway y en cada Lambda; `correlationId` como atributo SQS y en cada log |
| D2 | El tablero CloudWatch: pedidos por estado, tiempo por etapa, profundidad y edad de las colas, DLQ | El tablero durante una corrida del simulador | Qué métrica avisa primero de un problema | `AWS::CloudWatch::Dashboard`; *Embedded Metric Format*; `ApproximateAgeOfOldestMessage` |
| D3 | Una torre de control web: mapa en vivo con todos los repartidores y pedidos, y el mapa del cliente | Cientos de repartidores del simulador moviéndose | Cómo llegan las posiciones sin saturar | API Gateway WebSocket + Amazon Location Service; una posición por repartidor cada N segundos |
| D4 | El soporte del delivery con **enrutador** (pedido → SQL, política → RAG verificado con Jaccard, queja → persona), **humano en el ciclo** (reembolso mayor a un monto espera aprobación en `/admin`) y **la traza del agente encendida**: cada vuelta registrada con la ruta elegida, el `motivo` que devolvió el modelo en su JSON, la herramienta llamada, su resultado y la condición que detuvo el loop | Tres mensajes, cada uno por su camino; una pregunta sin respuesta en la política → «no lo sé»; un reembolso alto queda en espera hasta que lo aprueban; **con la traza en pantalla explican paso a paso por qué el agente mandó cada mensaje por su camino, y muestran un caso en que se equivocó** | Cómo decide el enrutador y qué hace si se equivoca de camino; **qué parte de la decisión es del modelo y qué parte es del código, y qué cambiarían para que no se equivoque**; cómo eligieron el umbral de Jaccard; por qué ese monto requiere a una persona | Reusar `SlaSectionStore`; el enrutador devuelve JSON `{ruta, motivo}` validado con esquema; la traza sigue el patrón de `StockAgent.cs` (pensamiento → acción → observación, con el motivo de parada de `StopPolicy`) y cada vuelta se escribe en CloudWatch Logs en JSON con `pedidoId`, para consultarla en Logs Insights; la aprobación es un estado `reembolso_pendiente` en RDS |
| D5 | El diagnóstico de la causa oculta que el profesor inyecta a su grupo | La encuentran usando solo sus propias herramientas (D1 a D3) | Qué vieron, en qué orden y por qué era esa la causa | Tablero (¿qué cola crece?) → traza (¿qué paso es lento?) → log (¿qué error?) |

### 9.7 La presentación: la aplicación en vivo, de punta a punta (30 minutos)

**Todo es en vivo:** nada pregrabado, ni capturas, ni diapositivas. Se presenta la **aplicación funcionando en AWS** mientras el profesor mira el
pipeline completo. Durante los 30 minutos quedan **tres pantallas a la vista**:

1. **Los celulares** con las apps, proyectados.
2. **AWS funcionando:** la consola con los servicios del grupo en marcha (API Gateway, Lambdas, colas SQS y sus DLQ,
   RDS, Cognito), mostrando en vivo lo que entra y sale de cada uno.
3. **AWS monitoreado:** el **tablero de CloudWatch (D2)** con sus alarmas y la traza de X-Ray; en cada momento se
   muestran sus métricas, y si una alarma se dispara se ve en el momento. Cada momento del guion demuestra desafíos de la rúbrica; si un momento falla, cae su bloque.

| Min. | Momento en vivo | Qué se ve en pantalla | Métricas en el tablero | Desafíos |
|---|---|---|---|---|
| 0–3 | **Instalan la app en vivo.** En un celular sin la app, la instalan por la vía que eligió el grupo (§9.8) e inician sesión | La descarga, la instalación y el primer inicio de sesión | — (si no se instala, no hay presentación) | C2 |
| 3–8 | **La búsqueda híbrida, en vivo.** En la pestaña Comida, «pollo picante sin maní» y **una búsqueda que propone el profesor en el momento** | La pregunta reescrita en JSON `{buscar, incluir, excluir}`; por cada plato su coseno, su Jaccard y el puntaje combinado; **cambian α en vivo** y el orden cambia; repiten con **otro modelo** de reescritura; ningún plato con maní | Tiempo de reescritura por modelo; reescrituras inválidas; aciertos en los 5 primeros | C5 |
| 8–10 | **El cliente pide.** Elige un plato de esa búsqueda y hace el pedido | El pedido entra a `pedidos-recibidos` | Solicitudes por consumidor y 4xx/429 de API Gateway; mensajes en `pedidos-recibidos` | A1, C1, C2 |
| 10–13 | **Los repartidores compiten.** Dos celulares reciben la oferta y aceptan a la vez | Solo uno gana; el otro ve «ya fue tomado» | Aceptaciones ganadas y rechazadas (409); pedidos por estado | A2, B2, A3 |
| 13–15 | **Se va la señal.** El repartidor ganador pasa a modo avión, marca «recogido» y vuelve la red | El cambio llega a RDS una sola vez | Cambios recibidos frente a aplicados (duplicados descartados); edad del mensaje más viejo de `cambios-entrega.fifo` | B1, B3, B4 |
| 15–19 | **La moto llega, de verdad.** Un integrante sale con el celular del repartidor, **camina por el patio de la universidad y regresa**; el cliente sigue la **moto** en su mapa con el GPS real. En la torre de control, además, el simulador mueve cientos de repartidores | La moto avanza en el mapa del cliente al ritmo del integrante, sin saltar hacia atrás, y vuelve al punto de entrega; la hora estimada de llegada se recalcula | Posiciones por segundo, posiciones descartadas por viejas, concurrencia y throttling de Lambda | D3, A4 |
| 19–21 | **Entrega y reclamo.** Se entrega; el cliente escribe al soporte «llegó frío, quiero mi dinero» | El enrutador lo manda al RAG; el reembolso alto queda esperando aprobación; **la traza del agente en pantalla** y un integrante explica por qué tomó cada camino | Mensajes por ruta del enrutador; respuestas rechazadas por Jaccard; reembolsos pendientes | A3, D4, B5 |
| 21–25 | **El pipeline por dentro.** El mismo pedido en la traza de punta a punta | Cada Lambda, cada cola, la DLQ, los tiempos por etapa | Tiempo por etapa del pedido; mensajes en las DLQ y su alarma | D1, D2, A5, C3, C4 |
| 25–30 | **La falla.** El profesor inyecta la causa oculta del grupo con semilla nueva; el grupo la diagnostica en vivo | Qué vieron, en qué orden y por qué era esa la causa | La métrica que avisó primero y cómo cambió el resto | D5 |

Durante el guion el profesor detiene la demostración en cualquier momento y elige a qué integrante preguntarle;
participan todos. Si no lo sabe sustentar, el desafío no cuenta y su bloque vale 0. Todo corre en la cuenta AWS
del grupo, desplegado con CloudFormation por GitHub Actions; lo hecho a mano no cuenta.

**Video obligatorio.** Se entrega el día anterior a la presentación. Recorre **los 20 desafíos, uno por uno**, dentro
del mismo guion completo: el pedido, el delivery, el pipeline por dentro, las métricas y el tablero. **Participan todos
los integrantes, cada uno con la cámara encendida** mientras explica su parte. Sin video no se presenta.

### 9.8 Cómo se instalan las apps (sin costo)

Cada grupo elige una o varias de estas opciones gratuitas:

| Opción | Android | iPhone | Qué es | Ojo con |
|---|---|---|---|---|
| **Expo Go** | ✅ | ✅ | Se escanea el QR del proyecto y la app corre | No es una instalación real; sirve para desarrollar y para la demostración |
| **EAS Build** (Expo, distribución interna) | ✅ APK por enlace | ❌ | Compila el instalable real y lo comparte por un enlace | El plan gratuito tiene un cupo de compilaciones al mes |
| **Firebase App Distribution** | ✅ | ❌ | Reparte el APK a testers invitados por correo, con versiones y avisos | No es una tienda pública |
| **Samsung Galaxy Store** | ✅ | ❌ | Tienda pública; la cuenta de vendedor es gratis | Revisión de varios días |
| **Huawei AppGallery** | ✅ | ❌ | Tienda pública; el registro de desarrollador es gratis | Revisión lenta y verificación de identidad |
| **GitHub Releases** | ✅ | ❌ | El APK publicado en el repositorio del grupo | Se instala permitiendo «orígenes desconocidos» |
| **AWS Device Farm** (prueba gratuita) | ✅ | ✅ | No es una tienda: prueba la app en celulares reales en la nube | Los minutos gratuitos se acaban; después cobra por minuto |

No son gratuitas y no se exigen: Google Play (cuenta de 25 USD) y la instalación real en iPhone con TestFlight
(cuenta de Apple de 99 USD al año). Para la presentación, los dos celulares del momento «los repartidores
compiten» y el del cliente tienen la app instalada por alguna de estas vías, y **al menos una instalación se hace
en vivo** al empezar (§9.7, minuto 0–3); el grupo explica por qué eligió la suya.

### 9.9 Especificación técnica de la base que entrega el curso

La base implementa **solo el camino feliz** (pedido → ofrecer → aceptar → recoger → entregar). Lo que la rúbrica
pide (offline, idempotencia, versiones, tableros, Jaccard, etc.) lo construye el grupo encima.

#### 9.9.1 Pila CloudFormation `infra/fase2-delivery.yaml`

Recibe de la pila `URPOnlineLab` la VPC, las subredes privadas de aplicación, el security group de RDS y el secreto de
la base. Todo se despliega por GitHub Actions.

| Recurso | Configuración base |
|---|---|
| **Cognito** | `AWS::Cognito::UserPool` (correo como usuario) con grupos `repartidor` y `cliente`; `UserPoolClient` público sin secreto (SRP y refresco de token) — se parte del de `infra/fase3-contenedores.yaml` de la rama `fase-3` anterior |
| **API Gateway REST** | Generada desde `contratos/delivery-v1.openapi.yaml` (`Body`), etapa `prod`, autorizador `COGNITO_USER_POOLS`, `ApiKeySourceType: HEADER`; un plan de uso con su clave por consumidor (app repartidor, app cliente, tienda web, restaurante socio) |
| **API Gateway WebSocket** | Rutas `$connect` (valida el token de Cognito), `$disconnect` y `seguir` (el cliente se suscribe a su pedido) |
| **SQS** | `pedidos-recibidos` (estándar), `pedidos-reofrecer` (estándar, el retraso lo pone el mensaje), `cambios-entrega.fifo`, `posiciones.fifo`; cada una con su DLQ (`maxReceiveCount: 3`) |
| **Lambda** (C# .NET 8, `lambdas-delivery/`) | Las de §9.9.2, en las subredes privadas, `Tracing: Active`, concurrencia reservada configurable |
| **RDS Proxy (opcional)** | Parámetro `UsarRdsProxy` (por defecto `false`): **no está disponible en las cuentas del plan gratuito de AWS** («This feature isn't available with free plan accounts», medido el 2026-10-26). Sin proxy, cada Lambda abre como máximo 5 conexiones directas a RDS |
| **EventBridge** | Reglas en el bus `urponline` de la §2 para `PedidoRecibido`, `PedidoAceptado`, `PedidoEntregado` |
| **Amazon Location Service** | Un mapa (estilo vectorial) y una API key de Location restringida a ese mapa, para la app cliente y la torre de control |
| **CloudWatch** | Grupos de logs con retención de 7 días; el tablero lo construye el grupo (D2) |

#### 9.9.2 Lambdas de la base

| Lambda | Disparador | Hace (camino feliz) |
|---|---|---|
| `RecibirPedido` | `POST /v1/pedidos` | Valida contra el contrato, guarda el pedido en `recibido`, envía a `pedidos-recibidos`, publica `PedidoRecibido` |
| `OfrecerPedido` | `pedidos-recibidos` | Busca repartidores `disponibles` (radio fijo), crea las ofertas, pasa el pedido a `ofrecido`, programa `pedidos-reofrecer` |
| `ReofrecerPedido` | `pedidos-reofrecer` | Si sigue `ofrecido`, vuelve a ofrecer (la base no amplía el radio: eso es A4) |
| `AceptarPedido` | `POST /v1/pedidos/{id}/aceptar` | Asigna el repartidor y pasa a `aceptado` (la base no controla la carrera: eso es B2) |
| `Sincronizar` | `POST /v1/sincronizar` | Recibe los cambios del celular y los envía a `cambios-entrega.fifo`; `GET` devuelve el estado actual completo (lo incremental es B4) |
| `ActualizarEntrega` | `cambios-entrega.fifo` | Aplica `recogido` y `entregado` |
| `RegistrarPosicion` | `posiciones.fifo` | Guarda la **última posición de cada repartidor en Redis** (el ElastiCache de la pila base; el mapa lee de ahí, no de RDS), deja el historial en RDS y la envía por WebSocket a los clientes que siguen ese pedido |
| `ConexionWs` | `$connect`, `$disconnect`, `seguir` | Registra y borra conexiones en `conexiones_ws` |
| `Simulador` | Invocación manual (`semilla`, `repartidores`, `causa`) | Mueve N repartidores falsos por rutas de Lima e inyecta la causa oculta del grupo |

La **búsqueda de platos** y el **soporte** no son Lambdas: API Gateway reenvía `/v1/platos/buscar` y `/v1/soporte`
a la tienda (`HTTP_PROXY` hacia el ALB), que ya tiene `EmbeddingService` (MiniLM), pgvector y `SlaSectionStore`.
La base busca solo por coseno; la reescritura y Jaccard son C5.

#### 9.9.3 Contrato `contratos/delivery-v1.openapi.yaml`

| Método y ruta | Grupo de Cognito | Hace |
|---|---|---|
| `GET /v1/restaurantes` · `GET /v1/restaurantes/{id}/platos` | cliente | Restaurantes y su carta |
| `GET /v1/platos/buscar?q=` | cliente | Búsqueda de platos |
| `POST /v1/pedidos` · `GET /v1/pedidos/{id}` | cliente | Crear y consultar un pedido |
| `GET /v1/ofertas` | repartidor | Ofertas vigentes para el repartidor |
| `POST /v1/pedidos/{id}/aceptar` | repartidor | Aceptar una oferta |
| `POST /v1/sincronizar` · `GET /v1/sincronizar` | repartidor | Cola de cambios del celular y estado actual |
| `POST /v1/posiciones` | repartidor | Posición GPS (lat, lon, hora del dispositivo, secuencia) |
| `POST /v1/soporte` | cliente | Mensaje al soporte del delivery |

Cada respuesta de error usa `application/problem+json`.

#### 9.9.4 Tablas nuevas en RDS (migración EF Core de la tienda)

| Tabla | Columnas principales |
|---|---|
| `restaurantes` | `id`, `nombre`, `lat`, `lon` |
| `platos` | `id`, `restaurante_id`, `nombre`, `descripcion`, `precio numeric(10,2)`, `disponible`, `ingredientes text[]`, `alergenos text[]`, `embedding vector(384)` con índice HNSW `vector_cosine_ops` |
| `repartidores` | `id`, `cognito_sub`, `nombre`, `disponible`, `lat`, `lon`, `actualizado_en` |
| `pedidos_comida` | `id`, `cliente_sub`, `restaurante_id`, `repartidor_id`, `estado`, `version`, `total numeric(10,2)`, `creado_en`, `actualizado_en`, `borrado` |
| `pedido_items` | `pedido_id`, `plato_id`, `cantidad`, `precio_unitario` |
| `ofertas` | `pedido_id`, `repartidor_id`, `intento`, `creada_en` |
| `posiciones` | `repartidor_id`, `pedido_id`, `secuencia`, `lat`, `lon`, `hora_dispositivo`, `hora_servidor` |
| `conexiones_ws` | `connection_id`, `pedido_id`, `cliente_sub`, `conectado_en` |
| `reembolsos` | `id`, `pedido_id`, `monto`, `estado`, `aprobado_por` |

Extensión: `vector` (ya está). La ubicación va en `lat`/`lon` (double precision): la base local no trae PostGIS, así que activarlo es parte del desafío A2. Tabla extra: `politica_delivery` (secciones de la política del soporte, con embedding). Los datos de restaurantes y platos son de prueba y se cargan solo con la
tarea de semilla del laboratorio. La tabla `cambios_aplicados` (B3) la crea el grupo.

#### 9.9.5 Apps React Native (Expo) `mobile-delivery/`

| App | Pantallas de la base | Librerías |
|---|---|---|
| `repartidor/` | Inicio de sesión, ofertas, pedido en curso (recogido / entregado), envío de GPS cada N segundos | `expo-location`, `expo-sqlite`, `amazon-cognito-identity-js` |
| `cliente/` | Inicio de sesión, pestañas **Tienda** · **Comida** · **Mis pedidos** · **Soporte**, mapa con la moto por WebSocket | `react-native-maps` (mapa por defecto del celular; usar Amazon Location Service es opcional), `expo-sqlite`, `amazon-cognito-identity-js` |

La base guarda en SQLite solo lo que muestra; la cola de cambios pendientes y la detección de red (B1) las construye el grupo.
**Expo SDK 58** (Expo Go solo abre la versión vigente). Las dos apps pasan TypeScript y se empaquetan con Metro para Android; `amazon-cognito-identity-js` necesita `react-native-get-random-values` y `@react-native-async-storage/async-storage`, que vienen en Expo Go. **A verificar en un celular real:** que `amazon-cognito-identity-js` (SRP) y las teselas de Location en
`react-native-maps` funcionen dentro de **Expo Go**; si no, la base usa un *development build* de EAS.

#### 9.9.6 Torre de control, pestaña Comida y verificación

- **Pestaña 🍔 Comida** en la tienda web (`Controllers/ComidaController.cs`, `Views/Comida/`), junto a «☰ Todo»: restaurantes, carta y búsqueda por coseno. Pedir desde la web por la cola es el desafío A1; la torre de control es el desafío D3.
- **Pruebas de humo** `scripts/delivery/humo_A.sh` … `humo_D.sh`: cada una dice CUMPLE / NO CUMPLE sobre la cuenta del grupo.
- **Calificador automático** `tests/CalificadorDelivery.cs` (como `CalificadorFase2.cs`): ~50 corridas con semilla, duplicados, carreras de aceptación, mensajes desordenados y cortes de red inyectados; cumple solo si todas cumplen.
- **Costo:** se mide en la cuenta del curso antes de entregar y se publica por servicio; hasta entonces no se afirma ninguna cifra.

## 10. Servicios AWS que se usan

EventBridge (bus y Scheduler), SQS con cola de mensajes fallidos, Lambda, SES, SNS, Secrets Manager, RDS PostgreSQL con pgvector, CloudWatch (métricas, logs y alarmas), VPC Flow Logs, Systems Manager, IAM.
Delivery (§9): API Gateway (REST con claves, planes de uso y WebSocket), SQS FIFO, RDS Proxy, PostGIS, Amazon Location Service, X-Ray.

## 11. Para un laboratorio posterior

- **Mythos traspasó la DMZ con descifrado cuántico** (detectar, contener, medir el daño, rediseñar; criptografía post-cuántica). *(por defecto, posterior)*
- Escenarios de caos: partición de red, cerebro dividido, falla gris, falla en cascada, avalancha de caché, caída de una dependencia externa o de una región.

## 12. Orden de construcción propuesto

1. Eventos de la tienda (compras, cancelaciones con motivo, señales) → EventBridge → SQS, con su infraestructura en el YAML y pruebas.
2. Ventana `/admin/modelos` y cliente LLM con keys en Secrets Manager.
3. RAG estructural de los SLA y agente 3 con guardrails.
4. Agentes 2 y 4, y el generador del escenario 1.
5. Agente 1.
6. Paquete NuGet del escenario de cadena de suministro.
7. Rúbrica y banco de preguntas de la fase 2.
