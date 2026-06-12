import boto3
import os
from datetime import datetime, timezone

ECS_CLUSTER = os.environ.get('ECS_CLUSTER', 'fortress-tools-cluster')
ECS_SERVICE  = os.environ.get('ECS_SERVICE',  'meetings-vpbot-dev')

def handler(event, context):
    ecs = boto3.client('ecs')

    # Check running tasks
    tasks = ecs.list_tasks(cluster=ECS_CLUSTER, serviceName=ECS_SERVICE, desiredStatus='RUNNING')
    task_arns = tasks.get('taskArns', [])

    if task_arns:
        # Describe tasks to check if any have been running < 30 min (likely active meeting)
        detail = ecs.describe_tasks(cluster=ECS_CLUSTER, tasks=task_arns)
        for t in detail.get('tasks', []):
            started = t.get('startedAt')
            if started:
                age_minutes = (datetime.now(timezone.utc) - started).total_seconds() / 60
                if age_minutes < 30:
                    print(f"[ScaleGuard] Task {t['taskArn'][-12:]} started {age_minutes:.1f}m ago — skipping scale-down (likely active meeting)")
                    return {'statusCode': 200, 'body': 'scale-down skipped: active meeting'}

        # Tasks exist but all older than 30 min — scale down anyway (stale tasks)
        print(f"[ScaleGuard] {len(task_arns)} stale task(s) found (>30min). Scaling down.")
    else:
        print("[ScaleGuard] No running tasks. Scaling down.")

    ecs.update_service(
        cluster=ECS_CLUSTER,
        service=ECS_SERVICE,
        desiredCount=0
    )
    print("[ScaleGuard] Scale-down complete: desiredCount=0")
    return {'statusCode': 200, 'body': 'scaled down'}
