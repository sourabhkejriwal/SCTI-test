import exec from 'k6/execution';
import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { Counter, Rate } from 'k6/metrics';

const workload = JSON.parse(open('./workload.json'));
const journeyData = new SharedArray('quote journeys', () => JSON.parse(open('./journeys.json')));

const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const BASE_URL = requiredBaseUrl(__ENV.BASE_URL);
const TARGET = parseTargetUrl(BASE_URL);
const TEST_RUN_ID = __ENV.TEST_RUN_ID || `local-${PROFILE}`;
// Ordered from shortest to longest; each journey includes every step of the ones before it.
const JOURNEY_ORDER = ['quote_only', 'customise', 'traveller_details', 'submit_travellers'];
// Smoke always runs the longest journey so that one iteration exercises every transaction.
const FORCED_JOURNEY_TYPE = (
  __ENV.JOURNEY_TYPE || (PROFILE === 'smoke' ? 'submit_travellers' : '')
).toLowerCase();
// The recorded journey never called /api/config (the app only fetches it when config is missing),
// so it is excluded from the request mix unless explicitly enabled.
const INCLUDE_CONFIG = __ENV.INCLUDE_CONFIG === 'true';
const RATE_MULTIPLIER = positiveNumber(__ENV.RATE_MULTIPLIER, 1, 'RATE_MULTIPLIER');
const THINK_TIME_SCALE = nonNegativeNumber(
  __ENV.THINK_TIME_SCALE,
  PROFILE === 'smoke' ? 0 : 1,
  'THINK_TIME_SCALE',
);

const paths = {
  config: __ENV.CONFIG_PATH || '/api/config',
  createQuote: __ENV.CREATE_QUOTE_PATH || '/api/Quotes/create',
  updateExtras: __ENV.UPDATE_EXTRAS_PATH || '/api/Quotes/extras',
  selectQuote: __ENV.SELECT_QUOTE_PATH || '/api/Quotes/select',
  updateTravellers: __ENV.UPDATE_TRAVELLERS_PATH || '/api/Traveller/update',
  confirmEligibility: __ENV.ELIGIBILITY_PATH || '/api/Quotes/eligibility',
};

validateSafetyGate(TARGET, PROFILE);
validateWorkload(workload, journeyData);

const scenario = scaledScenario(workload.profiles[PROFILE], RATE_MULTIPLIER);
const thresholds = workload.thresholds;

export const options = {
  scenarios: {
    quote_journey: {
      ...scenario,
      exec: 'quoteJourney',
      tags: {
        profile: PROFILE,
        test_run_id: TEST_RUN_ID,
      },
    },
  },
  thresholds: buildThresholds(thresholds),
  tags: {
    application: 'scti-insurance',
    profile: PROFILE,
    test_run_id: TEST_RUN_ID,
  },
  userAgent: `scti-k6-performance-test/${TEST_RUN_ID}`,
};

const journeysStarted = new Counter('journeys_started');
const journeysCompleted = new Counter('journeys_completed');
const businessFailures = new Rate('business_failures');
const journeyFailures = new Rate('journey_failures');

export function quoteJourney() {
  const journey = journeyData[exec.scenario.iterationInTest % journeyData.length];
  const journeyType = chooseJourneyType();
  const metricTags = { journey: journeyType, data_set: journey.name };
  journeysStarted.add(1, metricTags);
  initialiseSession();

  let succeeded = true;

  if (INCLUDE_CONFIG) {
    succeeded = group('01 - load application configuration', () => getApplicationConfig());
  }

  let quote;
  if (succeeded) {
    quote = group('02 - create quote', () => createQuote(journey));
    succeeded = quote !== null;
  }

  if (succeeded && journeyType !== 'quote_only') {
    pacedSleep(workload.thinkTimeSeconds.afterQuote);
    quote = group('03 - customise quote', () => customiseQuote(quote, journey));
    succeeded = quote !== null;
  }

  if (succeeded && reaches(journeyType, 'traveller_details')) {
    pacedSleep(workload.thinkTimeSeconds.beforeTravellerDetails);
    quote = group('04 - continue to traveller details', () => selectQuote(quote, journey));
    succeeded = quote !== null;
  }

  if (succeeded && reaches(journeyType, 'submit_travellers')) {
    pacedSleep(workload.thinkTimeSeconds.fillTravellerForm);
    quote = group('05 - submit traveller details', () => submitTravellers(quote, journey));
    succeeded = quote !== null;
  }

  journeyFailures.add(!succeeded, metricTags);
  if (succeeded) {
    journeysCompleted.add(1, metricTags);
  }
}

function initialiseSession() {
  const siteCookie = __ENV.SITE_COOKIE || 'scti-nz';
  http.cookieJar().set(BASE_URL, 'sc_site', siteCookie);
}

function getApplicationConfig() {
  const response = http.get(urlFor(paths.config), requestParams('get_config', 'GET'));
  const valid = check(
    response,
    {
      'config: status is 200': (result) => result.status === 200,
    },
    { transaction: 'get_config' },
  );

  businessFailures.add(!valid, { transaction: 'get_config' });
  return valid;
}

function createQuote(journey) {
  const insuranceDate = __ENV.INSURANCE_DATE || newZealandToday();
  const departureDate = __ENV.DEPARTURE_DATE || addDays(insuranceDate, journey.departInDays);
  const endDate = addDays(departureDate, journey.tripDays);
  const request = {
    ProductCode: journey.productCode,
    BaseCountry: journey.baseCountry,
    StartDateOfJourney: atUtcMidnight(departureDate),
    StartDateOfInsurance: atUtcMidnight(insuranceDate),
    EndDate: atUtcMidnight(endDate),
    Destinations: journey.destinations,
    AdultsAge: journey.adultsAge,
    NonDependentChildAges: journey.nonDependentChildAges,
    DependentChildAges: journey.dependentChildAges,
    ChildAges: journey.childAges,
  };

  const response = http.post(
    urlFor(paths.createQuote),
    JSON.stringify(request),
    requestParams('create_quote', 'POST'),
  );
  const body = responseJson(response);
  const state = quoteState(body, journey.productCode);
  const valid = check(
    response,
    {
      'create quote: status is 200': (result) => result.status === 200,
      'create quote: response is JSON': () => body !== null,
      'create quote: business result succeeded': () => responseSucceeded(body),
      'create quote: identifiers are present': () => Boolean(state.quoteId && state.policyNumber),
      'create quote: selected premium is positive': () => state.totalPremium > 0,
      'create quote: selected options are present': () =>
        Boolean(state.excess && state.cancellationCover),
    },
    { transaction: 'create_quote' },
  );

  businessFailures.add(!valid, { transaction: 'create_quote' });
  return valid ? state : null;
}

function customiseQuote(initialQuote, journey) {
  const desired = journey.customisation;
  let quote = initialQuote;
  let choices = {
    schsDiscount: quote.isSchsMember,
    excess: quote.excess,
    cancellationCover: quote.cancellationCover,
    mopedCover: quote.mopedCover,
    cruiseCover: quote.cruiseCover,
    skiCover: quote.skiCover,
  };

  const extraCover = extraCoverFlags(desired.extraCover);
  const changes = [
    { schsDiscount: Boolean(desired.schsDiscount) },
    { excess: money(desired.excess) },
    { cancellationCover: money(desired.cancellationCover) },
    extraCover,
  ];

  for (let index = 0; index < changes.length; index += 1) {
    choices = { ...choices, ...changes[index] };
    const request = extrasRequest(quote, choices);
    const response = http.put(
      urlFor(paths.updateExtras),
      JSON.stringify(request),
      requestParams('update_extras', 'PUT'),
    );
    const body = responseJson(response);
    const updated = quoteState(body, journey.productCode);
    const valid = check(
      response,
      {
        'update extras: status is 200': (result) => result.status === 200,
        'update extras: response is JSON': () => body !== null,
        'update extras: business result succeeded': () => responseSucceeded(body),
        'update extras: quote identity is preserved': () =>
          updated.quoteId === quote.quoteId && updated.policyNumber === quote.policyNumber,
        'update extras: selected premium is positive': () => updated.totalPremium > 0,
        'update extras: requested options are applied': () => optionsApplied(updated, choices),
      },
      { transaction: 'update_extras' },
    );

    businessFailures.add(!valid, { transaction: 'update_extras' });
    if (!valid) {
      return null;
    }

    quote = updated;
    if (index < changes.length - 1) {
      pacedSleep(workload.thinkTimeSeconds.betweenCustomisations);
    }
  }

  return quote;
}

function selectQuote(quote, journey) {
  const desired = journey.customisation;
  const request = selectRequest(quote, {
    schsDiscount: Boolean(desired.schsDiscount),
    excess: money(desired.excess),
    cancellationCover: money(desired.cancellationCover),
    ...extraCoverFlags(desired.extraCover),
  });
  const response = http.put(
    urlFor(paths.selectQuote),
    JSON.stringify(request),
    requestParams('select_quote', 'PUT'),
  );
  const body = responseJson(response);
  const selected = quoteState(body, journey.productCode);
  const valid = check(
    response,
    {
      'select quote: status is 200': (result) => result.status === 200,
      'select quote: response is JSON': () => body !== null,
      'select quote: business result succeeded': () => responseSucceeded(body),
      'select quote: quote identity is preserved': () =>
        selected.quoteId === quote.quoteId && selected.policyNumber === quote.policyNumber,
      'select quote: selected premium is positive': () => selected.totalPremium > 0,
      'select quote: travellers are returned': () => selected.travellers.length > 0,
    },
    { transaction: 'select_quote' },
  );

  businessFailures.add(!valid, { transaction: 'select_quote' });
  return valid ? selected : null;
}

// Recorded behaviour of "Next" on the Traveller details page: PUT /api/Traveller/update followed
// immediately by PUT /api/Quotes/eligibility, with no user think time between the two calls.
function submitTravellers(quote, journey) {
  const travellersRequest = {
    QuoteId: quote.quoteId,
    PolicyNumber: quote.policyNumber,
    Travellers: quote.travellers.map((traveller) => travellerRequest(traveller)),
  };
  const updateResponse = http.put(
    urlFor(paths.updateTravellers),
    JSON.stringify(travellersRequest),
    requestParams('update_travellers', 'PUT'),
  );
  const updateBody = responseJson(updateResponse);
  const updated = quoteState(updateBody, journey.productCode);
  const updateValid = check(
    updateResponse,
    {
      'update travellers: status is 200': (result) => result.status === 200,
      'update travellers: response is JSON': () => updateBody !== null,
      'update travellers: business result succeeded': () => responseSucceeded(updateBody),
      'update travellers: quote identity is preserved': () =>
        updated.quoteId === quote.quoteId && updated.policyNumber === quote.policyNumber,
      'update travellers: every traveller is kept': () =>
        updated.travellers.length === quote.travellers.length,
    },
    { transaction: 'update_travellers' },
  );

  businessFailures.add(!updateValid, { transaction: 'update_travellers' });
  if (!updateValid) {
    return null;
  }

  const eligibilityRequest = {
    QuoteId: quote.quoteId,
    PolicyNumber: quote.policyNumber,
    AlreadyOverseas: false,
    JourneyStarted: false,
  };
  const eligibilityResponse = http.put(
    urlFor(paths.confirmEligibility),
    JSON.stringify(eligibilityRequest),
    requestParams('confirm_eligibility', 'PUT'),
  );
  const eligibilityBody = responseJson(eligibilityResponse);
  const confirmed = quoteState(eligibilityBody, journey.productCode);
  const eligibilityValid = check(
    eligibilityResponse,
    {
      'confirm eligibility: status is 200': (result) => result.status === 200,
      'confirm eligibility: response is JSON': () => eligibilityBody !== null,
      'confirm eligibility: business result succeeded': () => responseSucceeded(eligibilityBody),
      'confirm eligibility: quote identity is preserved': () =>
        confirmed.quoteId === quote.quoteId && confirmed.policyNumber === quote.policyNumber,
    },
    { transaction: 'confirm_eligibility' },
  );

  businessFailures.add(!eligibilityValid, { transaction: 'confirm_eligibility' });
  return eligibilityValid ? confirmed : null;
}

// The main policyholder shape is taken from the recording. Child travellers were not submitted in
// the recording, so they reuse the same shape with empty contact fields; confirm this against the
// performance environment with a smoke run before any load is applied.
function travellerRequest(traveller) {
  const synthetic = workload.syntheticTraveller;
  const unique = `vu${exec.vu.idInTest}-it${exec.scenario.iterationInTest}`;
  const main = traveller.isMainPolicyHolder;
  return {
    mainPolicyholder: main,
    RiskId: traveller.id,
    Title: main ? synthetic.title : '',
    FirstName: synthetic.firstName,
    LastName: `${synthetic.lastNamePrefix}${traveller.id}`,
    Email: main ? `perf.${unique}@${synthetic.emailDomain}` : '',
    Phone: main ? synthetic.phone : '',
    DateOfBirth: atUtcMidnight(dateOfBirthForAge(traveller.age)),
    Address: main ? synthetic.address : '',
    Suburb: main ? synthetic.suburb : '',
    City: main ? synthetic.city : '',
    State: main ? synthetic.state : '',
    Country: main ? synthetic.country : '',
    Postcode: main ? synthetic.postcode : '',
    Type: traveller.type,
  };
}

// Six months past the birthday, so the derived age matches the quoted age for the whole trip window.
function dateOfBirthForAge(age) {
  const reference = parseDate(__ENV.INSURANCE_DATE || newZealandToday());
  reference.setUTCDate(1);
  reference.setUTCMonth(reference.getUTCMonth() - 6);
  reference.setUTCFullYear(reference.getUTCFullYear() - Number(age));
  return formatDate(reference);
}

function optionsApplied(quote, choices) {
  return (
    quote.excess === choices.excess &&
    quote.cancellationCover === choices.cancellationCover &&
    quote.isSchsMember === choices.schsDiscount &&
    quote.mopedCover === choices.mopedCover &&
    quote.cruiseCover === choices.cruiseCover &&
    quote.skiCover === choices.skiCover
  );
}

// The API returns display values ("$2,500") but the recorded requests send digits only ("2500").
function money(value) {
  return String(value === undefined || value === null ? '' : value)
    .replace(/\.\d+$/, '')
    .replace(/[^0-9]/g, '');
}

function reaches(journeyType, step) {
  return JOURNEY_ORDER.indexOf(journeyType) >= JOURNEY_ORDER.indexOf(step);
}

function buildThresholds(limits) {
  const result = {
    checks: [`rate>${limits.checksRate}`],
    business_failures: [`rate<${limits.errorRate}`],
    journey_failures: [`rate<${limits.errorRate}`],
    dropped_iterations: ['count==0'],
    http_req_failed: [
      `rate<${limits.errorRate}`,
      {
        threshold: `rate<${limits.abortErrorRate}`,
        abortOnFail: true,
        delayAbortEval: limits.abortAfter,
      },
    ],
  };

  const transactions = {
    create_quote: 'createQuote',
    update_extras: 'updateExtras',
    select_quote: 'selectQuote',
    update_travellers: 'updateTravellers',
    confirm_eligibility: 'confirmEligibility',
  };
  if (INCLUDE_CONFIG) {
    transactions.get_config = 'getConfig';
  }
  Object.keys(transactions).forEach((transaction) => {
    result[`http_req_duration{transaction:${transaction}}`] = [
      `p(95)<${limits.p95Milliseconds[transactions[transaction]]}`,
      `p(99)<${limits.p99Milliseconds}`,
    ];
  });
  return result;
}

function extrasRequest(quote, choices) {
  return {
    QuoteId: quote.quoteId,
    PolicyNumber: quote.policyNumber,
    Excess: choices.excess,
    CancellationCover: choices.cancellationCover,
    ProductCode: quote.productCode,
    SchsDiscount: choices.schsDiscount,
    MopedCover: choices.mopedCover,
    CruiseCover: choices.cruiseCover,
    SkiCover: choices.skiCover,
  };
}

function selectRequest(quote, choices) {
  return {
    ProductCode: quote.productCode,
    QuoteId: quote.quoteId,
    PolicyNumber: quote.policyNumber,
    Excess: choices.excess,
    CancellationCover: choices.cancellationCover,
    IsSchsMember: choices.schsDiscount,
    MopedCover: choices.mopedCover,
    CruiseCover: choices.cruiseCover,
    SkiCover: choices.skiCover,
  };
}

function quoteState(body, productCode) {
  const premiums = property(body, 'Premiums', 'premiums') || [];
  const premium = premiums.find(
    (candidate) => property(candidate, 'PolicyTypeCode', 'policyTypeCode') === productCode,
  ) || {};

  return {
    quoteId: property(body, 'QuoteId', 'quoteId') || '',
    policyNumber: property(body, 'PolicyNumber', 'policyNumber') || '',
    productCode,
    excess: money(property(premium, 'SelectedExcess', 'selectedExcess')),
    cancellationCover: money(
      property(premium, 'SelectedCancellationCover', 'selectedCancellationCover'),
    ),
    isSchsMember: Boolean(property(premium, 'IsSchsMember', 'isSchsMember')),
    mopedCover: Boolean(property(premium, 'HasMopedCover', 'hasMopedCover')),
    cruiseCover: Boolean(property(premium, 'HasCruiseCover', 'hasCruiseCover')),
    skiCover: Boolean(property(premium, 'HasSkiCover', 'hasSkiCover')),
    totalPremium: Number(property(premium, 'TotalPremium', 'totalPremium') || 0),
    travellers: (property(body, 'Travellers', 'travellers') || [])
      .filter((traveller) => property(traveller, 'IsActive', 'isActive') !== false)
      .map((traveller) => ({
        id: property(traveller, 'Id', 'id'),
        type: property(traveller, 'Type', 'type'),
        age: property(traveller, 'Age', 'age'),
        isMainPolicyHolder: property(traveller, 'IsMainPolicyHolder', 'isMainPolicyHolder') === true,
      })),
  };
}

function responseSucceeded(body) {
  return body !== null && property(body, 'IsSuccess', 'isSuccess') === true;
}

function property(value, pascalCaseName, camelCaseName) {
  if (!value || typeof value !== 'object') {
    return undefined;
  }
  return value[pascalCaseName] !== undefined ? value[pascalCaseName] : value[camelCaseName];
}

function responseJson(response) {
  try {
    return response.json();
  } catch (_) {
    return null;
  }
}

function chooseJourneyType() {
  if (FORCED_JOURNEY_TYPE) {
    return FORCED_JOURNEY_TYPE;
  }

  const mix = workload.journeyMix;
  const weights = [
    mix.quoteOnlyPercent,
    mix.customisePercent,
    mix.travellerDetailsPercent,
    mix.submitTravellersPercent,
  ];
  let roll = Math.random() * 100;
  for (let index = 0; index < weights.length; index += 1) {
    if (roll < weights[index]) {
      return JOURNEY_ORDER[index];
    }
    roll -= weights[index];
  }
  return JOURNEY_ORDER[JOURNEY_ORDER.length - 1];
}

function extraCoverFlags(extraCover) {
  const normalised = String(extraCover || '').toLowerCase();
  return {
    mopedCover: normalised.includes('moped') || normalised.includes('motorbike'),
    cruiseCover: normalised.includes('cruise'),
    skiCover: normalised.includes('ski') || normalised.includes('snowboard'),
  };
}

function requestParams(transaction, method) {
  const headers = {
    Accept: 'application/json',
    'Content-Type': 'application/json',
    Origin: TARGET.origin,
    Referer: `${BASE_URL}/`,
  };

  if (__ENV.AUTH_TOKEN) {
    headers.Authorization = `Bearer ${__ENV.AUTH_TOKEN}`;
  }
  if (__ENV.TEST_RUN_ID) {
    headers['X-Test-Run-Id'] = __ENV.TEST_RUN_ID;
  }

  return {
    headers,
    redirects: 0,
    tags: {
      name: `${method} ${pathFromUrl(urlFor(paths[transactionPathKey(transaction)]))}`,
      transaction,
    },
    timeout: __ENV.REQUEST_TIMEOUT || '30s',
  };
}

function transactionPathKey(transaction) {
  const keys = {
    get_config: 'config',
    create_quote: 'createQuote',
    update_extras: 'updateExtras',
    select_quote: 'selectQuote',
    update_travellers: 'updateTravellers',
    confirm_eligibility: 'confirmEligibility',
  };
  return keys[transaction];
}

function urlFor(path) {
  return `${BASE_URL}/${String(path).replace(/^\/+/, '')}`;
}

function pathFromUrl(value) {
  const withoutOrigin = value.replace(/^https?:\/\/[^/]+/i, '');
  return withoutOrigin.split(/[?#]/)[0] || '/';
}

function pacedSleep(range) {
  if (THINK_TIME_SCALE === 0) {
    return;
  }
  const duration = range.min + Math.random() * (range.max - range.min);
  sleep(duration * THINK_TIME_SCALE);
}

function newZealandToday() {
  const now = new Date();
  const year = now.getUTCFullYear();
  const daylightSavingEnds = nzTransitionUtc(year, 3, false);
  const daylightSavingStarts = nzTransitionUtc(year, 8, true);
  const offsetHours = now < daylightSavingEnds || now >= daylightSavingStarts ? 13 : 12;
  const local = new Date(now.getTime() + offsetHours * 60 * 60 * 1000);
  return formatDate(local);
}

function nzTransitionUtc(year, monthIndex, lastSunday) {
  let day;
  if (lastSunday) {
    const finalDay = new Date(Date.UTC(year, monthIndex + 1, 0)).getUTCDate();
    const weekday = new Date(Date.UTC(year, monthIndex, finalDay)).getUTCDay();
    day = finalDay - weekday;
  } else {
    const firstWeekday = new Date(Date.UTC(year, monthIndex, 1)).getUTCDay();
    day = 1 + ((7 - firstWeekday) % 7);
  }

  // NZ transitions occur on Sunday local time; both map to 14:00 UTC on Saturday.
  return new Date(Date.UTC(year, monthIndex, day - 1, 14, 0, 0));
}

function addDays(date, days) {
  const parsed = parseDate(date);
  parsed.setUTCDate(parsed.getUTCDate() + Number(days));
  return formatDate(parsed);
}

function atUtcMidnight(date) {
  return `${date}T00:00:00.000Z`;
}

function parseDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new Error(`Expected a date in YYYY-MM-DD format, received '${value}'.`);
  }
  return new Date(`${value}T00:00:00.000Z`);
}

function formatDate(value) {
  const year = value.getUTCFullYear();
  const month = String(value.getUTCMonth() + 1).padStart(2, '0');
  const day = String(value.getUTCDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function requiredBaseUrl(rawValue) {
  if (!rawValue) {
    throw new Error('BASE_URL is required, for example BASE_URL=https://performance.example.test.');
  }

  if (!/^https?:\/\/[^\s/?#]+(?:\/[^\s?#]*)?\/?$/i.test(rawValue) || rawValue.includes('@')) {
    throw new Error('BASE_URL must use HTTP(S) and must not contain credentials.');
  }

  return rawValue.replace(/\/+$/, '');
}

function parseTargetUrl(value) {
  const match = /^(https?):\/\/([^/]+)/i.exec(value);
  if (!match) {
    throw new Error(`BASE_URL must be an absolute HTTP(S) URL, received '${value}'.`);
  }

  const authority = match[2];
  const hostname = authority.startsWith('[')
    ? authority.slice(1, authority.indexOf(']'))
    : authority.split(':')[0];
  return {
    hostname,
    origin: `${match[1].toLowerCase()}://${authority}`,
  };
}

function validateSafetyGate(target, profile) {
  const hostname = target.hostname.toLowerCase().replace(/\.+$/, '');
  const productionHosts = ['insure.scti.co.nz', 'scti.co.nz', 'www.scti.co.nz'];
  if (productionHosts.includes(hostname)) {
    throw new Error(
      `Refusing to target production host '${hostname}'. Use a dedicated performance environment.`,
    );
  }

  // Allowlist: a host must be added to workload.json (a reviewed change) before any profile runs.
  const allowedHosts = ((workload.safety || {}).allowedHosts || []).map((host) =>
    String(host).toLowerCase(),
  );
  if (!allowedHosts.includes(hostname)) {
    throw new Error(
      `Host '${hostname}' is not in safety.allowedHosts in workload.json. Add the approved ` +
        'performance host there before running any profile.',
    );
  }

  if (profile !== 'smoke' && __ENV.PERF_TEST_APPROVED !== 'true') {
    throw new Error(
      `PROFILE=${profile} requires PERF_TEST_APPROVED=true after the target and test window are authorised.`,
    );
  }
}

function validateWorkload(model, data) {
  if (!model.profiles[PROFILE]) {
    throw new Error(
      `Unknown PROFILE '${PROFILE}'. Expected one of: ${Object.keys(model.profiles).join(', ')}.`,
    );
  }

  const total =
    model.journeyMix.quoteOnlyPercent +
    model.journeyMix.customisePercent +
    model.journeyMix.travellerDetailsPercent +
    model.journeyMix.submitTravellersPercent;
  if (total !== 100) {
    throw new Error(`Journey mix must total 100%; it currently totals ${total}%.`);
  }
  if (data.length === 0) {
    throw new Error('journeys.json must contain at least one data row.');
  }

  const allowedJourneyTypes = JOURNEY_ORDER;
  if (FORCED_JOURNEY_TYPE && !allowedJourneyTypes.includes(FORCED_JOURNEY_TYPE)) {
    throw new Error(
      `JOURNEY_TYPE must be one of: ${allowedJourneyTypes.join(', ')}.`,
    );
  }
}

function scaledScenario(profile, multiplier) {
  const result = JSON.parse(JSON.stringify(profile));
  if (multiplier === 1 || result.timeUnit === undefined) {
    return result;
  }
  if (result.timeUnit !== '1s') {
    throw new Error("RATE_MULTIPLIER requires arrival-rate profiles to use timeUnit '1s'.");
  }

  // k6 accepts whole-number rates only, so a scaled rate is expressed per 100 seconds:
  // 5/s x 0.5 becomes 250 per 100s (exactly 2.5/s) instead of being rounded to 3/s.
  result.timeUnit = '100s';
  if (result.rate !== undefined) {
    result.rate = scaledRate(result.rate, multiplier);
  }
  if (result.startRate !== undefined) {
    result.startRate = scaledRate(result.startRate, multiplier);
  }
  if (result.stages) {
    result.stages = result.stages.map((stage) => ({
      ...stage,
      target: scaledRate(stage.target, multiplier),
    }));
  }
  if (result.rate === 0) {
    throw new Error('RATE_MULTIPLIER is too small: the scaled arrival rate rounds to zero.');
  }
  if (result.preAllocatedVUs !== undefined) {
    result.preAllocatedVUs = Math.ceil(result.preAllocatedVUs * multiplier);
  }
  if (result.maxVUs !== undefined) {
    result.maxVUs = Math.ceil(result.maxVUs * multiplier);
  }
  return result;
}

function scaledRate(value, multiplier) {
  return Math.round(Number(value) * multiplier * 100);
}

function positiveNumber(rawValue, fallback, name) {
  const value = rawValue === undefined ? fallback : Number(rawValue);
  if (!Number.isFinite(value) || value <= 0) {
    throw new Error(`${name} must be a positive number.`);
  }
  return value;
}

function nonNegativeNumber(rawValue, fallback, name) {
  const value = rawValue === undefined ? fallback : Number(rawValue);
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`${name} must be zero or a positive number.`);
  }
  return value;
}
