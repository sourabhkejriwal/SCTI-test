# k6 quote workload model

This package models the recorded insurance quote journey at the HTTP API layer. Playwright remains responsible for browser correctness; k6 generates capacity traffic against only the application's first-party endpoints.

## Status

The model has **not been executed against a real environment**. Every environment configured in this repository points at the production site, which the script refuses to target. Request shapes were taken from a recorded browser session of the journey, and the script logic was exercised against a stub of the API. Run the `smoke` profile against an authorised performance environment before relying on any figure below.

## Safety

Use a dedicated performance environment with written approval and monitoring in place. The script:

- requires an explicit `BASE_URL`;
- blocks the known SCTI production hosts;
- runs only against hosts listed in `safety.allowedHosts` in `workload.json`, which is empty by default so that adding a target is a reviewed change;
- requires `PERF_TEST_APPROVED=true` for baseline, load and stress profiles; and
- aborts if HTTP failures exceed 10% after 30 seconds.

Do not send performance traffic to third-party analytics, address lookup or payment services. The journey stops before payment: `POST /api/Policy/payPolicy` is deliberately not modelled.

## Workload model

### Transactions

Taken from the recorded journey. Response times are from that single recorded session against production and are indicative only.

| Transaction | Request | Triggered by | Recorded response |
|---|---|---|---:|
| `create_quote` | `POST /api/Quotes/create` | Get plan options | 2.7 s |
| `update_extras` | `PUT /api/Quotes/extras` | Each of: discount answer, excess, cancellation cover, extra cover | 2.0-2.1 s |
| `select_quote` | `PUT /api/Quotes/select` | Next: Traveller details | 2.0 s |
| `update_travellers` | `PUT /api/Traveller/update` | Submitting the traveller form | 1.8 s |
| `confirm_eligibility` | `PUT /api/Quotes/eligibility` | Submitting the traveller form (sent straight after the update) | 1.4 s |

Eligibility answers, the privacy declaration and the traveller form are filled in the browser and only reach the server when the form is submitted, so those steps appear as think time followed by the last two transactions.

The recorded session never called `GET /api/config`; the application fetches it only when configuration is missing. It is excluded unless `INCLUDE_CONFIG=true`.

### Journey mix

The mix is an explicit assumption until funnel analytics are available. Each journey includes every step of the one above it.

| Journey | Weight | Calls per journey |
|---|---:|---|
| `quote_only` | 50% | create |
| `customise` | 30% | create + four extras updates |
| `traveller_details` | 12% | create + four extras updates + select |
| `submit_travellers` | 8% | create + four extras updates + select + traveller update + eligibility |

Therefore 100 started journeys generate approximately 100 create calls, 200 extras calls, 20 select calls, 8 traveller updates and 8 eligibility calls: about 3.4 requests per journey.

Quote and policy identifiers are extracted from each create response and are never shared across virtual users. Excess and cancellation cover are sent as digits (`"100"`, `"5000"`), as the browser sends them, and every extras response is checked to confirm the requested options were applied. Traveller details are synthetic (`syntheticTraveller` in `workload.json`) with a unique `example.com` email per iteration.

The main policyholder request shape comes from the recording. Child travellers were not submitted in the recording, so they reuse that shape with empty contact fields; this is the one part of the script to confirm first in a smoke run.

### Think time

| Pause | Range |
|---|---|
| After the quote is shown | 2-8 s |
| Between customisations | 1-3 s |
| Before continuing to traveller details | 2-8 s |
| Filling eligibility, declaration and traveller form | 30-90 s |

### Profiles

All profiles use open-model arrival-rate executors: new journeys start at the stated rate regardless of how slowly the system responds.

| Profile | Shape | Peak requests/second | Intent |
|---|---|---:|---|
| `smoke` | one `submit_travellers` iteration, no think time | - | Validate connectivity, contracts and correlation for every transaction |
| `baseline` | 1 journey/second for 10 minutes | ~3.4 | Establish a low-load reference |
| `load` | ramp to 5 journeys/second over 5 minutes, hold 30 minutes, ramp down | ~17 | Validate assumed peak demand |
| `stress` | 10-minute plateaus at 5, 10 and 15 journeys/second, then 10 minutes back at 1 journey/second | ~51 | Find saturation and verify recovery |

The rates are assumptions: `load` stands for expected peak demand and `stress` reaches three times that. Replace them once peak demand is agreed.

Virtual users are sized from the recorded response times and think times, which give an average journey of roughly 19 seconds: about 95 concurrent VUs at 5 journeys/second and 285 at 15. Pre-allocated VUs sit above those figures (30, 150 and 400) so that k6 does not have to create VUs mid-test.

## Thresholds

Configured in `workload.json`:

| Measure | Limit |
|---|---|
| HTTP, business and journey failure rates | below 1% |
| Checks passed | above 99% |
| p95 `create_quote` | below 4.0 s |
| p95 `update_extras`, `select_quote`, `update_travellers` | below 3.0 s |
| p95 `confirm_eligibility` | below 2.5 s |
| p99 any transaction | below 6.0 s |
| Dropped iterations | none |

The latency limits are provisional: each is the single-user recorded response time plus roughly 50% headroom, because no service-level objectives were supplied. Replace them with agreed objectives, or with the measured baseline once one exists.

A stress test is expected to cross one or more thresholds. Its result should record the last sustainable rate, the bottleneck, the failure behaviour and how long the system takes to return to baseline latency during the final recovery plateau.

## Local execution

Install k6 and add the authorised non-production host to `safety.allowedHosts` in `workload.json`. Then start with a smoke test:

```bash
BASE_URL=https://performance.example.test \
PROFILE=smoke \
k6 run performance/k6/quote-journey.js
```

Run baseline, load or stress only after approval:

```bash
mkdir -p performance/results
BASE_URL=https://performance.example.test \
PROFILE=baseline \
PERF_TEST_APPROVED=true \
TEST_RUN_ID=CHG-1234-baseline \
k6 run --summary-export=performance/results/baseline.json \
  performance/k6/quote-journey.js
```

Change `PROFILE` to `load` or `stress` as required. Useful overrides are:

| Variable | Purpose |
|---|---|
| `RATE_MULTIPLIER` | Multiplies all arrival rates and VU allocation exactly, for example `0.5` turns 5 journeys/second into 2.5 |
| `THINK_TIME_SCALE` | Scales all think times; smoke defaults to zero |
| `JOURNEY_TYPE` | Forces `quote_only`, `customise`, `traveller_details` or `submit_travellers`; smoke defaults to `submit_travellers` |
| `INCLUDE_CONFIG` | Set to `true` to add `GET /api/config` at the start of every journey |
| `INSURANCE_DATE` | Fixes the insurance start date in `YYYY-MM-DD` format |
| `DEPARTURE_DATE` | Fixes the journey departure date in `YYYY-MM-DD` format |
| `AUTH_TOKEN` | Adds a bearer token when the performance environment requires one |
| `SITE_COOKIE` | Overrides the recorded `sc_site=scti-nz` first-party routing cookie |
| `TEST_RUN_ID` | Adds a correlation tag and `X-Test-Run-Id` request header |
| `REQUEST_TIMEOUT` | Overrides the default `30s` request timeout |

Endpoint paths can be overridden with `CONFIG_PATH`, `CREATE_QUOTE_PATH`, `UPDATE_EXTRAS_PATH`, `SELECT_QUOTE_PATH`, `UPDATE_TRAVELLERS_PATH` and `ELIGIBILITY_PATH`.

## Distributed workload model

Load is generated by four k6 runner pods managed by k6 Operator, each on its own node. The profile rates are global: the operator gives each runner one quarter of the workload as an execution segment, so adding runners spreads the load rather than multiplying it.

| Profile | Total peak rate | Per runner (x4) | Total pre-allocated / max VUs | Per runner |
|---|---:|---:|---:|---:|
| `baseline` | 1 journey/s | 0.25 journey/s | 30 / 100 | 8 / 25 |
| `load` | 5 journeys/s | 1.25 journeys/s | 150 / 400 | 38 / 100 |
| `stress` | 15 journeys/s | 3.75 journeys/s | 400 / 1000 | 100 / 250 |

Each runner requests 0.5 CPU and 512 MiB and is limited to 2 CPU and 2 GiB. `separate: true` schedules one runner per node, so the cluster needs at least four schedulable nodes; otherwise runners stay pending. Test data rows are assigned by a test-wide iteration counter, so runners do not repeat each other's sequence.

### Results and thresholds

Each runner sees only its own quarter of the traffic. Thresholds, including the 10% error abort, are evaluated per runner, and a runner's end-of-test summary describes only that runner. The pass or fail decision for a distributed run must therefore come from aggregated metrics.

The manifest sends metrics from every runner to Prometheus remote write (`K6_PROMETHEUS_RW_SERVER_URL`). Point it at your Prometheus endpoint, and apply the thresholds above to the aggregated `transaction`-tagged series in Grafana. Keep any credentials in a Kubernetes `Secret`, not in this repository.

### Running

Install k6 Operator in an authorised Kubernetes cluster. Then create one ConfigMap containing the script and its two data files:

```bash
kubectl create configmap scti-quote-wlm \
  --from-file=quote-journey.js=performance/k6/quote-journey.js \
  --from-file=workload.json=performance/k6/workload.json \
  --from-file=journeys.json=performance/k6/journeys.json
```

Edit `performance/kubernetes/k6-testrun.yaml` to set the approved non-production URL, profile, run ID and Prometheus endpoint, and rename the `TestRun` to match the profile. Change `PERF_TEST_APPROVED` to `true` only after approval, then run:

```bash
kubectl apply -f performance/kubernetes/k6-testrun.yaml
kubectl get testrun scti-quote-load
kubectl get pods -l k6_cr=scti-quote-load
```

Before increasing load, confirm every generator has spare CPU, memory and network capacity and that application, database and dependency telemetry is visible.
