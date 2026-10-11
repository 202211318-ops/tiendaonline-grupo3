# Fase 3 — Modelo de ML: predecir la saturación y escalar antes

Sin GPU, open source y sin costo (scikit-learn). Guía completa: **README de la raíz, paso 7** y la sección
"Cómo implementar el modelo de ML".

1. **Arma el dataset** con tu propia telemetría (después de correr la carga con k6). CloudWatch guarda las
   métricas por recurso: pasa tu ALB con `--alb` (o `--stack` y el script busca ALB, RDS, SQS y Lambda):
   ```bash
   pip install -r requirements.txt
   python collect_metrics.py --alb app/NOMBRE/ID --horas 0.5 --p95-umbral-ms 2000 --salida dataset.csv
   ```
2. **Entrena** (saturación en los próximos `--horizonte` minutos; separa pasado/futuro; reporta precisión,
   recall y, por episodio, cobertura y anticipación frente a la persistencia):
   ```bash
   python train.py --dataset dataset.csv --horizonte 5
   ```
3. **Predice y escala proactivamente** cada minuto (CronJob en EKS, `k8s/prediccion.yaml`, imagen `ml/Dockerfile`):
   ```bash
   python predict.py --alb app/NOMBRE/ID --modelo s3://BUCKET/modelo.joblib \
       --sns-topic arn:aws:sns:REGION:CUENTA:urponline-alertas --escalar-hpa urponline/urponline
   ```
   Publica `SaturacionPredicha` y `SaturacionProbabilidad` en CloudWatch (namespace URPOnline), alerta por SNS
   y sube el mínimo de pods del HPA **antes** de que sature.
4. **Pruebas** (sin AWS): `python -m pytest -q test_ml.py`

**Para qué sirve:** tener un modelo que prediga el comportamiento de la plataforma y ayude a decidir
cuándo escalar y cómo mejorar o rediseñar la arquitectura. Alternativas open source equivalentes:
XGBoost/LightGBM (Python) o ML.NET (C#), todas en CPU.
