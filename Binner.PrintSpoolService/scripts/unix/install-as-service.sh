#!/bin/sh

is_user_root () { [ "$(id -u)" -eq 0 ]; }
if is_user_root; then
	chmod +x ./Binner.PrintSpoolService
	sed --expression "s#@INSTALLPATH@#${PWD}#g" Binner.PrintSpoolService.service.systemctl.template > /etc/systemd/system/Binner.PrintSpoolService.service
	systemctl daemon-reload
	systemctl enable Binner.PrintSpoolService.service
	systemctl start Binner.PrintSpoolService.service
	echo "Binner print service is now running
	exit 0
else
	echo "This script must be run as root user using sudo.";
	exit 1
fi
