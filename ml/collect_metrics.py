"""Fase 3 - Arma el DATASET con la propia telemetria de toda la arquitectura.

Baja de CloudWatch las metricas de todo el stack (ALB, RDS, SQS, Lambda) por minuto, las une en una
tabla y las ETIQUETA: 'saturado' = 1 si el p95 del ALB supero el umbral. Sale a dataset.csv.
Sin GPU: solo pandas + boto3.

CloudWatch publica estas metricas POR RECURSO (dimension), por eso hay que decirle cual es tu ALB,
tu RDS, tu cola y tu Lambda. Lo mas facil es pasar --stack: el script los busca en tu pila.
O los das a mano (--alb es obligatorio; los demas son opcionales y, si faltan, esa columna sale en 0).

Uso:
  python collect_metrics.py --stack MiCasoStack --horas 6 --p95-umbral-ms 2000 --salida dataset.csv
  python collect_metrics.py --alb app/mi-alb/0123456789abcdef --rds mi-rds --horas 6
"""
import argparse
import datetime as dt
import sys

import boto3
import pandas as pd


# (namespace, metrica, estadistico, recurso, nombre_columna)
METRICAS = [
    ("AWS/ApplicationELB", "TargetResponseTime", "p95", "alb", "alb_p95"),
    ("AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "Sum", "alb", "alb_5xx"),
    ("AWS/ApplicationELB", "RequestCount", "Sum", "alb", "alb_req"),
    ("AWS/RDS", "DatabaseConnections", "Average", "rds", "rds_conns"),
    ("AWS/RDS", "CPUUtilization", "Average", "rds", "rds_cpu"),
    ("AWS/SQS", "ApproximateNumberOfMessagesVisible", "Maximum", "sqs", "sqs_visibles"),
    ("AWS/Lambda", "Duration", "Average", "lambda", "lambda_dur"),
    ("AWS/Lambda", "Throttles", "Sum", "lambda", "lambda_throttles"),
]

# Nombre de la dimension de CloudWatch para cada tipo de recurso.
DIMENSION = {"alb": "LoadBalancer", "rds": "DBInstanceIdentifier", "sqs": "QueueName", "lambda": "FunctionName"}

# Tipo de recurso de CloudFormation -> como sacar el valor de la dimension de su PhysicalResourceId.
TIPOS_CFN = {
    "AWS::ElasticLoadBalancingV2::LoadBalancer": ("alb", lambda pid: pid.split(":loadbalancer/", 1)[-1]),
    "AWS::RDS::DBInstance": ("rds", lambda pid: pid),
    "AWS::SQS::Queue": ("sqs", lambda pid: pid.rstrip("/").rsplit("/", 1)[-1]),
    "AWS::Lambda::Function": ("lambda", lambda pid: pid),
}


def recursos_de_la_pila(cfn, stack):
    """Busca en la pila (y sus pilas anidadas) el primer ALB, RDS, cola SQS y Lambda."""
    encontrados = {}
    pendientes = [stack]
    while pendientes:
        pila = pendientes.pop()
        for pagina in cfn.get_paginator("list_stack_resources").paginate(StackName=pila):
            for r in pagina["StackResourceSummaries"]:
                tipo, pid = r["ResourceType"], r.get("PhysicalResourceId", "")
                if tipo == "AWS::CloudFormation::Stack" and pid:
                    pendientes.append(pid)
                elif tipo in TIPOS_CFN and pid:
                    clave, valor = TIPOS_CFN[tipo]
                    if clave in encontrados:
                        print(f"  Aviso: hay mas de un {clave}; uso {encontrados[clave]} (pasa --{clave} para elegir).")
                    else:
                        encontrados[clave] = valor(pid)
    return encontrados


def serie(cw, namespace, metrica, estadistico, dims, inicio, fin):
    kwargs = dict(
        Namespace=namespace, MetricName=metrica,
        Dimensions=[{"Name": k, "Value": v} for k, v in dims.items()],
        StartTime=inicio, EndTime=fin, Period=60,
    )
    if estadistico == "p95":
        kwargs["ExtendedStatistics"] = ["p95"]
    else:
        kwargs["Statistics"] = [estadistico]
    r = cw.get_metric_statistics(**kwargs)
    puntos = {}
    for p in r["Datapoints"]:
        val = p["ExtendedStatistics"]["p95"] if estadistico == "p95" else p[estadistico]
        puntos[p["Timestamp"].replace(second=0, microsecond=0)] = val
    return puntos


def agregar_args_recursos(ap):
    """Argumentos para ubicar los recursos (los usan collect_metrics.py y predict.py)."""
    ap.add_argument("--stack", default=None, help="pila de CloudFormation donde buscar ALB/RDS/SQS/Lambda")
    ap.add_argument("--alb", default=None, help="dimension LoadBalancer, ej. app/mi-alb/0123456789abcdef")
    ap.add_argument("--rds", default=None, help="DBInstanceIdentifier")
    ap.add_argument("--sqs", default=None, help="QueueName")
    ap.add_argument("--lambda", dest="lambda_", default=None, help="FunctionName")
    ap.add_argument("--region", default=None)


def resolver_recursos(a):
    """Recursos a medir: los de la pila (--stack) y, encima, los que se pasen a mano."""
    recursos = {}
    if a.stack:
        recursos = recursos_de_la_pila(boto3.client("cloudformation", region_name=a.region), a.stack)
    for clave, valor in (("alb", a.alb), ("rds", a.rds), ("sqs", a.sqs), ("lambda", a.lambda_)):
        if valor:
            recursos[clave] = valor
    if "alb" not in recursos:
        sys.exit("No se encontro el ALB: pasa --stack TU_PILA o --alb app/NOMBRE/ID "
                 "(la etiqueta 'saturado' sale del p95 del ALB).")
    return recursos


def tabla_por_minuto(cw, recursos, inicio, fin):
    """Una fila por minuto, una columna por metrica (0 si ese recurso no existe)."""
    cols = {}
    for ns, m, est, clave, nombre in METRICAS:
        if clave in recursos:
            cols[nombre] = serie(cw, ns, m, est, {DIMENSION[clave]: recursos[clave]}, inicio, fin)
        else:
            cols[nombre] = {}
    return pd.DataFrame(cols).sort_index().ffill().fillna(0)


COLUMNAS_METRICAS = [nombre for *_, nombre in METRICAS]


def agregar_tendencias(df):
    """Agrega, por metrica, cuanto cambio respecto del minuto anterior (columna <metrica>_delta).
    La TENDENCIA (la carga subiendo) es lo que permite avisar antes de saturar, no solo el valor actual.
    La usan train.py, predict.py y validar_ml.py para que el modelo vea siempre las mismas columnas."""
    df = df.copy()
    for col in COLUMNAS_METRICAS:
        if col in df.columns:
            df[f"{col}_delta"] = df[col].diff().fillna(0)
    return df


def etiqueta_horizonte(saturado, horizonte):
    """1 si hay saturacion en ALGUNO de los proximos `horizonte` minutos (sin contar el actual)."""
    futuro = pd.concat([saturado.shift(-k) for k in range(1, horizonte + 1)], axis=1)
    return futuro.max(axis=1).fillna(0).astype(int)


def evaluar_avisos(predicho, saturado, horizonte):
    """Evalua avisos (0/1 por minuto) por EPISODIO de saturacion, no por minuto: dentro de una saturacion
    que ya empezo, decir "sigue saturado" es trivial; lo que vale es avisar ANTES de que empiece.
      - cobertura: % de episodios con algun aviso en los `horizonte` minutos previos a su inicio.
      - anticipacion: minutos promedio entre el primer aviso y el inicio del episodio.
      - falsas: % de avisos tras los cuales NO hubo saturacion en el horizonte."""
    p, s = list(predicho), list(saturado)
    inicios = [i for i in range(1, len(s)) if s[i] == 1 and s[i - 1] == 0]
    avisados, anticipos = 0, []
    for i in inicios:
        ventana = [j for j in range(max(0, i - horizonte), i) if p[j] == 1]
        if ventana:
            avisados += 1
            anticipos.append(i - min(ventana))
    objetivo = etiqueta_horizonte(pd.Series(s), horizonte).tolist()
    avisos = [j for j in range(len(p) - horizonte) if p[j] == 1]
    return {
        "episodios": len(inicios),
        "avisados": avisados,
        "cobertura": avisados / len(inicios) if inicios else 0.0,
        "anticipacion": sum(anticipos) / len(anticipos) if anticipos else 0.0,
        "falsas": sum(1 for j in avisos if objetivo[j] == 0) / len(avisos) if avisos else 0.0,
    }


def main():
    ap = argparse.ArgumentParser()
    agregar_args_recursos(ap)
    ap.add_argument("--horas", type=float, default=6)
    ap.add_argument("--p95-umbral-ms", type=float, default=2000)
    ap.add_argument("--salida", default="dataset.csv")
    a = ap.parse_args()

    recursos = resolver_recursos(a)
    for clave in DIMENSION:
        print(f"  {clave:7s} -> {recursos.get(clave, '(no hay: columnas en 0)')}")

    cw = boto3.client("cloudwatch", region_name=a.region)
    fin = dt.datetime.now(dt.timezone.utc)
    inicio = fin - dt.timedelta(hours=a.horas)

    df = tabla_por_minuto(cw, recursos, inicio, fin)
    if df.empty or df["alb_p95"].eq(0).all():
        sys.exit("CloudWatch no devolvio datos del ALB en ese periodo: genera carga (k6) y revisa --horas/--region.")
    # El p95 del ALB viene en segundos; lo paso a ms para la etiqueta.
    df["saturado"] = (df["alb_p95"] * 1000.0 > a.p95_umbral_ms).astype(int)
    df.to_csv(a.salida, index_label="minuto")
    print(f"Guardado {a.salida}: {len(df)} filas, {df['saturado'].sum()} saturadas.")


if __name__ == "__main__":
    main()
