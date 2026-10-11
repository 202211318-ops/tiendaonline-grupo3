#!/usr/bin/env python3
"""Verificador automático: ¿hay datos personales en los logs de CloudWatch del grupo?

Revisa los grupos de logs de la tienda y de las Lambdas con CloudWatch Logs Insights y busca:
  - correos electrónicos
  - números de tarjeta (13 a 19 dígitos que pasan la validación de Luhn)
  - celulares peruanos (9 dígitos que empiezan con 9)
  - nombres de clientes de prueba (opcional, desde un archivo con un nombre por línea)

Resultado: CUMPLE (no encontró nada) o NO CUMPLE (muestra cada hallazgo ENMASCARADO).
Código de salida: 0 = cumple, 1 = no cumple, 2 = error de uso o de AWS.

Uso en AWS CloudShell (ya trae python3 y boto3):
  python3 verificar_datos_personales.py --pila URPOnlineLab --horas 24
  python3 verificar_datos_personales.py --pila URPOnlineLab --nombres clientes_prueba.txt
"""
import argparse
import re
import sys
import time

CORREO = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
DIGITOS = re.compile(r"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)")
CELULAR = re.compile(r"(?<!\d)9\d{8}(?!\d)")

# Regex que entiende Logs Insights para traer solo los mensajes sospechosos (el detalle se valida en Python).
FILTRO_INSIGHTS = r"/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|\d{13,19}|\d{4}[ -]\d{4}[ -]\d{4}|(^|[^0-9])9\d{8}([^0-9]|$)/"


def luhn(numero: str) -> bool:
    d = [int(c) for c in numero if c.isdigit()]
    if not 13 <= len(d) <= 19:
        return False
    total = 0
    for i, x in enumerate(reversed(d)):
        if i % 2 == 1:
            x *= 2
            if x > 9:
                x -= 9
        total += x
    return total % 10 == 0


def enmascarar(valor: str) -> str:
    if "@" in valor:
        usuario, dominio = valor.split("@", 1)
        return usuario[:1] + "***@" + dominio
    limpio = re.sub(r"\D", "", valor)
    if limpio:
        return "*" * (len(limpio) - 4) + limpio[-4:]
    return valor[:1] + "***"


def hallazgos(mensaje: str, nombres: list[str]) -> list[tuple[str, str]]:
    """Devuelve [(tipo, valor_enmascarado)] encontrados en un mensaje de log."""
    encontrados = [("correo", enmascarar(m)) for m in CORREO.findall(mensaje)]
    encontrados += [("tarjeta", enmascarar(m)) for m in DIGITOS.findall(mensaje) if luhn(m)]
    encontrados += [("celular", enmascarar(m)) for m in CELULAR.findall(mensaje)]
    bajo = mensaje.lower()
    encontrados += [("nombre", enmascarar(n)) for n in nombres if n and n.lower() in bajo]
    return encontrados


def grupos_de_logs(logs, pila: str, prefijos: list[str]) -> list[str]:
    grupos = []
    for prefijo in prefijos:
        prefijo = prefijo.replace("{pila}", pila)
        for pagina in logs.get_paginator("describe_log_groups").paginate(logGroupNamePrefix=prefijo):
            grupos += [g["logGroupName"] for g in pagina["logGroups"]]
    return sorted(set(grupos))


def consultar(logs, grupos: list[str], horas: int, limite: int) -> list[dict]:
    fin = int(time.time())
    inicio = fin - horas * 3600
    consulta = f"fields @timestamp, @log, @message | filter @message like {FILTRO_INSIGHTS} | limit {limite}"
    resultados = []
    for i in range(0, len(grupos), 50):  # Logs Insights admite hasta 50 grupos por consulta
        qid = logs.start_query(logGroupNames=grupos[i:i + 50], startTime=inicio, endTime=fin,
                               queryString=consulta)["queryId"]
        while True:
            r = logs.get_query_results(queryId=qid)
            if r["status"] in ("Complete", "Failed", "Cancelled", "Timeout"):
                break
            time.sleep(1)
        if r["status"] != "Complete":
            raise RuntimeError(f"La consulta de Logs Insights terminó en estado {r['status']}")
        for fila in r["results"]:
            resultados.append({c["field"]: c["value"] for c in fila})
    return resultados


def main() -> int:
    ap = argparse.ArgumentParser(description="Busca datos personales en los logs de CloudWatch del grupo.")
    ap.add_argument("--pila", default="URPOnlineLab", help="Nombre de la pila de CloudFormation")
    ap.add_argument("--region", default="us-east-2")
    ap.add_argument("--horas", type=int, default=24, help="Cuántas horas hacia atrás revisar")
    ap.add_argument("--nombres", help="Archivo con nombres de clientes de prueba, uno por línea")
    ap.add_argument("--prefijos", default="/urponline/{pila}/,/aws/lambda/{pila}",
                    help="Prefijos de grupos de logs, separados por coma ({pila} se reemplaza)")
    ap.add_argument("--limite", type=int, default=1000, help="Máximo de mensajes sospechosos a revisar")
    a = ap.parse_args()

    nombres = []
    if a.nombres:
        with open(a.nombres, encoding="utf-8") as f:
            nombres = [l.strip() for l in f if len(l.strip()) >= 4]

    try:
        import boto3
        logs = boto3.client("logs", region_name=a.region)
        grupos = grupos_de_logs(logs, a.pila, [p.strip() for p in a.prefijos.split(",") if p.strip()])
        if not grupos:
            print(f"ERROR: no se encontraron grupos de logs para la pila {a.pila} en {a.region}.")
            return 2
        print(f"Revisando {len(grupos)} grupo(s) de logs de las últimas {a.horas} h:")
        for g in grupos:
            print("  -", g)
        filas = consultar(logs, grupos, a.horas, a.limite)
        if nombres:  # los nombres no se pueden filtrar con la regex; se revisan con una segunda consulta
            alternativa = "|".join(re.escape(n) for n in nombres)
            global FILTRO_INSIGHTS
            FILTRO_INSIGHTS = f"/(?i){alternativa}/"
            filas += consultar(logs, grupos, a.horas, a.limite)
    except Exception as e:  # credenciales, permisos o región
        print(f"ERROR al consultar CloudWatch Logs: {e}")
        return 2

    total = []
    for fila in filas:
        for tipo, valor in hallazgos(fila.get("@message", ""), nombres):
            total.append((fila.get("@timestamp", "?"), fila.get("@log", "?"), tipo, valor))
    total = sorted(set(total))

    print()
    if not total:
        print("RESULTADO: CUMPLE — no se encontraron datos personales en los logs revisados.")
        return 0
    print(f"RESULTADO: NO CUMPLE — {len(total)} hallazgo(s) (los valores se muestran enmascarados):")
    for ts, grupo, tipo, valor in total[:50]:
        print(f"  {ts}  {tipo:8}  {valor:28}  {grupo}")
    if len(total) > 50:
        print(f"  … y {len(total) - 50} más")
    return 1


if __name__ == "__main__":
    sys.exit(main())
