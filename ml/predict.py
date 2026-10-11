"""Fase 3 - Escalado PROACTIVO con la prediccion del modelo.

Lee las ultimas metricas de CloudWatch (valor y tendencia), el modelo predice si la arquitectura se va
a saturar en los proximos minutos (el horizonte con que se entreno), y:
  - publica el contador 'SaturacionPredicha' (y la probabilidad) en CloudWatch, namespace URPOnline;
  - si va a saturar, manda una alerta por SNS;
  - con --escalar-hpa, sube el minimo de pods del HorizontalPodAutoscaler ANTES de que sature, y lo
    devuelve a --min-base cuando la prediccion vuelve a 'ok'. El HPA por CPU sigue funcionando encima.

Se corre cada minuto como CronJob en EKS (k8s/prediccion.yaml). El modelo puede venir de un archivo
local o de S3 (s3://bucket/clave).

Uso (mismos recursos que collect_metrics.py: --stack o --alb/--rds/--sqs/--lambda):
  python predict.py --alb app/k8s-urponlin-XXXX/YYYY --modelo s3://BUCKET/modelo.joblib \
      --sns-topic arn:aws:sns:...:urponline-alertas --escalar-hpa urponline/urponline
"""
import argparse
import datetime as dt
import json
import os
import ssl
import tempfile
import urllib.request

import boto3
import joblib
import pandas as pd

from collect_metrics import agregar_args_recursos, agregar_tendencias, resolver_recursos, tabla_por_minuto

SA_DIR = "/var/run/secrets/kubernetes.io/serviceaccount"


def cargar_modelo(ruta, region):
    """Carga el modelo de un archivo local o de s3://bucket/clave."""
    if ruta.startswith("s3://"):
        bucket, _, clave = ruta[5:].partition("/")
        destino = os.path.join(tempfile.gettempdir(), "modelo.joblib")
        boto3.client("s3", region_name=region).download_file(bucket, clave, destino)
        ruta = destino
    bundle = joblib.load(ruta)
    return bundle["modelo"], bundle["columnas"], bundle.get("horizonte", 1)


def ultimas_metricas(cw, recursos):
    """Valores y tendencias del ultimo minuto con datos (ventana de 5 min)."""
    fin = dt.datetime.now(dt.timezone.utc)
    df = tabla_por_minuto(cw, recursos, fin - dt.timedelta(minutes=5), fin)
    return agregar_tendencias(df).iloc[-1].to_dict() if not df.empty else {}


def minimo_de_pods(va_a_saturar, replicas_actuales, min_base, extra, max_replicas):
    """Si va a saturar: actuales + extra (sin pasar el maximo del HPA). Si no: el minimo base."""
    if not va_a_saturar:
        return min_base
    return min(max(min_base, replicas_actuales + extra), max_replicas)


def api_kubernetes(metodo, ruta, cuerpo=None, tipo="application/json"):
    """Llamada al API server desde dentro del pod, con el token de su ServiceAccount (RBAC en k8s/prediccion.yaml)."""
    token = open(os.path.join(SA_DIR, "token"), encoding="utf-8").read().strip()
    ctx = ssl.create_default_context(cafile=os.path.join(SA_DIR, "ca.crt"))
    url = f"https://{os.environ['KUBERNETES_SERVICE_HOST']}:{os.environ.get('KUBERNETES_SERVICE_PORT', '443')}{ruta}"
    req = urllib.request.Request(url, method=metodo, data=json.dumps(cuerpo).encode() if cuerpo is not None else None,
                                 headers={"Authorization": f"Bearer {token}", "Content-Type": tipo})
    with urllib.request.urlopen(req, context=ctx, timeout=10) as r:
        return json.loads(r.read())


def escalar_hpa(ns_nombre, va_a_saturar, min_base, extra):
    ns, _, nombre = ns_nombre.partition("/")
    ruta = f"/apis/autoscaling/v2/namespaces/{ns}/horizontalpodautoscalers/{nombre}"
    hpa = api_kubernetes("GET", ruta)
    actuales = hpa.get("status", {}).get("currentReplicas", min_base)
    nuevo = minimo_de_pods(va_a_saturar, actuales, min_base, extra, hpa["spec"]["maxReplicas"])
    if nuevo != hpa["spec"].get("minReplicas"):
        api_kubernetes("PATCH", ruta, {"spec": {"minReplicas": nuevo}}, "application/merge-patch+json")
    print(f"HPA {ns_nombre}: pods actuales {actuales}, minimo {hpa['spec'].get('minReplicas')} -> {nuevo}")


def main():
    ap = argparse.ArgumentParser()
    agregar_args_recursos(ap)
    ap.add_argument("--modelo", default="modelo.joblib", help="archivo local o s3://bucket/clave")
    ap.add_argument("--sns-topic", default=None)
    ap.add_argument("--escalar-hpa", default=None, help="NAMESPACE/NOMBRE del HPA (solo dentro de EKS)")
    ap.add_argument("--min-base", type=int, default=2, help="minimo de pods cuando NO se predice saturacion")
    ap.add_argument("--pods-extra", type=int, default=2, help="pods que se agregan al predecir saturacion")
    a = ap.parse_args()

    modelo, columnas, horizonte = cargar_modelo(a.modelo, a.region)
    cw = boto3.client("cloudwatch", region_name=a.region)
    datos = ultimas_metricas(cw, resolver_recursos(a))
    X = pd.DataFrame([{c: datos.get(c, 0) for c in columnas}])
    prob = float(modelo.predict_proba(X)[0][1]) if hasattr(modelo, "predict_proba") else float(modelo.predict(X)[0])
    va_a_saturar = prob >= 0.5

    # Contador propio a CloudWatch (AWS SDK): 1 si se predice saturacion, mas la probabilidad.
    cw.put_metric_data(Namespace="URPOnline", MetricData=[
        {"MetricName": "SaturacionPredicha", "Value": 1 if va_a_saturar else 0, "Unit": "Count"},
        {"MetricName": "SaturacionProbabilidad", "Value": prob * 100, "Unit": "Percent"},
    ])
    print(f"Probabilidad de saturacion en los proximos {horizonte} min: {prob:.0%} -> {'ESCALAR' if va_a_saturar else 'ok'}")

    if a.escalar_hpa:
        escalar_hpa(a.escalar_hpa, va_a_saturar, a.min_base, a.pods_extra)

    if va_a_saturar and a.sns_topic:
        boto3.client("sns", region_name=a.region).publish(
            TopicArn=a.sns_topic,
            Subject="URPOnline: saturacion predicha",
            Message=f"El modelo de ML predice saturacion en los proximos {horizonte} min (prob {prob:.0%}). "
                    "Escalado proactivo disparado.")


if __name__ == "__main__":
    main()
