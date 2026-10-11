"""Fase 3 - Entrena el modelo de Machine Learning (ML) que PREDICE el comportamiento de la arquitectura.

A partir del dataset.csv (telemetria de todo el stack, etiquetada 'saturado'), entrena un modelo en
CPU (sin GPU) con scikit-learn que predice si la arquitectura se va a saturar. Reporta PRECISION y
RECALL (no solo acierto, porque la saturacion es un evento raro). Guarda el modelo en modelo.joblib.

Para que sirve: tener un modelo que anticipe cuando escalar y ayude a mejorar o redisenar la plataforma.

Uso:
  pip install -r requirements.txt
  python train.py --dataset dataset.csv
"""
import argparse

import joblib
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import classification_report, confusion_matrix
from sklearn.model_selection import train_test_split


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dataset", default="dataset.csv")
    ap.add_argument("--salida", default="modelo.joblib")
    a = ap.parse_args()

    df = pd.read_csv(a.dataset)
    if "saturado" not in df.columns:
        raise SystemExit("El dataset no tiene la columna 'saturado' (corre collect_metrics.py).")

    # Para PREDECIR (anticipar), la etiqueta es la saturacion del minuto SIGUIENTE.
    df["objetivo"] = df["saturado"].shift(-1).fillna(0).astype(int)
    X = df.drop(columns=[c for c in ("minuto", "saturado", "objetivo") if c in df.columns])
    y = df["objetivo"]

    if y.nunique() < 2:
        raise SystemExit("El dataset no tiene ejemplos de las dos clases; genera mas carga (k6) y vuelve a recolectar.")

    Xtr, Xte, ytr, yte = train_test_split(X, y, test_size=0.3, random_state=42, stratify=y)
    # class_weight balanced: la saturacion es rara (clase desbalanceada).
    clf = RandomForestClassifier(n_estimators=200, class_weight="balanced", random_state=42)
    clf.fit(Xtr, ytr)

    pred = clf.predict(Xte)
    print("Matriz de confusion (filas=real, cols=predicho):")
    print(confusion_matrix(yte, pred))
    print("\nPrecision y recall (lo que importa, no solo el acierto):")
    print(classification_report(yte, pred, target_names=["ok", "saturado"], zero_division=0))

    print("Senales que mas anticipan la saturacion (importancia):")
    for col, imp in sorted(zip(X.columns, clf.feature_importances_), key=lambda t: -t[1]):
        print(f"  {col:20s} {imp:.3f}")

    joblib.dump({"modelo": clf, "columnas": list(X.columns)}, a.salida)
    print(f"\nModelo guardado en {a.salida}")


if __name__ == "__main__":
    main()
