"""Fase 3 - Arma el DATASET con la propia telemetria de toda la arquitectura.

Baja de CloudWatch las metricas de todo el stack (ALB, EC2/Fargate, SQS, Lambda, RDS) por minuto,
las une en una tabla y las ETIQUETA: 'saturado' = 1 si el p95 supero el umbral. Sale a dataset.csv.
Sin GPU: solo pandas + boto3.

Uso:
  python collect_metrics.py --horas 6 --p95-umbral-ms 2000 --salida dataset.csv
"""
import argparse
import datetime as dt

import boto3
import pandas as pd


# (namespace, metrica, estadistico, dimensiones, nombre_columna)
METRICAS = [
    ("AWS/ApplicationELB", "TargetResponseTime", "p95", {}, "alb_p95"),
    ("AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "Sum", {}, "alb_5xx"),
    ("AWS/ApplicationELB", "RequestCount", "Sum", {}, "alb_req"),
    ("AWS/RDS", "DatabaseConnections", "Average", {}, "rds_conns"),
    ("AWS/RDS", "CPUUtilization", "Average", {}, "rds_cpu"),
    ("AWS/SQS", "ApproximateNumberOfMessagesVisible", "Maximum", {}, "sqs_visibles"),
    ("AWS/Lambda", "Duration", "Average", {}, "lambda_dur"),
    ("AWS/Lambda", "Throttles", "Sum", {}, "lambda_throttles"),
]


def serie(cw, namespace, metrica, estadistico, dims, inicio, fin):
    stat = "p95" if estadistico == "p95" else estadistico
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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--horas", type=float, default=6)
    ap.add_argument("--p95-umbral-ms", type=float, default=2000)
    ap.add_argument("--region", default=None)
    ap.add_argument("--salida", default="dataset.csv")
    a = ap.parse_args()

    cw = boto3.client("cloudwatch", region_name=a.region)
    fin = dt.datetime.utcnow()
    inicio = fin - dt.timedelta(hours=a.horas)

    cols = {}
    for ns, m, est, dims, nombre in METRICAS:
        cols[nombre] = serie(cw, ns, m, est, dims, inicio, fin)

    df = pd.DataFrame(cols).sort_index().fillna(method="ffill").fillna(0)
    # El p95 del ALB viene en segundos; lo paso a ms para la etiqueta.
    p95_ms = df.get("alb_p95", pd.Series(0, index=df.index)) * 1000.0
    df["saturado"] = (p95_ms > a.p95_umbral_ms).astype(int)
    df.to_csv(a.salida, index_label="minuto")
    print(f"Guardado {a.salida}: {len(df)} filas, {df['saturado'].sum()} saturadas.")


if __name__ == "__main__":
    main()
