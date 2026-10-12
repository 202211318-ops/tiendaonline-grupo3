"""Pruebas de la respuesta automatica a intrusion (sin AWS): python -m pytest -q infra/test_respuesta_intrusion.py"""
import os
import sys

os.environ.update(AWS_DEFAULT_REGION='us-east-2', AWS_ACCESS_KEY_ID='prueba', AWS_SECRET_ACCESS_KEY='prueba',
                  FLOW_LOGS='/urponline/pila/vpc-flow-logs', CUARENTENA_SG='sg-cuarentena', TOPIC='arn:aws:sns:us-east-2:1:seguridad')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from botocore.stub import ANY, Stubber  # noqa: E402
import respuesta_intrusion as r  # noqa: E402


def _stubs():
    return Stubber(r.logs), Stubber(r.ec2), Stubber(r.asg), Stubber(r.sns)


def test_aisla_la_instancia_que_intento_la_conexion(monkeypatch):
    monkeypatch.setattr(r.time, 'sleep', lambda s: None)
    sl, se, sa, ss = _stubs()
    sl.add_response('start_query', {'queryId': 'q1'}, {'logGroupName': '/urponline/pila/vpc-flow-logs', 'startTime': ANY,
                                                         'endTime': ANY, 'queryString': r.CONSULTA})
    sl.add_response('get_query_results', {'status': 'Running', 'results': []}, {'queryId': 'q1'})
    sl.add_response('get_query_results', {'status': 'Complete', 'results': [
        [{'field': 'interfaceId', 'value': 'eni-ec2'}, {'field': 'count()', 'value': '4'}],
        [{'field': 'interfaceId', 'value': 'eni-lambda'}, {'field': 'count()', 'value': '1'}]]}, {'queryId': 'q1'})
    se.add_response('describe_network_interfaces', {'NetworkInterfaces': [
        {'NetworkInterfaceId': 'eni-ec2', 'Attachment': {'InstanceId': 'i-0abc'}},
        {'NetworkInterfaceId': 'eni-lambda', 'Attachment': {}}]},      # la ENI de una Lambda no tiene instancia
        {'NetworkInterfaceIds': ['eni-ec2', 'eni-lambda']})
    se.add_response('create_snapshots', {'Snapshots': []}, {'InstanceSpecification': {'InstanceId': 'i-0abc'},
                                                            'Description': ANY, 'TagSpecifications': ANY})
    se.add_response('modify_instance_attribute', {}, {'InstanceId': 'i-0abc', 'Groups': ['sg-cuarentena']})
    sa.add_response('set_instance_health', {}, {'InstanceId': 'i-0abc', 'HealthStatus': 'Unhealthy', 'ShouldRespectGracePeriod': False})
    ss.add_response('publish', {'MessageId': 'm1'}, {'TopicArn': 'arn:aws:sns:us-east-2:1:seguridad', 'Subject': ANY, 'Message': ANY})
    with sl, se, sa, ss:
        assert r.handler({}, None) == {'aisladas': ['i-0abc']}
        se.assert_no_pending_responses(); sa.assert_no_pending_responses(); ss.assert_no_pending_responses()


def test_si_no_encuentra_la_conexion_solo_avisa(monkeypatch):
    monkeypatch.setattr(r.time, 'sleep', lambda s: None)
    sl, se, sa, ss = _stubs()
    sl.add_response('start_query', {'queryId': 'q2'}, None)
    sl.add_response('get_query_results', {'status': 'Complete', 'results': []}, {'queryId': 'q2'})
    ss.add_response('publish', {'MessageId': 'm2'}, None)
    with sl, se, sa, ss:
        assert r.handler({}, None) == {'aisladas': []}
        ss.assert_no_pending_responses()


def test_el_codigo_cabe_en_la_plantilla():
    # CloudFormation acepta hasta 4096 caracteres de codigo en linea (ZipFile).
    fuente = open(os.path.join(os.path.dirname(__file__), 'respuesta_intrusion.py'), encoding='utf-8').read()
    assert len(fuente) <= 4096, len(fuente)
