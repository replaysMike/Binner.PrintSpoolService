# Binner.PrintSpoolService
Provides label printing capability for a remote printing device installed on another network or machine.

## Installation

### Installation on Windows

Download the Windows installer from the [latest release](https://github.com/replaysMike/Binner.PrintSpoolService/releases)
After installing, edit the `appsettings.json` file to specify the location of your Binner installation. 

For a local Binner installation, use `https://SERVER_IP:8090` for the PublicUrl value:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://SERVER_IP:8090",
  }
}
```

If you are using [Binner cloud](https://binner.io), use `https://binner.io` for the PublicUrl value:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://binner.io",
  }
}
```

You will also need to provide your PrintSpoolQueueId, which can be found in the Binner UI at Settings => Organization Settings => PrintSpoolQueueId.

Example:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://binner.io",
    "PrintSpoolQueueId": "f137e85f-23af-4a43-965b-b1c0197da74d",
  }
}
```

### Installation on other platforms such as Unix / Raspberry Pi:

Download the archive file for the platform of your choice from the [latest release](https://github.com/replaysMike/Binner.PrintSpoolService/releases)
Download and extract to a folder on your target machine.

```
// extract the archive
tar zxfp ./Binner.PrintSpoolService_linux-x64-VERSION.tar.gz

// rename the default configuration file to use as your configuration
rn appsettings.default.json appsettings.json

// to install as a service
sudo chmod +x ./install-as-service.sh
sudo ./install-as-service.sh

// or you can just run directly
sudo chmod +x ./Binner.PrintSpoolService
./Binner.PrintSpoolService
```

For a local Binner installation, use `https://SERVER_IP:8090` for the PublicUrl value:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://SERVER_IP:8090",
  }
}
```

If you are using [Binner cloud](https://binner.io), use `https://binner.io` for the PublicUrl value:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://binner.io",
  }
}
```

You will also need to provide your PrintSpoolQueueId, which can be found in the Binner UI at Settings => Organization Settings => PrintSpoolQueueId.

Example:
```json
{
  "PrintConfiguration": {
    "PublicUrl": "https://binner.io",
    "PrintSpoolQueueId": "f137e85f-23af-4a43-965b-b1c0197da74d",
  }
}
```

## Getting Help

Help with your installation, questions, reporting bugs, suggesting features can be provided in multiple ways:

* If you simply have a question or need help with installation issues, you have 2 options:
  * create a post in [Discussions](https://github.com/replaysMike/Binner/discussions)
  * join our [Discord server](https://discord.gg/74GEJY5g7G) for a more real-time response
* If you are reporting a bug or suggesting a feature, create a [New Issue](https://github.com/replaysMike/Binner.PrintSpoolService/issues)
