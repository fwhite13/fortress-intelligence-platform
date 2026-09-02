import json
import boto3
import os

ecs = boto3.client('ecs', region_name='us-east-1')

CLUSTER         = os.environ['ECS_CLUSTER']
TASK_DEF        = os.environ['VPBOT_TASK_DEF']
TASK_ROLE_ARN   = os.environ['TASK_ROLE_ARN']
EXEC_ROLE_ARN   = os.environ['EXEC_ROLE_ARN']
SUBNETS         = os.environ['SUBNETS'].split(',')
SECURITY_GROUPS = os.environ['SECURITY_GROUPS'].split(',')

def lambda_handler(event, context):
    meeting_id  = event.get('meetingId')
    meeting_url = event.get('meetingUrl')
    firm_api_url = event.get('firmApiUrl', '')
    bot_callback_secret = event.get('botCallbackSecret', '')

    print(f"firm-autojoin: launching vpbot for meeting {meeting_id} url={meeting_url} apiUrl={firm_api_url}")

    env_overrides = [
        {'name': 'MEETING_URL',          'value': meeting_url or ''},
        {'name': 'MEETING_ID',           'value': str(meeting_id or '')},
    ]
    if firm_api_url:
        env_overrides.append({'name': 'FIRM_API_URL', 'value': firm_api_url})
    if bot_callback_secret:
        env_overrides.append({'name': 'BOT_CALLBACK_SECRET', 'value': bot_callback_secret})

    response = ecs.run_task(
        cluster=CLUSTER,
        taskDefinition=TASK_DEF,
        launchType='FARGATE',
        networkConfiguration={
            'awsvpcConfiguration': {
                'subnets': SUBNETS,
                'securityGroups': SECURITY_GROUPS,
                'assignPublicIp': 'ENABLED'
            }
        },
        overrides={
            'taskRoleArn': TASK_ROLE_ARN,
            'executionRoleArn': EXEC_ROLE_ARN,
            'containerOverrides': [{
                'name': 'firm-vpbot',
                'environment': env_overrides
            }]
        }
    )

    failures = response.get('failures', [])
    if failures:
        raise Exception(f"ECS RunTask failed: {failures}")

    task_arn = response['tasks'][0]['taskArn']
    print(f"firm-autojoin: launched task {task_arn}")
    return {'taskArn': task_arn}
