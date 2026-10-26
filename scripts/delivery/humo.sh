#!/usr/bin/env bash
# Pruebas de humo del delivery (fase 2, FASE2_ESPECIFICACION.md §9.5): dicen CUMPLE / NO CUMPLE por desafío,
# contra la cuenta AWS del grupo, ANTES de presentar. No reemplazan la explicación: el grupo la sustenta en vivo.
# Sobre la BASE del curso varios desafíos dan NO CUMPLE a propósito: son lo que el grupo tiene que construir.
#
# Uso (CloudShell o Git Bash, con la AWS CLI configurada):
#   export CLIENTE=cliente1@ejemplo.com CLIENTE_CLAVE='...' MOTO1=moto1@ejemplo.com MOTO2=moto2@ejemplo.com MOTO_CLAVE='...'
#   bash scripts/delivery/humo.sh A      # o B, C, D, o todos
set -uo pipefail
STACK="${STACK:-URPOnlineLab}"
BLOQUE="${1:-todos}"
out() { aws cloudformation describe-stacks --stack-name "$1" --query "Stacks[0].Outputs[?OutputKey=='$2'].OutputValue" --output text; }
API=$(out "$STACK-delivery" ApiUrl); CLIENT_ID=$(out "$STACK-delivery" UserPoolClientId)
KEY=$(aws apigateway get-api-key --api-key "$(out "$STACK-delivery" ApiKeyId)" --include-value --query value --output text)
token() { aws cognito-idp initiate-auth --client-id "$CLIENT_ID" --auth-flow USER_PASSWORD_AUTH \
  --auth-parameters USERNAME="$1",PASSWORD="$2" --query AuthenticationResult.IdToken --output text; }
llamar() { # llamar TOKEN METODO RUTA [CUERPO] -> imprime "codigo cuerpo"
  local r; r=$(curl -s -w '\n%{http_code}' -X "$2" "$API$3" -H "Authorization: $1" -H "x-api-key: $KEY" \
    -H 'Content-Type: application/json' ${4:+-d "$4"}); echo "$(tail -n1 <<<"$r") $(sed '$d' <<<"$r")"; }
OK=0; KO=0
informe() { if [ "$2" = "1" ]; then echo "✅ $1 CUMPLE"; OK=$((OK+1)); else echo "❌ $1 NO CUMPLE — $3"; KO=$((KO+1)); fi; }

TC=$(token "$CLIENTE" "$CLIENTE_CLAVE"); TM1=$(token "$MOTO1" "$MOTO_CLAVE"); TM2=$(token "$MOTO2" "$MOTO_CLAVE")
REST=$(llamar "$TC" GET /v1/restaurantes | cut -d' ' -f2- | python3 -c 'import sys,json; print(json.load(sys.stdin)[0]["id"])')
PLATO=$(llamar "$TC" GET "/v1/restaurantes/$REST/platos" | cut -d' ' -f2- | python3 -c 'import sys,json; print(json.load(sys.stdin)[0]["id"])')
PEDIDO_JSON="{\"restauranteId\":$REST,\"items\":[{\"platoId\":$PLATO,\"cantidad\":1}],\"lat\":-12.12,\"lon\":-77.03}"
nuevo_pedido() { llamar "$TC" POST /v1/pedidos "$PEDIDO_JSON" | cut -d' ' -f2- | python3 -c 'import sys,json; print(json.load(sys.stdin)["id"])'; }
estado() { llamar "$TC" GET "/v1/pedidos/$1" | cut -d' ' -f2- | python3 -c 'import sys,json; print(json.load(sys.stdin)["estado"])'; }

bloque_A() {
  echo "== Bloque A — Pedidos y entrega con Lambda + SQS"
  local codigos; codigos=$(for i in $(seq 1 50); do llamar "$TC" POST /v1/pedidos "$PEDIDO_JSON" | cut -d' ' -f1 & done; wait)
  informe "A1 (50 pedidos simultáneos aceptados)" "$([ "$(grep -c '^202$' <<<"$codigos")" = 50 ] && echo 1)" "$(sort <<<"$codigos" | uniq -c | tr '\n' ' ')"
  local p; p=$(nuevo_pedido); sleep 5; llamar "$TM1" POST "/v1/pedidos/$p/aceptar" >/dev/null
  llamar "$TM1" POST /v1/sincronizar "{\"cambios\":[{\"pedidoId\":$p,\"estado\":\"entregado\"}]}" >/dev/null; sleep 15
  informe "A3 (entregado antes de recogido se rechaza)" "$([ "$(estado "$p")" != entregado ] && echo 1)" "el pedido $p quedó entregado sin pasar por recogido"
  local dlq; dlq=$(aws sqs get-queue-url --queue-name "delivery-pedidos-recibidos-dlq-$STACK-delivery" --query QueueUrl --output text)
  local antes; antes=$(aws sqs get-queue-attributes --queue-url "$dlq" --attribute-names ApproximateNumberOfMessages --query Attributes.ApproximateNumberOfMessages --output text)
  aws sqs send-message --queue-url "$(aws sqs get-queue-url --queue-name "delivery-pedidos-recibidos-$STACK-delivery" --query QueueUrl --output text)" --message-body 'veneno' >/dev/null
  echo "   (A5: esperando que el mensaje venenoso agote sus reintentos, ~10 min)"; sleep 600
  local despues; despues=$(aws sqs get-queue-attributes --queue-url "$dlq" --attribute-names ApproximateNumberOfMessages --query Attributes.ApproximateNumberOfMessages --output text)
  informe "A5 (el mensaje venenoso llega a la DLQ)" "$([ "$despues" -gt "$antes" ] && echo 1)" "la DLQ sigue con $despues mensajes"
}

bloque_B() {
  echo "== Bloque B — Sincronización"
  local p; p=$(nuevo_pedido); sleep 5
  local r; r=$( (llamar "$TM1" POST "/v1/pedidos/$p/aceptar" & llamar "$TM2" POST "/v1/pedidos/$p/aceptar" & wait) | cut -d' ' -f1 | sort | tr '\n' ' ')
  informe "B2 (dos aceptan a la vez, gana uno)" "$([ "$r" = "200 409 " ] && echo 1)" "respuestas: $r (se espera 200 y 409)"
  echo "   B1, B3, B4 y B5 se demuestran en vivo con el celular (modo avión): no se pueden comprobar desde aquí."
}

bloque_C() {
  echo "== Bloque C — API Manager, contratos y búsqueda"
  local r; r=$(llamar "$TC" POST /v1/pedidos '{"items":"no-es-lista"}')
  informe "C1 (request inválido rechazado por API Gateway)" "$(grep -q '^400 .*Invalid request body' <<<"$r" && echo 1)" "respuesta: ${r:0:120}"
  local p; p=$(nuevo_pedido)
  informe "C2a (un cliente no puede aceptar: 403)" "$([ "$(llamar "$TC" POST "/v1/pedidos/$p/aceptar" | cut -d' ' -f1)" = 403 ] && echo 1)" "el cliente pudo aceptar"
  informe "C2b (sin token: 401)" "$([ "$(llamar "" GET /v1/restaurantes | cut -d' ' -f1)" = 401 ] && echo 1)" "sin token no dio 401"
  local b; b=$(llamar "$TC" GET "/v1/platos/buscar?q=pollo%20picante%20sin%20man%C3%AD" | cut -d' ' -f2-)
  informe "C5 («sin maní» no trae platos con maní)" "$(python3 -c 'import sys,json; d=json.loads(sys.argv[1]); print(1 if d and all("maní" not in p["alergenos"] for p in d) else "")' "$b")" "$(python3 -c 'import sys,json; print([p["nombre"] for p in json.loads(sys.argv[1]) if "maní" in p["alergenos"]])' "$b" 2>/dev/null)"
}

bloque_D() {
  echo "== Bloque D — Ver todo el flujo"
  local n; n=$(aws xray get-trace-summaries --start-time $(( $(date +%s) - 3600 )) --end-time "$(date +%s)" --query 'length(TraceSummaries)' --output text)
  informe "D1 (hay trazas de X-Ray de la última hora)" "$([ "${n:-0}" -gt 0 ] && echo 1)" "sin trazas"
  local t; t=$(aws cloudwatch list-dashboards --query "DashboardEntries[?contains(DashboardName,'delivery')].DashboardName" --output text)
  informe "D2 (existe un tablero CloudWatch del delivery)" "$([ -n "$t" ] && echo 1)" "no hay un tablero con 'delivery' en el nombre"
  echo "   D3, D4 y D5 se demuestran en vivo."
}

case "$BLOQUE" in A) bloque_A;; B) bloque_B;; C) bloque_C;; D) bloque_D;; *) bloque_A; bloque_B; bloque_C; bloque_D;; esac
echo; echo "Resultado: $OK CUMPLE, $KO NO CUMPLE"
