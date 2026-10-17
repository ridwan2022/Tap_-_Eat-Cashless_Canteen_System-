import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');
const loginTrend = new Trend('login_duration');

// Target URL
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5095';

// Test profiles (smoke, load, stress, spike)
const profiles = {
  smoke: {
    vus: 1,
    duration: '30s',
    thresholds: {
      http_req_failed: ['rate<0.01'],   // Max 1% error rate
      http_req_duration: ['p(95)<500'], // 95% requests under 500ms
    },
  },
  load: {
    stages: [
      { duration: '2m', target: 50 },  // Ramp-up
      { duration: '5m', target: 50 },  // Steady load
      { duration: '2m', target: 0 },   // Ramp-down
    ],
    thresholds: {
      http_req_failed: ['rate<0.01'],
      http_req_duration: ['p(95)<800'],
    },
  },
  stress: {
    stages: [
      { duration: '2m', target: 50 },
      { duration: '3m', target: 100 },
      { duration: '3m', target: 200 },
      { duration: '3m', target: 300 }, // Overload limit
      { duration: '2m', target: 0 },   // Recovery phase
    ],
    thresholds: {
      http_req_failed: ['rate<0.10'],
      http_req_duration: ['p(95)<2000'],
    },
  },
  spike: {
    stages: [
      { duration: '10s', target: 10 },
      { duration: '1m',  target: 500 }, // Traffic surge
      { duration: '10s', target: 10 },
      { duration: '3m',  target: 10 },  // Recovery check
      { duration: '10s', target: 0 },
    ],
    thresholds: {
      http_req_failed: ['rate<0.15'],
      http_req_duration: ['p(95)<3000'],
    },
  },
};

// Select profile from CLI flag (-e PROFILE=...)
export const options = profiles[__ENV.PROFILE || 'smoke'];

export default function () {
  const jsonHeaders = { 'Content-Type': 'application/json' };

  // 1. Home Page Request
  const homeRes = http.get(`${BASE_URL}/`);
  const homeOk = check(homeRes, {
    'home: status 200': (r) => r.status === 200,
  });
  if (!homeOk) {
    errorRate.add(1);
    console.log(`[Home Failed] Status: ${homeRes.status}`);
  }
  sleep(1);

  // 2. Auth Login API
  const loginPayload = JSON.stringify({
    email: 'admin@tapandeat.local',
    password: 'Admin@12345',
  });

  const loginRes = http.post(`${BASE_URL}/api/auth/login`, loginPayload, {
    headers: jsonHeaders,
  });
  loginTrend.add(loginRes.timings.duration);

  const loginOk = check(loginRes, {
    'login: status 2xx': (r) => r.status >= 200 && r.status < 300,
  });
  if (!loginOk) {
    errorRate.add(1);
    console.log(`[Login Failed] Status: ${loginRes.status} | Body: ${loginRes.body}`);
  }
  sleep(1);

  // 3. Menu API Request
  const menuRes = http.get(`${BASE_URL}/api/menu`);
  const menuOk = check(menuRes, {
    'menu: status 200': (r) => r.status === 200,
  });
  if (!menuOk) {
    errorRate.add(1);
    console.log(`[Menu Failed] Status: ${menuRes.status} | Body: ${menuRes.body}`);
  }
  sleep(1);
}

// Generate summary report (stdout & summary.json)
export function handleSummary(data) {
  return {
    'stdout': JSON.stringify(
      {
        profile: __ENV.PROFILE || 'smoke',
        total_requests: data.metrics.http_reqs?.values?.count,
        failed_rate: data.metrics.http_req_failed?.values?.rate,
        p95_duration: data.metrics.http_req_duration?.values?.['p(95)'],
        error_rate: data.metrics.errors?.values?.rate,
      },
      null,
      2
    ),
    'summary.json': JSON.stringify(data, null, 2),
  };
}