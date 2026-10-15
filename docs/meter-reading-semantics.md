# Installed Envoy meter-reading semantics

## Read-only verification

An authenticated read-only audit on 2026-10-15 compared these **direct Envoy**
endpoints, not just the EnphaseLocal application:

| Endpoint | Purpose |
| --- | --- |
| `/ivp/meters` | Enabled meter identities, measurement types, split-phase configuration and status flags |
| `/ivp/meters/readings` | Physical meter and per-phase readings keyed by meter `eid` |
| `/ivp/meters/reports` | Envoy-generated production, net-consumption and total-consumption reports |
| `/production.json` | The report-derived payload consumed by EnphaseLocal |

Use the configured Envoy bearer credential through the existing secret mechanism;
never put it in documentation, shell history, diagnostic output or test fixtures.
The installed Envoy presents a manufacturer/device TLS certificate, not a public
certificate for its local DNS alias. This audit pinned the device certificate for
the bounded requests; no TLS or meter settings were changed.

## Findings

Meter configuration identifies an enabled, normal, two-phase `production` meter
and an enabled, normal, two-phase `total-consumption` meter. Neither reports status
flags. Storage and the additional optional meters are disabled.

Adjacent direct readings were:

| Source | Watts |
| --- | ---: |
| Physical production meter | 5887.466 |
| Physical total-consumption meter | 1717.557 |
| Generated production report | 5881.192 |
| Generated net-consumption report | -4175.004 |
| Generated total-consumption report | -4175.004 |
| Household consumption reconstructed from generated production + net | 1706.188 |

The reconstructed household consumption differs from the adjacent physical reading
by about 11.4 W (0.7%). The requests are not an atomic simultaneous snapshot, and
the meter/report update cadence differs. This is corroborating evidence, not an
accuracy specification or calibration certificate.

Both `/ivp/meters/reports` and `/production.json` duplicate the signed grid-flow
value into their generated `total-consumption` entry. The positive physical meter
reading is separate. Therefore the duplication originates in Envoy's generated
reporting path, not in EnphaseLocal's DTO mapping. Do not reinterpret the physical
total-consumption meter itself as signed grid flow, nor use the generated
total-consumption value directly as household load.

Per-phase consumption power factors were 0.879 and 0.796; the aggregate field was
1.676. The aggregate behaves as a phase sum (with slightly different update times),
not as a single physical power factor. An aggregate above 1 alone is not evidence
of reversed CTs. Production similarly reports approximately 1 per phase and 2 in
the aggregate. Inspect the individual phases before diagnosing wiring.

## Application behavior

EnphaseLocal continues to use one `/production.json` response for its normalized
Power Metrics snapshot. For an identified EIM net-consumption reading:

```text
household consumption = production + signed net-consumption
net production        = -signed net-consumption
```

Positive net-consumption means grid Import; positive normalized net production
means Export. The duplicated generated total is not added again. Raw application
endpoints remain unmodified for diagnostics, and no CT configuration was changed.

## Physical verification boundary

The meter flags, phase values and agreement with the separate physical household
meter provide no evidence requiring a CT reversal or a software sign-flip override.
They cannot certify CT placement, utility-meter accuracy, or coverage of every
household circuit. If physical validation is needed, compare the utility meter's
direction and power during a naturally occurring stable load, preferably with
negligible solar. The expected import is approximately household consumption.
Only a qualified installer should inspect or change CT placement or phase wiring;
do not alter live metering configuration merely to force displayed values to agree.
