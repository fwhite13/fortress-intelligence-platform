import json
import urllib.request
import urllib.error

def lambda_handler(event, context):
    meeting_id  = event.get('meetingId')
    meeting_url = event.get('meetingUrl')   # kept for logging; FIRM reads from DB now
    firm_api_url = event.get('firmApiUrl', '')
    bot_callback_secret = event.get('botCallbackSecret', '')

    print(f"firm-autojoin: validating meeting {meeting_id} via FIRM before ECS launch")

    if not firm_api_url:
        raise Exception("firmApiUrl not in payload — cannot validate meeting")

    url = f"{firm_api_url}/api/vp/autojoin/{meeting_id}"
    req = urllib.request.Request(
        url,
        method='POST',
        headers={
            'Content-Type': 'application/json',
            'X-Bot-Secret': bot_callback_secret,
        },
        data=b'{}'
    )

    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = json.loads(resp.read().decode())
            task_arn = body.get('taskArn')
            print(f"firm-autojoin: FIRM launched task {task_arn} for meeting {meeting_id}")
            return {'taskArn': task_arn}
    except urllib.error.HTTPError as e:
        if e.code in (404, 409):
            body = e.read().decode()
            print(f"firm-autojoin: FIRM returned {e.code} for meeting {meeting_id} — skipping ECS launch. Body: {body}")
            return {'skipped': True, 'reason': f'HTTP {e.code}', 'meetingId': meeting_id}
        raise
