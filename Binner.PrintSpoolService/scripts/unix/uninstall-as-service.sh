#!/bin/sh

is_user_root () { [ "$(id -u)" -eq 0 ]; }
if is_user_root; then
	systemctl stop Binner.PrintSpoolService.service
	systemctl disable Binner.PrintSpoolService.service
	rm /etc/systemd/system/Binner.PrintSpoolService.service
	systemctl daemon-reload
	systemctl reset-failed

	echo "Binner print service is uninstalled!"
	exit 0
else
	echo "This script must be run as root user using sudo.";
	exit 1
fi
