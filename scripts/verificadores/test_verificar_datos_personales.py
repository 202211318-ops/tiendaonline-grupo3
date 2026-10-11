"""Pruebas del verificador de datos personales (sin AWS): python -m pytest test_verificar_datos_personales.py"""
from verificar_datos_personales import enmascarar, hallazgos, luhn


def tipos(mensaje, nombres=()):
    return sorted(t for t, _ in hallazgos(mensaje, list(nombres)))


def test_luhn():
    assert luhn("4111111111111111")          # tarjeta de prueba Visa
    assert luhn("4111 1111 1111 1111")
    assert not luhn("4111111111111112")
    assert not luhn("123456789012")          # muy corto


def test_detecta_correo_tarjeta_y_celular():
    m = "Pedido 25 de ana.perez@urp.edu.pe pagado con 4111 1111 1111 1111, cel 987654321"
    assert tipos(m) == ["celular", "correo", "tarjeta"]


def test_no_marca_datos_que_no_son_personales():
    # ids de pedido, códigos de producto, montos y fechas no deben contar como hallazgo
    m = "Pedido 1024 · total S/ 3799.00 · 2026-11-07T10:15:00Z · trace 1234567890123 · producto 12"
    assert tipos(m) == []


def test_nombres_de_clientes_de_prueba():
    m = "Correo de oferta enviado a Ana Perez"
    assert tipos(m, ["Ana Perez"]) == ["nombre"]
    assert tipos(m, ["Luis Rojas"]) == []


def test_enmascarado_no_revela_el_dato():
    assert enmascarar("ana.perez@urp.edu.pe") == "a***@urp.edu.pe"
    assert enmascarar("4111 1111 1111 1111") == "************1111"
    assert enmascarar("987654321") == "*****4321"
    valores = [v for _, v in hallazgos("ana.perez@urp.edu.pe 4111111111111111 987654321", [])]
    assert "ana.perez@urp.edu.pe" not in valores and "4111111111111111" not in valores
