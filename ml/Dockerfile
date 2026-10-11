# Fase 3 - Imagen del predictor (ml/predict.py) que corre cada minuto como CronJob en EKS.
# Solo CPU, sin GPU. Se construye desde la carpeta ml/:  docker build -t $ECR_ML:v1 ml/
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY collect_metrics.py predict.py ./
# Usuario sin privilegios.
RUN useradd --create-home predictor
USER predictor
ENTRYPOINT ["python", "predict.py"]
