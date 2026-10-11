# Fase 3 — Modelo de ML: predecir el comportamiento de la arquitectura

Sin GPU, open source y sin costo (scikit-learn). Flujo:

1. **Arma el dataset** con tu propia telemetría (mientras corres k6 de carga):
   ```bash
   pip install -r requirements.txt
   python collect_metrics.py --horas 6 --p95-umbral-ms 2000 --salida dataset.csv
   ```
2. **Entrena** el modelo (reporta precisión y recall, no solo acierto):
   ```bash
   python train.py --dataset dataset.csv
   ```
3. **Predice y escala proactivamente** (cada minuto, por Scheduler→Lambda o un CronJob en EKS):
   ```bash
   python predict.py --modelo modelo.joblib --sns-topic arn:aws:sns:REGION:CUENTA:urponline-alertas
   ```
   Publica `SaturacionPredicha` y `SaturacionProbabilidad` en CloudWatch (namespace URPOnline) y alerta por SNS.
   Una **alarma** sobre `SaturacionPredicha` dispara el **autoescalado ANTES** de que sature.

**Para qué sirve:** tener un modelo que prediga el comportamiento de la plataforma y ayude a decidir
cuándo escalar y cómo mejorar o rediseñar la arquitectura. Alternativas open source equivalentes:
XGBoost/LightGBM (Python) o ML.NET (C#), todas en CPU.
