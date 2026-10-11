"""Fase 3 - Escalado PROACTIVO con la prediccion del modelo.

Lee las ultimas metricas de CloudWatch, el modelo predice si la arquitectura se va a saturar, y si
dice que si: publica el contador 'SaturacionPredicha' en CloudWatch (namespace URPOnline) y manda una
alerta por SNS. Una alarma sobre ese contador dispara el autoescalado ANTES de que sature.

Se corre cada minuto (EventBridge Scheduler -> Lambda, o un CronJob en EKS).

Uso:
  python predict.py --modelo modelo.joblib --sns-topic arn:aws:sns:...:urponline-alertas
"""
import argparse
import datetime as dt

import boto3
import joblib
import pandas as pd

from collect_metrics import METRICAS, serie


def ultimas_metricas(cw):
    fin = dt.datetime.utcnow()
    inicio = fin - dt.timedelta(minutes=5)
    cols = {}
    for ns, m, est, dims, nombre in METRICAS:
        puntos = serie(cw, ns, m, est, dims, inicio, fin)
        cols[nombre] = puntos[max(puntos)] if puntos else 0
    return cols


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--modelo", default="modelo.joblib")
    ap.add_argument("--sns-topic", default=None)
    ap.add_argument("--region", default=None)
    a = ap.parse_args()

    bundle = joblib.load(a.modelo)
    modelo, columnas = bundle["modelo"], bundle["columnas"]

    cw = boto3.client("cloudwatch", region_name=a.region)
    datos = ultimas_metricas(cw)
    X = pd.DataFrame([{c: datos.get(c, 0) for c in columnas}])
    prob = float(modelo.predict_proba(X)[0][1]) if hasattr(modelo, "predict_proba") else float(modelo.predict(X)[0])
    va_a_saturar = prob >= 0.5

    # Contador propio a CloudWatch (AWS SDK): 1 si se predice saturacion, mas la probabilidad.
    cw.put_metric_data(Namespace="URPOnline", MetricData=[
        {"MetricName": "SaturacionPredicha", "Value": 1 if va_a_saturar else 0, "Unit": "Count"},
        {"MetricName": "SaturacionProbabilidad", "Value": prob * 100, "Unit": "Percent"},
    ])
    print(f"Probabilidad de saturacion: {prob:.0%} -> {'ESCALAR' if va_a_saturar else 'ok'}")

    if va_a_saturar and a.sns_topic:
        boto3.client("sns", region_name=a.region).publish(
            TopicArn=a.sns_topic,
            Subject="URPOnline: saturacion predicha",
            Message=f"El modelo de ML predice saturacion (prob {prob:.0%}). Escalado proactivo disparado.")


if __name__ == "__main__":
    main()
