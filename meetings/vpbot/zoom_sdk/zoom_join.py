#!/usr/bin/env python3
"""
Zoom Meeting SDK join script (WI #7024).

Joins a Zoom meeting via the Zoom Meeting SDK for Linux (no browser, no
Cloudflare) and streams mixed PCM audio into a named FIFO for ffmpeg to
record. Uses the `zoom-meeting-sdk` PyPI package (py-zoom-meeting-sdk,
https://github.com/noah-duncan/py-zoom-meeting-sdk). That package's
pyproject.toml cibuildwheel step copies the Zoom Linux .so directly into
the wheel it builds, so `pip install zoom-meeting-sdk` is sufficient --
no separate SDK download/vendoring step is needed.

Deviations from the original implementation brief, found by reading that
project's own sample_program (sample.py / meeting_bot.py) and C++
bindings on GitHub before writing this script:

- Raw audio subscription only supports 32kHz or 48kHz sampling
  (AudioRawdataSamplingRate_32K / _48K) -- 16kHz is not an SDK option.
  This script requests 32kHz by default and reports the actual rate on
  stderr as `[ZoomSDK] Audio started (sample_rate=<n>)`. The Node side
  (zoom-sdk-bot.ts) must run ffmpeg with `-ar` matching that rate, not
  a hardcoded 16000.
- The auth JWT payload the SDK's own sample program builds is
  {appKey, iat, exp, tokenExp} signed HS256 -- there is no mn/role
  field for the generic SDKAuth() call used here (the meeting number is
  passed separately via JoinParam, not embedded in the auth token).
  This script also sets a matching `sdkKey` field for compatibility,
  but does not include `mn`/`role`.
- Auth, join, and audio start are all asynchronous and driven by the
  underlying C++ SDK's callback/event system, not a synchronous call
  sequence -- this script runs a GLib main loop (as the SDK's own
  sample program does) and drives everything from SDK callbacks.
- Starting raw audio recording requires "local recording privilege".
  If CanStartRawRecording() fails, this script requests that privilege
  and retries once it's granted, matching the SDK sample's behavior.
- audio_ctrl.JoinVoip() is called on join -- the SDK sample notes raw
  audio input is broken after SDK 6.3.5 without this workaround
  (see https://devforum.zoom.us/t/cant-record-audio-with-linux-meetingsdk-after-6-3-5-6495-error-code-32/130689/5).
"""

import argparse
import base64
import hashlib
import hmac
import json
import os
import sys
import time

import gi
gi.require_version('GLib', '2.0')
from gi.repository import GLib

import zoom_meeting_sdk as zoom

SUPPORTED_SAMPLE_RATES = {
    32000: zoom.AudioRawdataSamplingRate.AudioRawdataSamplingRate_32K,
    48000: zoom.AudioRawdataSamplingRate.AudioRawdataSamplingRate_48K,
}
DEFAULT_SAMPLE_RATE = 32000


def log(msg: str) -> None:
    print(msg, file=sys.stderr, flush=True)


def generate_jwt(sdk_key: str, sdk_secret: str) -> str:
    """Build the SDK auth JWT using only stdlib hmac/hashlib/base64/json.

    Payload fields mirror what py-zoom-meeting-sdk's own sample program
    sends to SDKAuth() (appKey/iat/exp/tokenExp) -- see module docstring.
    """
    header = {"alg": "HS256", "typ": "JWT"}
    now = int(time.time())
    payload = {
        "sdkKey": sdk_key,
        "appKey": sdk_key,
        "iat": now,
        "exp": now + 86400,
        "tokenExp": now + 86400,
    }

    def b64url(data: bytes) -> str:
        return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")

    header_b64 = b64url(json.dumps(header, separators=(",", ":")).encode())
    payload_b64 = b64url(json.dumps(payload, separators=(",", ":")).encode())
    signing_input = f"{header_b64}.{payload_b64}".encode()
    signature = hmac.new(sdk_secret.encode(), signing_input, hashlib.sha256).digest()
    return f"{header_b64}.{payload_b64}.{b64url(signature)}"


class ZoomJoinBot:
    def __init__(self, args: argparse.Namespace, quit_fn):
        self.args = args
        self.sample_rate = args.audio_sample_rate
        self._quit_fn = quit_fn

        self.meeting_service = None
        self.setting_service = None
        self.auth_service = None

        self.meeting_service_event = None
        self.auth_event = None
        self.recording_event = None

        self.recording_ctrl = None
        self.audio_ctrl = None
        self.audio_helper = None
        self.audio_source = None

        self.fifo_fd = None
        self.audio_started = False
        self.chat_sent = False
        self.reached_in_meeting = False
        self.exiting = False
        self.exit_code = 0

    # -- FIFO -----------------------------------------------------------

    def _try_open_fifo(self) -> bool:
        if self.fifo_fd is not None:
            return True
        try:
            fd = os.open(self.args.fifo_path, os.O_WRONLY | os.O_NONBLOCK)
        except OSError as e:
            log(f"[ZoomSDK] FIFO not ready for writing yet ({e}) -- will retry")
            return False
        os.set_blocking(fd, True)
        self.fifo_fd = fd
        log(f"[ZoomSDK] FIFO opened for writing: {self.args.fifo_path}")
        return True

    def on_mixed_audio_raw_data_received(self, data) -> None:
        if not self.audio_started:
            self.audio_started = True
            log(f"[ZoomSDK] Audio started (sample_rate={data.GetSampleRate()})")
            # Send chat announcement once after audio starts (non-fatal)
            if not self.chat_sent:
                self.chat_sent = True
                self.send_chat_announcement()

        if not self._try_open_fifo():
            return

        try:
            os.write(self.fifo_fd, data.GetBuffer())
        except BrokenPipeError:
            log("[ZoomSDK] FIFO reader (ffmpeg) went away -- exiting")
            self.request_exit(1)
        except OSError as e:
            log(f"[ZoomSDK] Error writing to FIFO: {e}")

    def _close_fifo(self) -> None:
        if self.fifo_fd is not None:
            try:
                os.close(self.fifo_fd)
            except OSError:
                pass
            self.fifo_fd = None

    # -- SDK lifecycle ----------------------------------------------------

    def init_sdk(self) -> None:
        init_param = zoom.InitParam()
        init_param.strWebDomain = "https://zoom.us"
        init_param.strSupportUrl = "https://zoom.us"
        init_param.enableGenerateDump = True
        init_param.emLanguageID = zoom.SDK_LANGUAGE_ID.LANGUAGE_English
        init_param.enableLogByDefault = True

        result = zoom.InitSDK(init_param)
        if result != zoom.SDKERR_SUCCESS:
            raise RuntimeError(f"InitSDK failed: {result}")

        self.meeting_service = zoom.CreateMeetingService()
        self.setting_service = zoom.CreateSettingService()

        self.meeting_service_event = zoom.MeetingServiceEventCallbacks(
            onMeetingStatusChangedCallback=self.on_meeting_status_changed
        )
        if self.meeting_service.SetEvent(self.meeting_service_event) != zoom.SDKERR_SUCCESS:
            raise RuntimeError("MeetingService SetEvent failed")

        self.auth_service = zoom.CreateAuthService()
        self.auth_event = zoom.AuthServiceEventCallbacks(
            onAuthenticationReturnCallback=self.on_auth_return
        )
        self.auth_service.SetEvent(self.auth_event)

    def authenticate(self) -> None:
        auth_context = zoom.AuthContext()
        auth_context.jwt_token = generate_jwt(self.args.sdk_key, self.args.sdk_secret)
        result = self.auth_service.SDKAuth(auth_context)
        if result != zoom.SDKError.SDKERR_SUCCESS:
            raise RuntimeError(f"SDKAuth call failed: {result}")

    def on_auth_return(self, result) -> None:
        if result != zoom.AUTHRET_SUCCESS:
            log(f"[ZoomSDK] Authentication failed: {result}")
            self.request_exit(1)
            return
        log("[ZoomSDK] Authentication successful")
        self.join_meeting()

    def join_meeting(self) -> None:
        log(f"[ZoomSDK] Joining meeting {self.args.meeting_id}")

        join_param = zoom.JoinParam()
        join_param.userType = zoom.SDKUserType.SDK_UT_WITHOUT_LOGIN

        p = join_param.param
        p.meetingNumber = int(self.args.meeting_id)
        p.userName = self.args.display_name
        p.psw = self.args.password
        p.isVideoOff = True
        p.isAudioOff = False
        p.isMyVoiceInMix = False
        p.isAudioRawDataStereo = False
        p.eAudioRawdataSamplingRate = SUPPORTED_SAMPLE_RATES[self.sample_rate]
        if self.args.obf_token:
            # WI #7246: OBF (on-behalf-of) token, required to join a meeting hosted outside the
            # app owner's Zoom account. Field confirmed present on the underlying JoinParam4WithoutLogin
            # binding (see py-zoom-meeting-sdk src/binding/meeting_service_interface_binding.cpp).
            p.onBehalfToken = self.args.obf_token
        join_param.param = p

        result = self.meeting_service.Join(join_param)
        if result != zoom.SDKERR_SUCCESS:
            log(f"[ZoomSDK] Join call failed: {result}")
            self.request_exit(1)
            return

        audio_settings = self.setting_service.GetAudioSettings()
        audio_settings.EnableAutoJoinAudio(True)

    def on_meeting_status_changed(self, status, result) -> None:
        log(f"[ZoomSDK] Meeting status changed: {status} (result={result})")

        if status == zoom.MEETING_STATUS_INMEETING:
            self.reached_in_meeting = True
            log("[ZoomSDK] In meeting")
            self.on_join()
        elif status == zoom.MEETING_STATUS_FAILED:
            log(f"[ZoomSDK] Join failed (code={result})")
            self.request_exit(1)
        elif status in (zoom.MEETING_STATUS_ENDED, zoom.MEETING_STATUS_DISCONNECTING):
            log("[ZoomSDK] Meeting ended — exiting")
            self.request_exit(0)

    def on_join(self) -> None:
        # Workaround for a known SDK regression: raw audio input silently
        # produces nothing after 6.3.5 unless the bot also joins VoIP audio.
        # https://devforum.zoom.us/t/cant-record-audio-with-linux-meetingsdk-after-6-3-5-6495-error-code-32/130689/5
        self.audio_ctrl = self.meeting_service.GetMeetingAudioController()
        self.audio_ctrl.JoinVoip()

        self.recording_ctrl = self.meeting_service.GetMeetingRecordingController()

        def on_recording_privilege_changed(can_record: bool) -> None:
            log(f"[ZoomSDK] Recording privilege changed: can_record={can_record}")
            if can_record:
                GLib.timeout_add_seconds(1, self.start_raw_recording)

        self.recording_event = zoom.MeetingRecordingCtrlEventCallbacks(
            onRecordPrivilegeChangedCallback=on_recording_privilege_changed
        )
        self.recording_ctrl.SetEvent(self.recording_event)

        GLib.timeout_add_seconds(1, self.start_raw_recording)
        self.send_chat_announcement()

    def send_chat_announcement(self) -> None:
        """Send a chat announcement to all meeting participants. Non-fatal."""
        announce_name = os.environ.get("BOT_CHAT_ANNOUNCE_NAME", "")
        if not announce_name:
            log("[ZoomSDK] Chat skipped — BOT_CHAT_ANNOUNCE_NAME not set")
            return
        try:
            chat_ctrl = self.meeting_service.GetMeetingChatController()
            if chat_ctrl is None:
                log("[ZoomSDK] Chat failed — GetMeetingChatController returned None")
                return
            message = f"I'm here to take notes for {announce_name}. I'll send a summary when the meeting ends."
            builder = chat_ctrl.GetChatMessageBuilder()
            builder.SetReceiver(0)  # 0 = send to all
            builder.SetContent(message)
            result = chat_ctrl.SendChatMsgTo(builder.Build())
            log(f"[ZoomSDK] Chat sent (result={result})")
        except Exception as e:
            log(f"[ZoomSDK] Chat failed — {e}")

    def start_raw_recording(self) -> bool:
        can_start = self.recording_ctrl.CanStartRawRecording()
        if can_start != zoom.SDKERR_SUCCESS:
            log("[ZoomSDK] Cannot start raw recording yet — requesting local recording privilege")
            self.recording_ctrl.RequestLocalRecordingPrivilege()
            return False

        start_result = self.recording_ctrl.StartRawRecording()
        if start_result != zoom.SDKERR_SUCCESS:
            log(f"[ZoomSDK] StartRawRecording failed: {start_result}")
            self.request_exit(1)
            return False

        self.audio_helper = zoom.GetAudioRawdataHelper()
        if self.audio_helper is None:
            log("[ZoomSDK] GetAudioRawdataHelper returned None")
            self.request_exit(1)
            return False

        self.audio_source = zoom.ZoomSDKAudioRawDataDelegateCallbacks(
            onMixedAudioRawDataReceivedCallback=self.on_mixed_audio_raw_data_received
        )
        subscribe_result = self.audio_helper.subscribe(self.audio_source, False)
        log(f"[ZoomSDK] Audio helper subscribe result: {subscribe_result}")
        if subscribe_result != zoom.SDKERR_SUCCESS:
            self.request_exit(1)
        return False

    # -- Shutdown -----------------------------------------------------

    def leave(self) -> None:
        if self.meeting_service is None:
            return
        try:
            if self.meeting_service.GetMeetingStatus() != zoom.MEETING_STATUS_IDLE:
                self.meeting_service.Leave(zoom.LEAVE_MEETING)
        except Exception as e:
            log(f"[ZoomSDK] Error leaving meeting: {e}")

    def cleanup(self) -> None:
        self._close_fifo()
        try:
            if self.audio_helper:
                self.audio_helper.unSubscribe()
        except Exception:
            pass
        try:
            if self.meeting_service:
                zoom.DestroyMeetingService(self.meeting_service)
            if self.setting_service:
                zoom.DestroySettingService(self.setting_service)
            if self.auth_service:
                zoom.DestroyAuthService(self.auth_service)
        except Exception as e:
            log(f"[ZoomSDK] Error during service cleanup: {e}")
        try:
            zoom.CleanUPSDK()
        except Exception as e:
            log(f"[ZoomSDK] Error during CleanUPSDK: {e}")

    def request_exit(self, code: int) -> bool:
        if self.exiting:
            return False
        self.exiting = True
        self.exit_code = code
        self.leave()
        self.cleanup()
        self._quit_fn()
        return False


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Join a Zoom meeting via the Zoom Meeting SDK")
    parser.add_argument("--meeting-id", required=True, help="Numeric Zoom meeting number")
    parser.add_argument("--password", required=True, help="Zoom meeting password")
    parser.add_argument("--display-name", required=True, help="Bot display name")
    parser.add_argument("--sdk-key", required=True, help="Zoom Meeting SDK key")
    parser.add_argument("--sdk-secret", required=True, help="Zoom Meeting SDK secret")
    parser.add_argument("--fifo-path", required=True, help="Path to the FIFO to write raw PCM audio to")
    parser.add_argument(
        "--obf-token",
        default=None,
        help="Zoom OBF (on-behalf-of) token, required to join a meeting hosted outside the "
             "app owner's Zoom account (WI #7246). Optional -- falls back to JWT-only join.",
    )
    parser.add_argument(
        "--audio-sample-rate",
        type=int,
        default=DEFAULT_SAMPLE_RATE,
        help="Requested raw audio sample rate. The SDK only supports 32000 or 48000; "
             "other values (e.g. the historical 16000 default used elsewhere in this "
             "pipeline) are coerced to 32000.",
    )
    args = parser.parse_args()
    if args.audio_sample_rate not in SUPPORTED_SAMPLE_RATES:
        log(f"[ZoomSDK] Unsupported --audio-sample-rate {args.audio_sample_rate} — using {DEFAULT_SAMPLE_RATE}")
        args.audio_sample_rate = DEFAULT_SAMPLE_RATE
    return args


def main() -> None:
    args = parse_args()
    main_loop = GLib.MainLoop()
    bot = ZoomJoinBot(args, quit_fn=main_loop.quit)

    max_hours = float(os.environ.get("FIRM_MAX_MEETING_HOURS", "4"))

    def on_sigterm(signum, frame):
        log("[ZoomSDK] SIGTERM received — leaving meeting")
        GLib.idle_add(bot.request_exit, 0)

    import signal
    signal.signal(signal.SIGTERM, on_sigterm)

    def on_timeout() -> bool:
        log(f"[ZoomSDK] FIRM_MAX_MEETING_HOURS ({max_hours}) reached — leaving meeting")
        bot.request_exit(0)
        return False

    GLib.timeout_add_seconds(int(max_hours * 3600), on_timeout)

    try:
        bot.init_sdk()
        bot.authenticate()
    except Exception as e:
        log(f"[ZoomSDK] Fatal init/auth error: {e}")
        sys.exit(1)

    try:
        main_loop.run()
    except KeyboardInterrupt:
        bot.request_exit(0)

    sys.exit(bot.exit_code)


if __name__ == "__main__":
    main()
