"""Fase 3 - Entrena el modelo de Machine Learning (ML) que PREDICE el comportamiento de la arquitectura.

A partir del dataset.csv (telemetria de todo el stack, etiquetada 'saturado'), entrena un modelo en
CPU (sin GPU) con scikit-learn que predice si la arquitectura se va a saturar en los proximos
--horizonte minutos (por defecto 5: lo que tarda en arrancar y calentar un pod o una EC2 nueva).
Usa el valor de cada metrica y su TENDENCIA (cambio respecto del minuto anterior).
Separa entrenamiento y prueba por TIEMPO (pasado / futuro), porque es una serie de tiempo.
Reporta PRECISION y RECALL (no solo acierto, porque la saturacion es un evento raro) y, por EPISODIO de
saturacion, si avisa antes de que empiece, comparado con un modelo trivial de PERSISTENCIA ("seguira
igual que ahora"): si el ML no le gana anticipando, no sirve.
Guarda el modelo en modelo.joblib.

Para que sirve: tener un modelo que anticipe cuando escalar y ayude a mejorar o redisenar la plataforma.

Uso:
  pip install -r requirements.txt
  python train.py --dataset dataset.csv --horizonte 5
"""
import argparse

import joblib
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import classification_report, confusion_matrix
from sklearn.model_selection import train_test_split

from collect_metrics import agregar_tendencias, etiqueta_horizonte, evaluar_avisos


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dataset", default="dataset.csv")
    ap.add_argument("--salida", default="modelo.joblib")
    ap.add_argument("--horizonte", type=int, default=5, help="minutos de anticipacion que se quieren predecir")
    a = ap.parse_args()

    df = pd.read_csv(a.dataset)
    if "saturado" not in df.columns:
        raise SystemExit("El dataset no tiene la columna 'saturado' (corre collect_metrics.py).")

    # Para PREDECIR (anticipar), la etiqueta es: ¿habra saturacion en los proximos `horizonte` minutos?
    df = agregar_tendencias(df)
    df["objetivo"] = etiqueta_horizonte(df["saturado"], a.horizonte)
    df = df.iloc[:-a.horizonte]          # las ultimas filas no tienen futuro completo para etiquetarse
    X = df.drop(columns=[c for c in ("minuto", "saturado", "objetivo") if c in df.columns])
    y = df["objetivo"]

    if y.nunique() < 2:
        raise SystemExit("El dataset no tiene ejemplos de las dos clases; genera mas carga (k6) y vuelve a recolectar.")

    # Es una serie de tiempo: se entrena con el PASADO (primer 70 %) y se prueba con el FUTURO (ultimo 30 %).
    # Mezclar al azar pondria minutos vecinos en ambos lados y inflaria precision y recall.
    Xtr, Xte, ytr, yte = train_test_split(X, y, test_size=0.3, shuffle=False)
    if ytr.nunique() < 2 or yte.sum() == 0:
        raise SystemExit("El entrenamiento o la prueba no tienen minutos saturados: alarga la carga (k6) para que haya "
                         "saturaciones al inicio y al final del periodo, y vuelve a recolectar.")
    # class_weight balanced: la saturacion es rara (clase desbalanceada).
    clf = RandomForestClassifier(n_estimators=200, class_weight="balanced", random_state=42)
    clf.fit(Xtr, ytr)

    pred = clf.predict(Xte)
    print(f"Horizonte: saturacion en los proximos {a.horizonte} minutos")
    print("Matriz de confusion (filas=real, cols=predicho):")
    print(confusion_matrix(yte, pred))
    print("\nPrecision y recall (lo que importa, no solo el acierto):")
    print(classification_report(yte, pred, target_names=["ok", "saturado"], zero_division=0))

    # Lo que importa: ¿avisa ANTES de que EMPIECE cada saturacion? Se compara con la linea base de
    # PERSISTENCIA (avisa si ahora mismo esta saturado), que casi nunca puede anticipar un inicio.
    sat = df.loc[Xte.index, "saturado"]
    ml = evaluar_avisos(pred, sat, a.horizonte)
    base = evaluar_avisos(sat, sat, a.horizonte)
    print(f"Por episodio de saturacion (en la prueba hay {ml['episodios']}):")
    print(f"  ML           -> cobertura {ml['cobertura']:.0%}, anticipacion {ml['anticipacion']:.1f} min, "
          f"falsas alarmas {ml['falsas']:.0%}")
    print(f"  Persistencia -> cobertura {base['cobertura']:.0%}, anticipacion {base['anticipacion']:.1f} min, "
          f"falsas alarmas {base['falsas']:.0%}")
    if ml["episodios"]:
        print("  El ML " + ("SUPERA" if ml["cobertura"] > base["cobertura"] else "NO supera")
              + " a la persistencia anticipando saturaciones.\n")

    print("Senales que mas anticipan la saturacion (importancia):")
    for col, imp in sorted(zip(X.columns, clf.feature_importances_), key=lambda t: -t[1]):
        print(f"  {col:20s} {imp:.3f}")

    joblib.dump({"modelo": clf, "columnas": list(X.columns), "horizonte": a.horizonte}, a.salida)
    print(f"\nModelo guardado en {a.salida}")


if __name__ == "__main__":
    main()
