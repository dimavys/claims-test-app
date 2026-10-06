// Generates claims-api.postman_collection.json. Edit THIS file, then `npm run build:collection`.
// Requests share state through collection variables (tokens, ids), so the order below matters.
import { writeFileSync } from 'node:fs';

const AUTH = (who) => who === 'none'
  ? { type: 'noauth' }
  : { type: 'bearer', bearer: [{ key: 'token', value: `{{${who}Token}}`, type: 'string' }] };

const script = (listen, lines) => ({ listen, script: { type: 'text/javascript', exec: lines.flat() } });

/** One request. `tests`/`pre` are arrays of JS lines; `who` picks the bearer token (or 'none'). */
function req(name, method, path, { who = 'handler', body, tests = [], pre = [], headers = {}, form } = {}) {
  const item = {
    name,
    event: [],
    request: {
      method,
      auth: AUTH(who),
      header: Object.entries(headers).map(([key, value]) => ({ key, value })),
      url: path.startsWith('http') || path.startsWith('{{downloadUrl') ? path : `{{baseUrl}}${path}`,
    },
  };
  if (body !== undefined) {
    item.request.header.push({ key: 'Content-Type', value: 'application/json' });
    item.request.body = { mode: 'raw', raw: typeof body === 'string' ? body : JSON.stringify(body, null, 2) };
  }
  if (form) item.request.body = { mode: 'formdata', formdata: form };
  if (pre.length) item.event.push(script('prerequest', pre));
  if (tests.length) item.event.push(script('test', tests));
  return item;
}

const folder = (name, item, description) => ({ name, description, item });

// ---------------------------------------------------------------- reusable assertions
const status = (code) => `pm.test('status is ${code}', () => pm.response.to.have.status(${code}));`;
const json = `const body = pm.response.json();`;
const errorShape = (type) => [
  `pm.test('uses the structured error body (${type})', () => {`,
  `  const e = pm.response.json();`,
  `  pm.expect(e.type).to.eql('${type}');`,
  `  pm.expect(e.status).to.eql(pm.response.code);`,
  `  pm.expect(e.title).to.be.a('string').and.not.empty;`,
  `  pm.expect(e.correlationId).to.match(/^[0-9a-f-]{36}$/);`,
  `});`,
];
const setVar = (name, expr) => `pm.collectionVariables.set('${name}', ${expr});`;

const claimBody = (policyVar, extra = {}) => JSON.stringify({
  policyId: policyVar ? `{{${policyVar}}}` : null,
  lossDate: '{{lossDate}}',
  lossDescription: 'Truck collided with a guardrail on the highway',
  causeOfLossCode: 'COL-VEH-COL',
  lossLocation: 'Highway 9',
  estimatedLossAmount: 12000,
  parties: [{ partyRole: 'Claimant', partyType: 'Person', firstName: 'Ada', lastName: 'Lovelace', email: 'ada@example.com' }],
  riskObjects: [{ assetType: 'Vehicle', assetDescription: 'Volvo FH16' }],
  ...extra,
}, null, 2);

const login = (label, who) => req(`Login as ${label}`, 'POST', '/api/auth/login', {
  who: 'none',
  body: { userName: `{{${who}User}}`, password: `{{${who}Password}}` },
  tests: [
    status(200), json,
    `pm.test('returns a JWT and the ${label} role', () => {`,
    `  pm.expect(body.accessToken.split('.')).to.have.lengthOf(3);`,
    `  pm.expect(body.user.role).to.eql('${label}');`,
    `});`,
    setVar(`${who}Token`, 'body.accessToken'),
    setVar(`${who}Id`, 'body.user.id'),
  ],
});

const reserveBody = (component, amount, reason = 'Newman reserve', type = 'Add') => ({ component, amount, changeReason: reason, transactionType: type });
const approve = (name, who, txnVar, tests) => req(name, 'POST', `/api/claims/{{claimId}}/reserves/{{${txnVar}}}/approve`, { who, tests });

// ---------------------------------------------------------------- collection
const collection = {
  info: {
    name: 'Claims Module API',
    description: 'End-to-end API tests: auth, FNOL, lifecycle, reserve authority, GL jobs, audit, documents and error handling.\nRun with `npm test` in tests/postman (API must be running).',
    schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
  },
  event: [
    script('prerequest', [
      '// A loss date two days ago: inside the seeded Meridian policy period (2024-01-01 → 2026-12-31).',
      '// Generated once per run so repeated requests carry identical bodies (the idempotency tests rely on it).',
      `if (!pm.collectionVariables.get('lossDate')) pm.collectionVariables.set('lossDate', new Date(Date.now() - 2 * 86400000).toISOString());`,
    ]),
  ],
  variable: [{ key: 'glPolls', value: '0' }],
  item: [
    folder('1. Health & authentication', [
      req('Health check', 'GET', '/health', { who: 'none', tests: [status(200), json, `pm.test('is healthy', () => pm.expect(body.status).to.eql('Healthy'));`] }),
      login('handler', 'handler'),
      login('handler', 'handler2'),
      login('supervisor', 'supervisor'),
      login('manager', 'manager'),
      req('Login with a wrong password is 401', 'POST', '/api/auth/login', {
        who: 'none', body: { userName: 'handler', password: 'wrong' }, tests: [status(401), ...errorShape('Unauthorized')],
      }),
      req('Protected endpoint without a token is 401', 'GET', '/api/claims', { who: 'none', tests: [status(401), ...errorShape('Unauthorized')] }),
      req('Who am I', 'GET', '/api/auth/me', { tests: [status(200), json, `pm.test('is the handler', () => pm.expect(body.userName).to.eql('handler'));`] }),
    ]),

    folder('2. Reference data', [
      req('Cause of loss codes (10 active)', 'GET', '/api/reference/cause-of-loss-codes', {
        tests: [status(200), json, `pm.test('10 codes', () => pm.expect(body).to.have.lengthOf(10));`, `pm.test('includes COL-FIRE', () => pm.expect(body.map(c => c.code)).to.include('COL-FIRE'));`],
      }),
      req('Cause of loss codes filtered by peril', 'GET', '/api/reference/cause-of-loss-codes?perilCategory=Auto', {
        tests: [status(200), json, `pm.test('only the 2 auto codes', () => pm.expect(body.map(c => c.code)).to.have.members(['COL-VEH-COL', 'COL-VEH-COMP']));`],
      }),
      req('Claim statuses with valid transitions', 'GET', '/api/reference/claim-statuses', {
        tests: [
          status(200), json,
          `pm.test('7 statuses', () => pm.expect(body).to.have.lengthOf(7));`,
          `const next = (s) => body.find(x => x.status === s).validNextStatuses.map(n => n.status);`,
          `pm.test('Draft can only go to Open', () => pm.expect(next('Draft')).to.eql(['Open']));`,
          `pm.test('Withdrawn is terminal', () => pm.expect(next('Withdrawn')).to.be.empty);`,
          `pm.test('only reopening needs a supervisor', () => pm.expect(body.find(x => x.status === 'Closed').validNextStatuses[0].minimumRole).to.eql('Supervisor'));`,
        ],
      }),
      req('Search active policy (Meridian)', 'GET', '/api/policies/search?q=meridian', {
        tests: [
          status(200), json,
          `pm.test('finds POL-2024-001001', () => { pm.expect(body).to.have.lengthOf(1); pm.expect(body[0].policyNumber).to.eql('POL-2024-001001'); pm.expect(body[0].status).to.eql('Active'); });`,
          setVar('policyId', 'body[0].id'),
        ],
      }),
      req('Search expired policy (Archived Corp)', 'GET', '/api/policies/search?q=POL-2023-000099', {
        tests: [status(200), json, `pm.test('is expired', () => pm.expect(body[0].status).to.eql('Expired'));`, setVar('expiredPolicyId', 'body[0].id')],
      }),
      req('Policy coverage', 'GET', '/api/policies/{{policyId}}/coverage', {
        tests: [status(200), json, `pm.test('Vehicle and Cargo', () => pm.expect(body.coverageTypes).to.eql(['Vehicle', 'Cargo']));`],
      }),
      req('Unknown policy coverage is 404', 'GET', '/api/policies/00000000-0000-0000-0000-000000000000/coverage', { tests: [status(404), ...errorShape('NotFound')] }),
    ]),

    folder('3. FNOL intake (claim A)', [
      req('Dry-run validation reports problems and saves nothing', 'POST', '/api/claims/validate', {
        body: JSON.stringify({ policyId: null, lossDate: '{{lossDate}}', lossDescription: 'tiny', causeOfLossCode: 'COL-FIRE', parties: [], riskObjects: [] }),
        tests: [
          status(200), json,
          `pm.test('critical: description and claimant', () => { pm.expect(body.critical).to.include('Loss description is required and must be at least 20 characters.'); pm.expect(body.critical).to.include('At least one Claimant party is required to open a claim.'); });`,
          `pm.test('warnings: no policy and no risk objects', () => { pm.expect(body.warnings.some(w => w.startsWith('No policy linked'))).to.be.true; pm.expect(body.warnings).to.include('No risk objects are linked to the claim.'); });`,
          `pm.test('is not valid', () => pm.expect(body.isValid).to.be.false);`,
        ],
      }),
      req('Invalid intake is a 422 with every field error', 'POST', '/api/claims', {
        body: { lossDate: '2999-01-01T00:00:00Z', lossDescription: 'x', causeOfLossCode: 'NOPE' },
        tests: [
          status(422), ...errorShape('ValidationError'),
          `pm.test('lists each failing field', () => { const e = pm.response.json().errors; pm.expect(e.LossDate[0]).to.eql('Loss date cannot be in the future.'); pm.expect(e.LossDescription[0]).to.contain('at least 20 characters'); pm.expect(e.CauseOfLossCode[0]).to.contain('not recognised'); });`,
        ],
      }),
      req('Initial reserve without a policy is a 422', 'POST', '/api/claims', {
        body: claimBody(null, { initialReserve: { component: 'Indemnity', amount: 100 } }),
        tests: [status(422), `pm.test('points at PolicyId', () => pm.expect(pm.response.json().errors).to.have.property('PolicyId'));`],
      }),
      req('Create claim A with a 5,000 initial reserve', 'POST', '/api/claims', {
        headers: { 'Idempotency-Key': '{{idemKey}}' },
        pre: [`pm.collectionVariables.set('idemKey', pm.variables.replaceIn('{{$guid}}'));`],
        body: claimBody('policyId', { initialReserve: { component: 'Indemnity', amount: 5000, changeReason: 'Initial reserve' } }),
        tests: [
          status(201), json,
          `pm.test('has a Location header', () => pm.expect(pm.response.headers.get('Location')).to.contain('/api/claims/'));`,
          `pm.test('claim number format CLM-YYYY-0000000', () => pm.expect(body.claimNumber).to.match(/^CLM-\\d{4}-\\d{7}$/));`,
          `pm.test('starts in Draft', () => pm.expect(body.status).to.eql('Draft'));`,
          `pm.test('5,000 is auto-approved (within handler authority)', () => { pm.expect(body.initialReserve.transaction.approvalStatus).to.eql('AutoApproved'); pm.expect(body.initialReserve.requiresApproval).to.be.false; });`,
          `pm.test('no outstanding issues for a complete intake', () => pm.expect(body.validationIssues).to.be.empty);`,
          setVar('claimId', 'body.id'), setVar('claimNumber', 'body.claimNumber'),
          `pm.test('echoes the correlation id header', () => pm.expect(pm.response.headers.get('X-Correlation-Id')).to.match(/^[0-9a-f-]{36}$/));`,
        ],
      }),
      req('Same Idempotency-Key replays the stored response', 'POST', '/api/claims', {
        headers: { 'Idempotency-Key': '{{idemKey}}' },
        body: claimBody('policyId', { initialReserve: { component: 'Indemnity', amount: 5000, changeReason: 'Initial reserve' } }),
        tests: [
          status(201), json,
          `pm.test('marked as replayed', () => pm.expect(pm.response.headers.get('Idempotent-Replayed')).to.eql('true'));`,
          `pm.test('returns the SAME claim, not a new one', () => { pm.expect(body.id).to.eql(pm.collectionVariables.get('claimId')); pm.expect(body.claimNumber).to.eql(pm.collectionVariables.get('claimNumber')); });`,
        ],
      }),
      req('Same key with a different body is rejected', 'POST', '/api/claims', {
        headers: { 'Idempotency-Key': '{{idemKey}}' },
        body: claimBody('policyId', { lossDescription: 'A completely different description of the loss' }),
        tests: [status(422), `pm.test('explains the key was reused', () => pm.expect(pm.response.json().title).to.contain('Idempotency-Key'));`],
      }),
      req('Get claim A detail', 'GET', '/api/claims/{{claimId}}', {
        tests: [
          status(200), json,
          `pm.test('header facts', () => { pm.expect(body.claimNumber).to.eql(pm.collectionVariables.get('claimNumber')); pm.expect(body.policyNumber).to.eql('POL-2024-001001'); pm.expect(body.clientName).to.eql('Meridian Transport LLC'); pm.expect(body.assignedHandlerName).to.eql('Hannah Handler'); });`,
          `pm.test('loss event resolved to a name', () => pm.expect(body.lossEvent.causeOfLossName).to.eql('Vehicle Collision'));`,
          `pm.test('one active claimant and a primary risk object', () => { pm.expect(body.parties.filter(p => p.isActive && p.partyRole === 'Claimant')).to.have.lengthOf(1); pm.expect(body.riskObjects[0].isPrimary).to.be.true; });`,
          `pm.test('only Open is a valid next status', () => pm.expect(body.validNextStatuses.map(s => s.status)).to.eql(['Open']));`,
          setVar('claimantId', `body.parties.find(p => p.partyRole === 'Claimant').id`),
        ],
      }),
      req('List finds claim A by number (string enums, paging)', 'GET', '/api/claims?search={{claimNumber}}&status=Draft&status=Open&pageSize=5', {
        tests: [
          status(200), json,
          `pm.test('exactly one match', () => { pm.expect(body.totalCount).to.eql(1); pm.expect(body.items[0].id).to.eql(pm.collectionVariables.get('claimId')); pm.expect(body.items[0].status).to.eql('Draft'); });`,
          `pm.test('summary columns', () => { const c = body.items[0]; pm.expect(c.policyNumber).to.eql('POL-2024-001001'); pm.expect(c.causeOfLossName).to.eql('Vehicle Collision'); pm.expect(c.totalReserves).to.eql(5000); });`,
        ],
      }),
      req('Bad paging is a 422', 'GET', '/api/claims?pageSize=1000', { tests: [status(422), ...errorShape('ValidationError')] }),
      req('Unknown claim is a 404', 'GET', '/api/claims/00000000-0000-0000-0000-000000000000', { tests: [status(404), ...errorShape('NotFound')] }),
    ]),

    folder('4. Claim B: expired policy warning and withdrawal', [
      req('Create claim B on the expired policy', 'POST', '/api/claims', {
        body: claimBody('expiredPolicyId'),
        tests: [
          status(201), json,
          `pm.test('loss date outside the policy period is an acknowledgeable warning', () => { const i = body.validationIssues.find(x => x.code === 'LOSS_DATE_OUTSIDE_POLICY'); pm.expect(i).to.exist; pm.expect(i.severity).to.eql('Warning'); pm.expect(i.requiresAcknowledgement).to.be.true; });`,
          setVar('claimBId', 'body.id'),
        ],
      }),
      req('Opening without acknowledging the warning is blocked', 'PUT', '/api/claims/{{claimBId}}/status', {
        body: { targetStatus: 'Open' },
        tests: [status(422), `pm.test('says warnings must be acknowledged', () => pm.expect(pm.response.json().blockingConditions.join(' ')).to.contain('acknowledged'));`],
      }),
      req('Opening with acknowledgeWarnings succeeds', 'PUT', '/api/claims/{{claimBId}}/status', {
        body: { targetStatus: 'Open', acknowledgeWarnings: true }, tests: [status(200), json, `pm.test('is Open', () => pm.expect(body.status).to.eql('Open'));`],
      }),
      req('Withdrawing without a reason is blocked', 'PUT', '/api/claims/{{claimBId}}/status', {
        body: { targetStatus: 'Withdrawn' }, tests: [status(422), `pm.test('asks for a withdrawal reason', () => pm.expect(pm.response.json().blockingConditions[0]).to.contain('reason'));`],
      }),
      req('Withdrawing with a reason succeeds', 'PUT', '/api/claims/{{claimBId}}/status', {
        body: { targetStatus: 'Withdrawn', reason: 'Claimant withdrew the claim' }, tests: [status(200), json, `pm.test('is Withdrawn and terminal', () => { pm.expect(body.status).to.eql('Withdrawn'); pm.expect(body.validNextStatuses).to.be.empty; });`],
      }),
    ]),

    folder('5. Claim C: no policy blocks reserves', [
      req('Create claim C without a policy', 'POST', '/api/claims', {
        body: claimBody(null),
        tests: [
          status(201), json,
          `pm.test('records the no-policy warning', () => pm.expect(body.validationIssues.map(i => i.code)).to.include('NO_POLICY'));`,
          setVar('claimCId', 'body.id'),
        ],
      }),
      req('Reserve on a claim without a policy is a 422', 'POST', '/api/claims/{{claimCId}}/reserves', {
        body: reserveBody('Indemnity', 1000), tests: [status(422), `pm.test('says to link a policy first', () => pm.expect(JSON.stringify(pm.response.json().errors)).to.contain('No policy linked'));`],
      }),
    ]),

    folder('6. Claim A lifecycle', [
      req('Draft → Closed is not permitted', 'PUT', '/api/claims/{{claimId}}/status', {
        body: { targetStatus: 'Closed' },
        tests: [
          status(422), ...errorShape('ValidationError'),
          `pm.test('names the transition and lists valid next statuses', () => { const e = pm.response.json(); pm.expect(e.title).to.eql('Transition from Draft to Closed is not permitted.'); pm.expect(e.validNextStatuses).to.eql(['Open']); });`,
        ],
      }),
      req('Draft → Open', 'PUT', '/api/claims/{{claimId}}/status', {
        body: { targetStatus: 'Open' },
        tests: [status(200), json, `pm.test('Draft → Open', () => { pm.expect(body.previousStatus).to.eql('Draft'); pm.expect(body.status).to.eql('Open'); });`],
      }),
      req('Add a witness', 'POST', '/api/claims/{{claimId}}/parties', {
        body: { partyRole: 'Witness', partyType: 'Person', firstName: 'Wes', lastName: 'Ness' },
        tests: [status(201), json, `pm.test('witness added and active', () => { pm.expect(body.partyRole).to.eql('Witness'); pm.expect(body.isActive).to.be.true; });`],
      }),
      req('Removing the last claimant is a 422', 'DELETE', '/api/claims/{{claimId}}/parties/{{claimantId}}', {
        tests: [status(422), `pm.test('explains a claim needs a claimant', () => pm.expect(JSON.stringify(pm.response.json().errors)).to.contain('last Claimant'));`],
      }),
      req('Update notes', 'PUT', '/api/claims/{{claimId}}/notes', { body: { notes: 'Called the claimant; photos requested.' }, tests: [status(204)] }),
      req('Closure pre-flight reports the open balance', 'GET', '/api/claims/{{claimId}}/closure-preflight', {
        tests: [status(200), json, `pm.test('cannot close yet: justification needed', () => { pm.expect(body.canClose).to.be.false; pm.expect(body.requiresJustification).to.be.true; pm.expect(body.openReserveBalance).to.eql(5000); });`],
      }),
    ]),

    folder('7. Reserve authority & approvals (claim A)', [
      req('Handler submits 50,000 → pending approval', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('Indemnity', 50000, 'Estimate after inspection'),
        tests: [
          status(201), json,
          `pm.test('needs supervisor approval and is not applied yet', () => { pm.expect(body.transaction.approvalStatus).to.eql('PendingApproval'); pm.expect(body.requiredAuthority).to.eql('Supervisor approval required'); pm.expect(body.requiresApproval).to.be.true; });`,
          setVar('txn50kId', 'body.transaction.id'), setVar('indemnityComponentId', 'body.transaction.reserveComponentId'),
        ],
      }),
      approve('Handler cannot approve (403)', 'handler', 'txn50kId', [status(403), ...errorShape('Forbidden')]),
      approve('Supervisor approves 50,000', 'supervisor', 'txn50kId', [
        status(200), json,
        `pm.test('approved by the supervisor, balance applied', () => { pm.expect(body.approvalStatus).to.eql('Approved'); pm.expect(body.approvedByName).to.eql('Sam Supervisor'); pm.expect(body.newBalance).to.eql(55000); });`,
      ]),
      approve('Approving it twice is a conflict (409)', 'manager', 'txn50kId', [status(409), ...errorShape('Conflict')]),
      req('Supervisor submits 20,000 (Expense)', 'POST', '/api/claims/{{claimId}}/reserves', {
        who: 'supervisor', body: reserveBody('Expense', 20000, 'Expert fees'),
        tests: [status(201), json, `pm.test('pending even for a supervisor', () => pm.expect(body.transaction.approvalStatus).to.eql('PendingApproval'));`, setVar('txn20kId', 'body.transaction.id')],
      }),
      approve('Supervisor cannot approve their own reserve (422)', 'supervisor', 'txn20kId', [
        status(422), `pm.test('self-approval message', () => pm.expect(pm.response.json().errors.ReserveApproval[0]).to.eql('Self-approval is not permitted.'));`,
      ]),
      approve('Manager approves the supervisor\'s reserve', 'manager', 'txn20kId', [status(200), json, `pm.test('approved by the manager', () => pm.expect(body.approvedByName).to.eql('Maria Manager'));`]),
      req('Handler submits 250,000 → needs a manager', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('Indemnity', 250000, 'Total loss'),
        tests: [status(201), json, `pm.test('manager tier', () => pm.expect(body.requiredAuthority).to.eql('Manager approval required'));`, setVar('txn250kId', 'body.transaction.id')],
      }),
      approve('Supervisor lacks authority for 250,000 (403)', 'supervisor', 'txn250kId', [status(403), ...errorShape('Forbidden')]),
      req('Manager rejects 250,000 with a reason', 'POST', '/api/claims/{{claimId}}/reserves/{{txn250kId}}/reject', {
        who: 'manager', body: { rejectionReason: 'Exceeds the replacement value' },
        tests: [status(200), json, `pm.test('rejected with reason kept', () => { pm.expect(body.approvalStatus).to.eql('Rejected'); pm.expect(body.rejectionReason).to.eql('Exceeds the replacement value'); });`],
      }),
      req('Reject without a reason is a 422', 'POST', '/api/claims/{{claimId}}/reserves/{{txn250kId}}/reject', { who: 'manager', body: { rejectionReason: '' }, tests: [status(422)] }),
      req('Handler submits 30,000 to retract', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('ALAE', 30000, 'Legal costs'),
        tests: [status(201), json, setVar('txnRetractId', 'body.transaction.id')],
      }),
      req('Another handler cannot retract it (403)', 'POST', '/api/claims/{{claimId}}/reserves/{{txnRetractId}}/retract', { who: 'handler2', tests: [status(403), ...errorShape('Forbidden')] }),
      req('The submitter retracts it', 'POST', '/api/claims/{{claimId}}/reserves/{{txnRetractId}}/retract', {
        tests: [status(200), json, `pm.test('cancelled', () => pm.expect(body.approvalStatus).to.eql('Cancelled'));`],
      }),
      req('Zero and negative amounts are rejected', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('Indemnity', 0), tests: [status(422), `pm.test('amount must be greater than zero', () => pm.expect(JSON.stringify(pm.response.json().errors)).to.contain('greater than zero'));`],
      }),
      req('Subrogation may be negative (auto-approved)', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('SubrogationRecoverable', -8000, 'Expected salvage recovery'),
        tests: [status(201), json, `pm.test('negative balance allowed for subrogation', () => { pm.expect(body.transaction.approvalStatus).to.eql('AutoApproved'); pm.expect(body.transaction.newBalance).to.eql(-8000); });`],
      }),
      req('Reverse cannot take a component below zero', 'POST', '/api/claims/{{claimId}}/reserves', {
        body: reserveBody('Expense', 999999, 'Too much', 'Reverse'), tests: [status(422)],
      }),
      req('Adjust an existing component by id (PUT)', 'PUT', '/api/claims/{{claimId}}/reserves/{{indemnityComponentId}}', {
        body: { amount: 1000, changeReason: 'Additional damage found' },
        tests: [status(200), json, `pm.test('recorded as an Adjust', () => { pm.expect(body.transaction.transactionType).to.eql('Adjust'); pm.expect(body.transaction.newBalance).to.eql(56000); });`],
      }),
      req('Only a manager can set the override flag (handler 403)', 'PUT', '/api/claims/{{claimId}}/manager-override', { body: { value: true }, tests: [status(403)] }),
      req('Manager sets the override flag', 'PUT', '/api/claims/{{claimId}}/manager-override', { who: 'manager', body: { value: true }, tests: [status(204)] }),
      req('Reserve summary and history', 'GET', '/api/claims/{{claimId}}/reserves', {
        tests: [
          status(200), json,
          `const c = (n) => body.summary.components.find(x => x.component === n);`,
          `pm.test('balances per component', () => { pm.expect(c('Indemnity').currentAmount).to.eql(56000); pm.expect(c('Expense').currentAmount).to.eql(20000); pm.expect(c('SubrogationRecoverable').currentAmount).to.eql(-8000); });`,
          `pm.test('total 68,000 with nothing pending; override on', () => { pm.expect(body.summary.totalReserves).to.eql(68000); pm.expect(body.summary.totalPending).to.eql(0); pm.expect(body.summary.managerOverride).to.be.true; });`,
          `pm.test('rejected and cancelled entries stay in the history', () => { const s = body.transactions.map(t => t.approvalStatus); pm.expect(s).to.include.members(['Rejected', 'Cancelled', 'Approved', 'AutoApproved']); });`,
        ],
      }),
    ]),

    folder('8. Background jobs & audit trail (claim A)', [
      req('Wait for GL posting jobs (polls up to ~10s)', 'GET', '/api/claims/{{claimId}}/reserves', {
        pre: [`setTimeout(function () {}, 1000); // give the Hangfire worker time between polls`],
        tests: [
          status(200), json,
          `const approved = body.transactions.filter(t => t.approvalStatus === 'Approved' || t.approvalStatus === 'AutoApproved');`,
          `const waiting = approved.filter(t => t.postingStatus === 'Pending').length;`,
          `const polls = Number(pm.collectionVariables.get('glPolls') || 0);`,
          `if (waiting > 0 && polls < 10) {`,
          `  pm.collectionVariables.set('glPolls', polls + 1);`,
          `  postman.setNextRequest(pm.info.requestName);`,
          `} else {`,
          `  pm.test('every approved transaction was posted to the GL', () => { pm.expect(waiting).to.eql(0); approved.forEach(t => pm.expect(t.postingStatus).to.eql('Posted')); });`,
          `  pm.test('posting jobs recorded a Hangfire job id', () => approved.forEach(t => pm.expect(t.postingJobId).to.be.a('string').and.not.empty));`,
          `  pm.test('rejected and cancelled entries were never posted', () => body.transactions.filter(t => ['Rejected', 'Cancelled'].includes(t.approvalStatus)).forEach(t => pm.expect(t.postingStatus).to.eql('Cancelled')));`,
          `}`,
        ],
      }),
      req('Audit log is complete, ordered and attributed', 'GET', '/api/claims/{{claimId}}/audit?pageSize=100', {
        tests: [
          status(200), json,
          `const types = body.items.map(i => i.eventType);`,
          `const count = (t) => types.filter(x => x === t).length;`,
          `pm.test('newest first', () => { const d = body.items.map(i => Date.parse(i.createdAt)); pm.expect(d).to.eql([...d].sort((a, b) => b - a)); });`,
          `pm.test('business events were audited', () => pm.expect(types).to.include.members(['CLAIM_CREATED', 'PARTY_ADDED', 'PARTY_REMOVED'].filter(t => t !== 'PARTY_REMOVED').concat(['STATUS_CHANGED', 'RESERVE_CREATED', 'RESERVE_AUTO_APPROVED', 'RESERVE_APPROVED', 'RESERVE_REJECTED', 'RESERVE_RETRACTED'])));`,
          `pm.test('exactly one GL posting entry per approved transaction (idempotent)', () => pm.expect(count('GL_POSTING_SIMULATED')).to.eql(5));`,
          `pm.test('GL entries are attributed to System with the journal text', () => body.items.filter(i => i.eventType === 'GL_POSTING_SIMULATED').forEach(i => { pm.expect(i.createdByName).to.eql('System'); pm.expect(i.description).to.contain('DR '); pm.expect(i.description).to.contain(' / CR '); }));`,
          `pm.test('the rejection reason is kept', () => pm.expect(body.items.find(i => i.eventType === 'RESERVE_REJECTED').oldValue).to.contain('Exceeds the replacement value'));`,
          `pm.test('one correlation id per request: all entries have one', () => body.items.forEach(i => pm.expect(i.correlationId).to.match(/^[0-9a-f-]{36}$/)));`,
          `pm.test('paging metadata', () => { pm.expect(body.totalCount).to.eql(body.items.length); pm.expect(body.page).to.eql(1); });`,
        ],
      }),
    ]),

    folder('9. Documents (claim A)', [
      req('Upload a text document (path traversal in the name)', 'POST', '/api/claims/{{claimId}}/documents', {
        form: [
          { key: 'file', type: 'file', src: 'fixtures/report.txt' },
          { key: 'documentType', value: 'PoliceReport', type: 'text' },
          { key: 'notes', value: 'Scanned copy', type: 'text' },
        ],
        tests: [
          status(201), json,
          `pm.test('stored as a sanitised name with metadata', () => { pm.expect(body.documentName).to.eql('report.txt'); pm.expect(body.documentType).to.eql('PoliceReport'); pm.expect(body.uploadedByName).to.eql('Hannah Handler'); pm.expect(body.fileSizeBytes).to.be.above(0); });`,
          setVar('documentId', 'body.id'),
        ],
      }),
      req('A disallowed file type is a 422', 'POST', '/api/claims/{{claimId}}/documents', {
        form: [{ key: 'file', type: 'file', src: 'fixtures/malware.exe' }],
        tests: [status(422), `pm.test('names the allowed types', () => pm.expect(JSON.stringify(pm.response.json().errors)).to.contain('not allowed'));`],
      }),
      req('Upload without a file is a 422', 'POST', '/api/claims/{{claimId}}/documents', { form: [{ key: 'documentType', value: 'Other', type: 'text' }], tests: [status(422)] }),
      req('List documents returns a short-lived download URL', 'GET', '/api/claims/{{claimId}}/documents', {
        tests: [
          status(200), json,
          `pm.test('one document with a URL that expires in about an hour', () => { pm.expect(body).to.have.lengthOf(1); pm.expect(body[0].downloadUrl).to.be.a('string').and.not.empty; const mins = (Date.parse(body[0].downloadUrlExpiresAt) - Date.now()) / 60000; pm.expect(mins).to.be.within(55, 61); });`,
          setVar('downloadUrl', 'body[0].downloadUrl'),
        ],
      }),
      req('Download through the signed link (no token needed)', 'GET', '{{downloadUrl}}', {
        who: 'none',
        tests: [
          status(200),
          `pm.test('returns the uploaded bytes', () => pm.expect(pm.response.text()).to.contain('police report: collision on highway 9'));`,
          `pm.test('as an attachment with the clean file name', () => pm.expect(pm.response.headers.get('Content-Disposition')).to.contain('report.txt'));`,
        ],
      }),
      req('A tampered signature is rejected (404)', 'GET', '{{downloadUrl}}0', { who: 'none', tests: [status(404)] }),
    ]),

    folder('10. Close, reopen (claim A)', [
      req('Closing with an open balance and no justification is blocked', 'PUT', '/api/claims/{{claimId}}/status', {
        body: { targetStatus: 'Closed' }, tests: [status(422), `pm.test('asks for a justification', () => pm.expect(pm.response.json().blockingConditions.join(' ')).to.contain('justification'));`],
      }),
      req('Pre-flight with a justification says it can close', 'GET', '/api/claims/{{claimId}}/closure-preflight?closureJustification=Settled', {
        tests: [status(200), json, `pm.test('can close', () => { pm.expect(body.canClose).to.be.true; pm.expect(body.blockers).to.be.empty; });`],
      }),
      req('Close with a justification', 'PUT', '/api/claims/{{claimId}}/status', {
        body: { targetStatus: 'Closed', reason: 'Settled', closureJustification: 'Claimant settled for the reserved amount' },
        tests: [status(200), json, `pm.test('Closed, and only a reopen is possible', () => { pm.expect(body.status).to.eql('Closed'); pm.expect(body.validNextStatuses.map(s => s.status)).to.eql(['Reopened']); });`],
      }),
      req('Reserves are blocked on a closed claim', 'POST', '/api/claims/{{claimId}}/reserves', { body: reserveBody('Indemnity', 100), tests: [status(422)] }),
      req('A handler cannot reopen (403)', 'PUT', '/api/claims/{{claimId}}/status', { body: { targetStatus: 'Reopened', reason: 'New evidence' }, tests: [status(403), ...errorShape('Forbidden')] }),
      req('A supervisor reopens it (lands in Open)', 'PUT', '/api/claims/{{claimId}}/status', {
        who: 'supervisor', body: { targetStatus: 'Reopened', reason: 'New evidence surfaced' },
        tests: [status(200), json, `pm.test('reopened claims go straight back to Open', () => { pm.expect(body.previousStatus).to.eql('Closed'); pm.expect(body.status).to.eql('Open'); });`],
      }),
      req('Closing and reopening are audited', 'GET', '/api/claims/{{claimId}}/audit?pageSize=100', {
        tests: [status(200), json, `pm.test('CLAIM_CLOSED and CLAIM_REOPENED recorded', () => pm.expect(body.items.map(i => i.eventType)).to.include.members(['CLAIM_CLOSED', 'CLAIM_REOPENED']));`],
      }),
    ]),
  ],
};

writeFileSync(new URL('./claims-api.postman_collection.json', import.meta.url), JSON.stringify(collection, null, 2) + '\n');
const count = (items) => items.reduce((n, i) => n + (i.item ? count(i.item) : 1), 0);
console.log(`Wrote claims-api.postman_collection.json (${collection.item.length} folders, ${count(collection.item)} requests)`);
