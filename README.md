# RatioMaster HTTPS
[![Release](https://img.shields.io/github/v/release/massmas/ratiomaster-https?style=for-the-badge)](https://github.com/massmas/ratiomaster-https/releases/latest)
![Last commit](https://img.shields.io/github/last-commit/massmas/ratiomaster-https?style=for-the-badge&color=00AD00)

## About

Fork of [RatioMaster](https://github.com/sergiye/RatioMaster) with native **HTTPS tracker support**.

RatioMaster is a small standalone application which fakes upload and download stats of a torrent to almost all bittorrent trackers. It does NOT rely on your bittorrent client and will NOT download/upload files — it only fakes download/upload stats.

Supported clients: uTorrent, BitComet, Azureus, ABC, BitLord, BTuga, BitTornado, Burst, BitTyrant, BitSpirit.

## What's new in this fork

- **Native HTTPS support** — the original used raw TCP sockets which failed on HTTPS-only trackers (e.g. Cloudflare-protected). This fork uses `SslStream` with proper TLS handshake and `Connection: close` to correctly communicate with HTTPS trackers.
- **Modernised UI** — fully rewritten in WPF with a dark/light theme toggle, adaptive log window, responsive title bar buttons, and improved contrast throughout.
- **Seeding fix** — announces start even when the tracker returns an empty peer list.
- **Security** — tracker TLS certificates are verified (your passkey can't be sniffed by a man-in-the-middle), proxy passwords and PC info are no longer written to the log.

## Download

Grab `RatioMaster.exe` from the [latest release](https://github.com/massmas/ratiomaster-https/releases/latest). Every release binary is built by [GitHub Actions](.github/workflows/release.yml) straight from this source — no hand-uploaded executables. The SHA-256 of each file is shown on the release page.

## Known limitations

- HTTPS trackers can't be used through a proxy (the request is refused rather than sent unencrypted).

## Build

Requires Visual Studio 2019+ with .NET Framework 4.7.2.

```
MSBuild RatioMaster\RatioMaster.csproj /p:Configuration=Release /t:Restore,Build
```

The compiled EXE will be at `RatioMaster\bin\RatioMaster.exe`.

## Credits

Based on [sergiye/RatioMaster](https://github.com/sergiye/RatioMaster) by Sergiy Egoshyn — licensed under GPL v3.

## Disclaimer

Faking stats breaks the rules of most private trackers and can get your account banned. Use at your own risk. This software is provided as-is, without warranty (see [LICENSE](LICENSE.txt)).

