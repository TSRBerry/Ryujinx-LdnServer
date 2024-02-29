import json
import time
from enum import IntEnum

import dbus
import logging
import os

import requests
from dbus import DBusException
from discord_webhook import DiscordEmbed, DiscordWebhook

from .ryuldn.packets.create_access_point import CreateAccessPointPacket
from .ryuldn import RyujinxLdnClient
from .ryuldn.packets.initialize import InitializePacket

__VERSION__ = "0.1.0"
NAME = "RyuDoctor"

HOST = "ldn.ryujinx.org"
PORT = 30456

SERVER_RESTART_PING_ROLE = "1130585039890042911"

ALLOWED_MENTIONS = {
    "users": ["113928430583545863", "330753027071934486"],
    "roles": [SERVER_RESTART_PING_ROLE],
}

EMBED = DiscordEmbed("LDN server status check")
EMBED_FIELDS = {
    "Check connection": ":zzz: Waiting...",
    "Init RyuLDN": ":zzz: Waiting...",
    "Host a new game": ":zzz: Waiting...",
}
for name, value in EMBED_FIELDS.items():
    EMBED.add_embed_field(name, value, False)

HOST_AP_PACKET = CreateAccessPointPacket()
# Security config
HOST_AP_PACKET.security_mode = 0
HOST_AP_PACKET.passphrase_size = 0
HOST_AP_PACKET.passphrase = b""
# User config
HOST_AP_PACKET.username = NAME.encode()
HOST_AP_PACKET.unknown1 = b""
# Network config
HOST_AP_PACKET.local_communication_id = 0x4200000000007E27
HOST_AP_PACKET.reserved_1 = 0
HOST_AP_PACKET.scene_id = 0
HOST_AP_PACKET.reserved_2 = 0
HOST_AP_PACKET.channel = 3
HOST_AP_PACKET.node_count_max = 0xFF
HOST_AP_PACKET.reserved_3 = 0
HOST_AP_PACKET.local_communication_version = 0
HOST_AP_PACKET.reserved_4 = b""
HOST_AP_PACKET.game_version = __VERSION__.encode() + b"-TEST"
HOST_AP_PACKET.private_ip = b"\x7f\x00\x00\x01"
HOST_AP_PACKET.address_family = 2
HOST_AP_PACKET.external_proxy_port = 30456
HOST_AP_PACKET.internal_proxy_port = 31456


class TestStatus(IntEnum):
    Waiting = 0
    Running = 1
    Cancelled = 2
    Failed = 3
    Passed = 4


def send_init_embed() -> bool:
    api_success = False
    EMBED.color = 0x4D9DF2
    try:
        api_response = requests.get(f"https://{HOST}/api")
        api_response.raise_for_status()
        EMBED.set_description(
            "Current status:\n"
            f"```json\n{json.dumps(api_response.json(), indent=2)}\n```"
        )
        api_success = True
    except requests.RequestException as e:
        logging.exception("Could not receive stats from API.")
        EMBED.set_description("Unable to query API for statistics.\n" f"```\n{e}\n```")
    EMBED.set_footer(text="Initializing...")
    webhook.add_embed(EMBED)
    webhook.execute(True)
    return api_success


def send_wip_embed(test_index: int, status: TestStatus, exception: Exception = None):
    assert test_index < len(EMBED_FIELDS)

    EMBED.color = 0xE9EF32
    EMBED.set_footer(text="Running tests...")
    test_name = EMBED.get_embed_fields()[test_index]["name"]
    for i in range(len(EMBED_FIELDS)):
        EMBED.delete_embed_field(0)
    match status:
        case TestStatus.Running:
            EMBED_FIELDS[test_name] = ":hourglass_flowing_sand: Running..."
        case TestStatus.Cancelled:
            EMBED_FIELDS[test_name] = ":grey_exclamation: Cancelled."
        case TestStatus.Failed:
            EMBED_FIELDS[test_name] = (
                f":x: Failed.\n```\n{exception}\n```"
                if exception is not None
                else ":x: Failed."
            )
        case TestStatus.Passed:
            EMBED_FIELDS[test_name] = ":white_check_mark: Passed!"
        case _:
            EMBED_FIELDS[test_name] = ":question: Unknown status."
    for field_name, field_value in EMBED_FIELDS.items():
        EMBED.add_embed_field(field_name, field_value, False)

    webhook.add_embed(EMBED)
    webhook.edit()
    webhook.remove_embeds()


def send_done_embed(success: bool):
    EMBED.color = 0x67EF4F if success else 0xFC0505
    EMBED.set_footer(text="All tests succeeded!" if success else "Some tests failed.")
    EMBED.set_timestamp()
    webhook.add_embed(EMBED)
    webhook.edit()
    webhook.remove_embeds()


def send_api_message(api_success: bool):
    if api_success:
        return
    message = f":space_invader: <@&{SERVER_RESTART_PING_ROLE}> :space_invader:\n" \
              "The LDN website stopped working correctly.\n" \
              "Please take a look at the error above."
    webhook.set_content(message)
    webhook.execute(True)


def restart_service(use_webhook=True):
    message = f":warning: <@&{SERVER_RESTART_PING_ROLE}> :warning:\n" \
              "The LDN server stopped working correctly.\nRestarting..."
    if use_webhook:
        webhook.set_content(message)
        webhook.execute(True)
    logging.info("Restarting LDN service...")
    try:
        manager.RestartUnit(service_name, "fail")
        logging.info("Successfully restarted LDN service!")
    except DBusException as ex:
        logging.exception("Could not restart the LDN service.")
        if use_webhook:
            webhook.set_content(f"{message} Error.\n```{ex}\n```")
            webhook.edit()
        exit(1)

    if use_webhook:
        webhook.set_content(f"{message} Done.")
        webhook.edit()


def init():
    global manager, service_name

    if "DEBUG" in os.environ.keys():
        logging.getLogger().setLevel(logging.DEBUG)
    else:
        logging.getLogger().setLevel(logging.INFO)

    logging.debug("Getting system bus manager interface...")
    sys_bus = dbus.SystemBus()
    systemd1 = sys_bus.get_object(
        "org.freedesktop.systemd1", "/org/freedesktop/systemd1"
    )
    manager = dbus.Interface(systemd1, "org.freedesktop.systemd1.Manager")

    if "LDN_SERVICE" not in os.environ.keys() or len(os.environ["LDN_SERVICE"]) == 0:
        logging.error("LDN service name is not configured.")
        exit(1)

    service_name = os.environ["LDN_SERVICE"]
    logging.debug(f"Got LDN service name: {service_name}")


def debug_restart():
    init()
    restart_service(False)


def main():
    global webhook

    init()

    if "DC_WEBHOOK" not in os.environ.keys() or len(os.environ["DC_WEBHOOK"]) == 0:
        logging.error("Discord webhook is not configured.")
        exit(1)

    webhook_url = os.environ["DC_WEBHOOK"]
    logging.debug(f"Using discord webhook URL: {webhook_url}")
    # noinspection PyTypeChecker
    # allowed_mentions is currently wrongly typed for some reason
    webhook = DiscordWebhook(
        url=webhook_url,
        rate_limit_retry=True,
        username=NAME,
        allowed_mentions=ALLOWED_MENTIONS,
    )

    api_success = send_init_embed()

    logging.info("Trying to connect to LDN server...")
    send_wip_embed(0, TestStatus.Running)
    try:
        client = RyujinxLdnClient(HOST, PORT)
    except Exception as ex:
        logging.exception("Failed to connect to LDN server.")
        send_wip_embed(0, TestStatus.Failed, ex)
        send_wip_embed(1, TestStatus.Cancelled)
        send_wip_embed(2, TestStatus.Cancelled)
        send_done_embed(False)
        send_api_message(api_success)
        restart_service()
        exit(0)

    send_wip_embed(0, TestStatus.Passed)
    logging.info("Sending initialize packet...")
    send_wip_embed(1, TestStatus.Running)
    client.send(InitializePacket())
    logging.info("Waiting for response...")
    init_response, _ = client.receive()
    if init_response is None:
        logging.error("Failed to receive a valid response.")
        send_wip_embed(1, TestStatus.Failed)
        send_wip_embed(2, TestStatus.Cancelled)
        send_done_embed(False)
        send_api_message(api_success)
        restart_service()
        exit(0)

    logging.info(f"Initialize reply received: {init_response}")
    send_wip_embed(1, TestStatus.Passed)
    logging.info("Creating test game...")
    send_wip_embed(2, TestStatus.Running)
    client.send(HOST_AP_PACKET)
    logging.info("Game created, waiting for 5 seconds...")
    send_wip_embed(2, TestStatus.Passed)
    time.sleep(5)
    logging.info("Disconnecting...")
    client.disconnect()
    send_done_embed(True)
    send_api_message(api_success)
    logging.info("Done.")


if __name__ == "__main__":
    main()
