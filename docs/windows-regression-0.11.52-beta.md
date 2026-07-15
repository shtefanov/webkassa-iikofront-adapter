# Windows/iikoFront Regression — 0.11.52-beta

Date: 15-07-2026

## Scope

This beta combines the changes accumulated after `0.11.50-beta`:

- complete Webkassa request deadlines and guarded Code 505 alternative-domain
  handling without blind fiscal retries;
- serialized unknown-result recovery before another fiscal write can overtake
  the unresolved operation;
- protected-secret status, masking, temporary reveal, and explicit edit UX;
- authentication-mode Base URL defaults with an editable safe
  `*.webkassa.kz` HTTPS origin;
- print and external-ticket QR actions for closed and past orders;
- one non-blocking update availability check per iikoFront process.

## Package

```text
Resto.Front.Api.Webkassa.V9-0.11.52-beta-20260715-083814.zip
size: 401983
sha256: 99e3e0e45ef062f39fa8bf1b79113057dec9f1b918d10d3d5185821e35f67bc7
```

The archive CRC, required root files, `VERSION`, Manifest.xml identity, V9 API
identity, safe paths, and absence of forbidden secret filenames were checked
again before GitHub publication.

## Passed

- Gateway contract suite for sidecar, fiscal recovery, settings, updater, and
  plugin source contracts.
- Windows Release build/package and installation on the controlled test
  iikoFront terminal.
- Installed plugin `VERSION=0.11.52-beta`; sidecar health remained OK and the
  existing DPAPI configuration was preserved.
- Settings UI showed configured/masked secret state and explicit temporary
  reveal/edit controls without writing secret values to the validation result.
- Closed-order and past-order fiscal receipt print actions.
- Closed-order and past-order external-ticket QR display.
- Successful manual print no longer interrupts the operator with a modal
  success message; errors remain visible.
- Startup update check remained non-blocking. With the website beta still at
  `0.11.45-beta`, the newer installed pilot correctly showed no update notice.

## Not promoted to stable

This is a beta/pilot release. A new full production fiscal regression and
end-to-end signature verification have not been completed. The stable channel
must remain unpublished.
