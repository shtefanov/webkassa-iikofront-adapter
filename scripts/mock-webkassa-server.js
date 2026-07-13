#!/usr/bin/env node

const { createMockWebkassaServer } = require('../src/mock-webkassa-server');

const port = Number(process.env.WEBKASSA_MOCK_PORT || process.argv[2] || 18080);
const server = createMockWebkassaServer();

server.listen(port, '127.0.0.1', () => {
  // eslint-disable-next-line no-console
  console.log(`Mock Webkassa server listening on http://127.0.0.1:${port}`);
});

process.on('SIGINT', () => {
  server.close(() => process.exit(0));
});
