import boto3
import os

ECS_CLUSTER = os.environ.get('ECS_CLUSTER', 'fortress-tools-cluster')
ECS_SERVICE  = os.environ.get('ECS_SERVICE',  'meetings-vpbot-dev')

def handler(event, context):
    ecs = boto3.client('ecs')
    ecs.update_service(
        cluster=ECS_CLUSTER,
        service=ECS_SERVICE,
        desiredCount=1
    )
    print("[ScaleUp] desiredCount=1 set.")
    return {'statusCode': 200, 'body': 'scaled up'}
