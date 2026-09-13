import json
import os
import urllib.request
import urllib.error

# WI #7033: this Lambda has no DB access, so the multi-user dedup guard (skip cleanly when the
# meeting record is a subscriber, or another primary is already active for the same meeting) lives
# server-side in firm-web's POST /api/vp/autojoin/{meetingId} (MeetingsApiController.AutoJoinTrigger).
# That endpoint returns HTTP 409 for both cases, which the HTTPError handler below already treats
# as a clean, non-retrying skip — no change needed here.
def lambda_handler(event, context):
    meeting_id  = event.get('meetingId')
    meeting_url = event.get('meetingUrl')   # kept for logging; FIRM reads from DB now
    # ADO#6813: fall back to FIRM_API_URL env var when payload doesn't carry firmApiUrl
    firm_api_url = event.get('firmApiUrl') or os.environ.get('FIRM_API_URL', '')
    # ADO#6813 follow-up: fall back to BOT_CALLBACK_SECRET env var when payload
    # doesn't carry botCallbackSecret. Old EventBridge schedules (created before
    # this field was added) omit it, causing 401s on every autojoin attempt.
    bot_callback_secret = event.get('botCallbackSecret') or os.environ.get('BOT_CALLBACK_SECRET', '')

    print(f"firm-autojoin: validating meeting {meeting_id} via FIRM before ECS launch")

    if not firm_api_url:
        raise Exception("firmApiUrl not in payload or FIRM_API_URL env var — cannot validate meeting")

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
