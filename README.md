# RatioMaster HTTPS
[![Release](https://img.shields.io/github/v/release/massmas/ratiomaster-https?style=for-the-badge)](https://github.com/massmas/ratiomaster-https/releases/latest)
![Last commit](https://img.shields.io/github/last-commit/massmas/ratiomaster-https?style=for-the-badge&color=00AD00)

## About

Fork of [RatioMaster](https://github.com/sergiye/RatioMaster) with native **HTTPS tracker support**.

RatioMaster is a small standalone application which fakes upload and download stats of a torrent to almost all bittorrent trackers. It does NOT rely on your bittorrent client and will NOT download/upload files — it only fakes download/upload stats.

Supported clients: uTorrent, BitComet, Azureus, ABC, BitLord, BTuga, BitTornado, Burst, BitTyrant, BitSpirit.

## What's new in this fork

- **Native HTTPS support** — the original used raw TCP sockets which failed on HTTPS-only trackers (e.g. Cloudflare-protected). This fork uses `SslStream` with proper TLS handshake and `Connection: close` to correctly communicate with HTTPS trackers.

## Build

Requires Visual Studio 2019+ with .NET Framework 4.7.2.

```
MSBuild RatioMaster\RatioMaster.csproj /p:Configuration=Release /t:Restore,Build
```

The compiled EXE will be at `RatioMaster\bin\RatioMaster.exe`.

## Credits

Based on [sergiye/RatioMaster](https://github.com/sergiye/RatioMaster) by Sergiy Egoshyn — licensed under GPL v3.

