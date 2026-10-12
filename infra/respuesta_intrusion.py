# Respuesta AUTOMATICA a una intrusion (fase 2, defensa proactiva). Va embebida en infra/urponline-oneclick.yaml
# (recurso RespuestaIntrusionLambda); este archivo es la fuente legible y la que prueba infra/test_respuesta_intrusion.py.
# La dispara la alarma de conexion sospechosa (VPC Flow Logs). Sin intervencion humana:
#  1) busca en los Flow Logs que interfaz de red intento la conexion y a que instancia pertenece;
#  2) guarda un snapshot FORENSE de sus discos; 3) la AISLA (security group de cuarentena, sin trafico);
#  4) la marca no sana para que Auto Scaling la REEMPLACE por una limpia; 5) avisa por SNS.
import os, time, boto3
logs = boto3.client('logs'); ec2 = boto3.client('ec2'); asg = boto3.client('autoscaling'); sns = boto3.client('sns')
CONSULTA = ("fields interfaceId | filter dstPort in [3333,4444,5555,14444,51820] "
            "or isIpv4InSubnet(dstAddr, '203.0.113.0/24') | stats count() by interfaceId")


def instancias_sospechosas(minutos=15):
    fin = int(time.time())
    q = logs.start_query(logGroupName=os.environ['FLOW_LOGS'], startTime=fin - minutos * 60, endTime=fin,
                         queryString=CONSULTA)['queryId']
    r = {}
    for _ in range(30):
        r = logs.get_query_results(queryId=q)
        if r['status'] in ('Complete', 'Failed', 'Cancelled', 'Timeout'):
            break
        time.sleep(2)
    enis = [c['value'] for fila in r.get('results', []) for c in fila if c['field'] == 'interfaceId']
    if not enis:
        return []
    nis = ec2.describe_network_interfaces(NetworkInterfaceIds=enis)['NetworkInterfaces']
    return sorted({n['Attachment']['InstanceId'] for n in nis if n.get('Attachment', {}).get('InstanceId')})


def handler(event, context):
    hechas = []
    for i in instancias_sospechosas():
        ec2.create_snapshots(InstanceSpecification={'InstanceId': i}, Description='Forense URPOnline ' + i,
                             TagSpecifications=[{'ResourceType': 'snapshot', 'Tags': [{'Key': 'urponline', 'Value': 'forense'}]}])
        ec2.modify_instance_attribute(InstanceId=i, Groups=[os.environ['CUARENTENA_SG']])
        try:
            asg.set_instance_health(InstanceId=i, HealthStatus='Unhealthy', ShouldRespectGracePeriod=False)
        except asg.exceptions.ClientError:
            pass  # la instancia no es del Auto Scaling: queda aislada igual
        hechas.append(i)
    sns.publish(TopicArn=os.environ['TOPIC'], Subject='URPOnline: respuesta automatica a intrusion',
                Message='Conexion sospechosa en VPC Flow Logs. Instancias con snapshot forense, en cuarentena y '
                        'reemplazadas por Auto Scaling: ' + (', '.join(hechas) or 'ninguna encontrada en los Flow Logs'))
    return {'aisladas': hechas}
