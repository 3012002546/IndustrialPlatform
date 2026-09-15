# PF-06 shared-media PoC

This is an isolated browser sample for the PF-06 001＋009 gate. It exercises the final media shape only:

- one `RTCPeerConnection`;
- exactly three slots in order: `audio`, `screenLow`, `screenHigh`;
- Low creates the slots and the first offer; High maps the remote m-lines;
- low/high screen publishing uses its fixed video slot;
- microphone capture is audio-only; screen capture requests `audio: false`;
- capability generations reject late capture results; voice and screen stop independently.

It is not a SignalR, authorization, TURN, or production-chat implementation. `BroadcastChannel` is only a same-origin local signaling shim for the sample, so it is suitable for two tabs on one machine, not a two-device acceptance path.

## Run

Serve this directory from `localhost` or HTTPS, then open two tabs:

```powershell
python -m http.server 8787 --directory tools/pf06-media-poc
```

- Low: `http://localhost:8787/?role=low&room=demo`
- High: `http://localhost:8787/?role=high&room=demo`

Use the buttons in both tabs. Browser microphone and screen-picker prompts must be accepted by the operator. The screen capture deliberately does not request system audio.

For a future real-device run, replace the local signaling shim with the already-approved CollaborationHub path and load the same page from a trusted HTTPS origin on both devices. Record OS/browser/version, display scale, capture size, network path, TURN candidate type, and the exact room/sample revision before operating it. Do not treat localhost, fake media, or this same-machine BroadcastChannel path as cross-device evidence.

Click `Export stats` after each scenario. The JSON contains only role/room labels, timestamps, fixed event labels, and RTP byte/frame counters. For stop evidence, first collect at least 5 seconds of changing counters, click the relevant stop button, export after the 5-second normal-stop window (or after 30 seconds plus at most 5 seconds for a deliberately silent control path), then continue observing for 10 seconds. Compare the affected audio/video counters separately; the remaining capability must continue growing.

## Automated check

The pure protocol model has no dependency on the frontend or a browser:

```powershell
node --test tools/pf06-media-poc/protocol.test.mjs
node --check tools/pf06-media-poc/poc.js
```

The automated check proves slot ordering/mapping, one active screen slot, independent capability generations, stale signaling rejection, and bounded negotiation/candidate queues. It does not prove real two-device, HTTPS, TURN, permission, quality, or stop timing.
