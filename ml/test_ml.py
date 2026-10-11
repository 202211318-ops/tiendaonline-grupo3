"""Pruebas del modelo de ML (sin AWS):  python -m pytest ml/test_ml.py"""
import os
import sys

import pandas as pd

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from collect_metrics import TIPOS_CFN, agregar_tendencias, etiqueta_horizonte, evaluar_avisos  # noqa: E402
from predict import minimo_de_pods  # noqa: E402


def test_etiqueta_horizonte_mira_solo_el_futuro():
    sat = pd.Series([0, 0, 0, 1, 0, 0])
    # minutos 0,1,2 ven la saturacion del minuto 3 dentro de 3 min; el 3 no se cuenta a si mismo
    assert etiqueta_horizonte(sat, 3).tolist() == [1, 1, 1, 0, 0, 0]


def test_tendencia_es_el_cambio_respecto_del_minuto_anterior():
    df = agregar_tendencias(pd.DataFrame({"alb_req": [10, 15, 12]}))
    assert df["alb_req_delta"].tolist() == [0, 5, -3]


def test_evaluar_por_episodio_y_no_por_minuto():
    sat = [0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1]           # 2 episodios: empiezan en 4 y 12
    aviso = [0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]          # avisa 2 min antes del 1.o, nada del 2.o
    r = evaluar_avisos(aviso, sat, horizonte=3)
    assert r["episodios"] == 2 and r["avisados"] == 1
    assert r["cobertura"] == 0.5 and r["anticipacion"] == 2


def test_persistencia_no_anticipa_inicios():
    sat = [0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1]
    assert evaluar_avisos(sat, sat, horizonte=3)["cobertura"] == 0


def test_falsas_alarmas():
    sat = [0] * 10
    aviso = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    assert evaluar_avisos(aviso, sat, horizonte=3)["falsas"] == 1.0


def test_minimo_de_pods():
    assert minimo_de_pods(False, 7, 2, 2, 10) == 2               # sin prediccion: vuelve al minimo base
    assert minimo_de_pods(True, 3, 2, 2, 10) == 5                # con prediccion: actuales + extra
    assert minimo_de_pods(True, 9, 2, 2, 10) == 10               # nunca pasa el maximo del HPA


def test_dimensiones_desde_cloudformation():
    alb = TIPOS_CFN["AWS::ElasticLoadBalancingV2::LoadBalancer"][1]
    sqs = TIPOS_CFN["AWS::SQS::Queue"][1]
    assert alb("arn:aws:elasticloadbalancing:us-east-2:1:loadbalancer/app/mi-alb/abc") == "app/mi-alb/abc"
    assert sqs("https://sqs.us-east-2.amazonaws.com/1/actas") == "actas"
